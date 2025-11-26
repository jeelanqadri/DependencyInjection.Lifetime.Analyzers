using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DependencyInjection.Lifetime.Analyzers.Infrastructure;

/// <summary>
/// Collects service registrations from IServiceCollection extension method calls.
/// </summary>
public sealed class RegistrationCollector
{
    private readonly ConcurrentDictionary<INamedTypeSymbol, ServiceRegistration> _registrations;
    private readonly ConcurrentBag<OrderedRegistration> _orderedRegistrations;
    private readonly INamedTypeSymbol? _serviceCollectionType;
    private int _registrationOrder;

    private RegistrationCollector(INamedTypeSymbol? serviceCollectionType)
    {
        _serviceCollectionType = serviceCollectionType;
        _registrations = new ConcurrentDictionary<INamedTypeSymbol, ServiceRegistration>(SymbolEqualityComparer.Default);
        _orderedRegistrations = new ConcurrentBag<OrderedRegistration>();
        _registrationOrder = 0;
    }

    /// <summary>
    /// Creates a registration collector for the given compilation.
    /// Returns null if IServiceCollection is not available.
    /// </summary>
    public static RegistrationCollector? Create(Compilation compilation)
    {
        var serviceCollectionType = compilation.GetTypeByMetadataName(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection");

        return new RegistrationCollector(serviceCollectionType);
    }

    /// <summary>
    /// Gets all collected registrations.
    /// </summary>
    public IEnumerable<ServiceRegistration> Registrations => _registrations.Values;

    /// <summary>
    /// Gets all ordered registrations for analyzing registration order.
    /// </summary>
    public IEnumerable<OrderedRegistration> OrderedRegistrations => _orderedRegistrations;

    /// <summary>
    /// Tries to get the registration for a specific service type.
    /// </summary>
    public bool TryGetRegistration(INamedTypeSymbol serviceType, out ServiceRegistration? registration)
    {
        return _registrations.TryGetValue(serviceType, out registration);
    }

    /// <summary>
    /// Gets the lifetime for a service type, if registered.
    /// </summary>
    public ServiceLifetime? GetLifetime(ITypeSymbol? serviceType)
    {
        if (serviceType is INamedTypeSymbol namedType &&
            _registrations.TryGetValue(namedType, out var registration))
        {
            return registration.Lifetime;
        }

        return null;
    }

    /// <summary>
    /// Analyzes an invocation expression to detect and record service registrations.
    /// </summary>
    public void AnalyzeInvocation(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // Get the method symbol
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        if (symbolInfo.Symbol is not IMethodSymbol methodSymbol)
        {
            return;
        }

        var isExtension = IsServiceCollectionExtensionMethod(methodSymbol);
        var isAddMethod = IsServiceCollectionAddMethod(methodSymbol);

        // Check if this is an extension method on IServiceCollection OR ICollection.Add
        if (!isExtension && !isAddMethod)
        {
            return;
        }

        // If it's the instance Add method, verify the receiver is IServiceCollection
        if (isAddMethod && !IsReceiverServiceCollection(invocation, semanticModel))
        {
            return;
        }

        var methodName = methodSymbol.Name;
        var isTryAdd = IsTryAddMethod(methodName);

        // Parse the lifetime from method name
        var lifetime = GetLifetimeFromMethodName(methodName);

        INamedTypeSymbol? serviceType;
        INamedTypeSymbol? implementationType;
        ExpressionSyntax? factoryExpression;

        if (lifetime.HasValue)
        {
            // Extract service, implementation types, and factory expression from standard methods
            (serviceType, implementationType, factoryExpression) = ExtractTypes(methodSymbol, invocation, semanticModel);
        }
        else if ((methodName == "Add" || methodName == "TryAdd") && 
                 (isExtension || isAddMethod))
        {
            // Handle Add(ServiceDescriptor)
            (serviceType, implementationType, factoryExpression, lifetime) = ExtractFromServiceDescriptor(invocation, semanticModel);
        }
        else
        {
            return;
        }

        if (serviceType is null || lifetime is null)
        {
            return;
        }

        // Always track ordered registrations (for DI012 analysis)
        var order = Interlocked.Increment(ref _registrationOrder);
        var orderedRegistration = new OrderedRegistration(
            serviceType,
            lifetime.Value,
            invocation.GetLocation(),
            order,
            isTryAdd,
            methodName);
        _orderedRegistrations.Add(orderedRegistration);

        // Store in main registrations dictionary if we have implementation type OR a factory
        // and this is not a TryAdd (TryAdd doesn't override existing registrations)
        if ((implementationType is not null || factoryExpression is not null) && !isTryAdd)
        {
            var registration = new ServiceRegistration(
                serviceType,
                implementationType,
                factoryExpression,
                lifetime.Value,
                invocation.GetLocation());

            // Store by service type (later registrations override earlier ones, like DI container behavior)
            _registrations[serviceType] = registration;
        }
    }

    private bool IsServiceCollectionExtensionMethod(IMethodSymbol method)
    {
        // Get the original definition if this is a reduced extension method
        var originalMethod = method.ReducedFrom ?? method;

        if (!originalMethod.IsExtensionMethod)
        {
            return false;
        }

        // Check if the containing type is ServiceCollectionServiceExtensions or ServiceCollectionDescriptorExtensions
        var containingType = originalMethod.ContainingType;
        if (containingType?.Name != "ServiceCollectionServiceExtensions" &&
            containingType?.Name != "ServiceCollectionDescriptorExtensions")
        {
            return false;
        }

        // Verify the first parameter is IServiceCollection
        if (originalMethod.Parameters.Length == 0)
        {
            return false;
        }

        var firstParam = originalMethod.Parameters[0];
        return firstParam.Type.Name == "IServiceCollection";
    }

    private bool IsServiceCollectionAddMethod(IMethodSymbol method)
    {
        if (method.Name != "Add") return false;
        if (method.Parameters.Length != 1) return false;

        var paramType = method.Parameters[0].Type;
        return paramType.Name == "ServiceDescriptor" &&
               (paramType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                paramType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions");
    }

    private bool IsReceiverServiceCollection(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        if (_serviceCollectionType is null) return false;

        ExpressionSyntax? receiver = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            receiver = memberAccess.Expression;
        }

        if (receiver is null) return false;

        var typeInfo = semanticModel.GetTypeInfo(receiver);
        var type = typeInfo.Type;

        if (type is null) return false;

        // Check if type equals or implements IServiceCollection
        return InheritsFromOrEquals(type, _serviceCollectionType);
    }

    private bool InheritsFromOrEquals(ITypeSymbol type, INamedTypeSymbol baseType)
    {
        if (SymbolEqualityComparer.Default.Equals(type, baseType))
        {
            return true;
        }

        foreach (var iface in type.AllInterfaces)
        {
            if (SymbolEqualityComparer.Default.Equals(iface, baseType))
            {
                return true;
            }
        }

        return false;
    }

    private static ServiceLifetime? GetLifetimeFromMethodName(string methodName)
    {
        // Handle common registration patterns (both Add*, TryAdd*, and keyed variants)
        if (methodName.StartsWith("AddSingleton") || methodName.StartsWith("TryAddSingleton") ||
            methodName.StartsWith("AddKeyedSingleton") || methodName.StartsWith("TryAddKeyedSingleton"))
        {
            return ServiceLifetime.Singleton;
        }

        if (methodName.StartsWith("AddScoped") || methodName.StartsWith("TryAddScoped") ||
            methodName.StartsWith("AddKeyedScoped") || methodName.StartsWith("TryAddKeyedScoped"))
        {
            return ServiceLifetime.Scoped;
        }

        if (methodName.StartsWith("AddTransient") || methodName.StartsWith("TryAddTransient") ||
            methodName.StartsWith("AddKeyedTransient") || methodName.StartsWith("TryAddKeyedTransient"))
        {
            return ServiceLifetime.Transient;
        }

        return null;
    }

    private static bool IsKeyedMethod(string methodName)
    {
        return methodName.Contains("Keyed");
    }

    private static bool IsTryAddMethod(string methodName)
    {
        return methodName.StartsWith("TryAdd");
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, ServiceLifetime? lifetime) ExtractFromServiceDescriptor(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        // Look for ServiceDescriptor argument
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is ObjectCreationExpressionSyntax creation)
            {
                var typeSymbol = semanticModel.GetTypeInfo(creation).Type;
                if (typeSymbol?.Name == "ServiceDescriptor" && 
                    (typeSymbol.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                     typeSymbol.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions"))
                {
                    return ExtractFromServiceDescriptorArguments(creation.ArgumentList, semanticModel);
                }
            }
            else if (arg.Expression is InvocationExpressionSyntax describeInvocation)
            {
                var methodSymbol = semanticModel.GetSymbolInfo(describeInvocation).Symbol as IMethodSymbol;
                if (methodSymbol?.Name == "Describe" &&
                    methodSymbol.ContainingType.Name == "ServiceDescriptor" &&
                    (methodSymbol.ContainingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection" ||
                     methodSymbol.ContainingType.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection.Abstractions"))
                {
                    return ExtractFromServiceDescriptorArguments(describeInvocation.ArgumentList, semanticModel);
                }
            }
        }

        return (null, null, null, null);
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression, ServiceLifetime? lifetime) ExtractFromServiceDescriptorArguments(
        ArgumentListSyntax? argumentList,
        SemanticModel semanticModel)
    {
        var args = argumentList?.Arguments;
        if (args is null || args.Value.Count < 3)
        {
            return (null, null, null, null);
        }

        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;
        ServiceLifetime? lifetime = null;

        // ServiceDescriptor(Type serviceType, ... , ServiceLifetime lifetime)
        // Argument 0 is always serviceType
        if (args.Value[0].Expression is TypeOfExpressionSyntax serviceTypeOf)
        {
            var typeInfo = semanticModel.GetTypeInfo(serviceTypeOf.Type);
            serviceType = typeInfo.Type as INamedTypeSymbol;
        }

        // Last argument is usually lifetime
        var lastArg = args.Value.Last();
        if (lastArg.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Expression is IdentifierNameSyntax enumType &&
            enumType.Identifier.Text == "ServiceLifetime")
        {
            var lifetimeName = memberAccess.Name.Identifier.Text;
            if (System.Enum.TryParse<ServiceLifetime>(lifetimeName, out var parsedLifetime))
            {
                lifetime = parsedLifetime;
            }
        }

        // Middle argument can be ImplementationType, Instance, or Factory
        // ServiceDescriptor(Type serviceType, Type implementationType, ServiceLifetime lifetime)
        if (args.Value[1].Expression is TypeOfExpressionSyntax implTypeOf)
        {
             var typeInfo = semanticModel.GetTypeInfo(implTypeOf.Type);
             implementationType = typeInfo.Type as INamedTypeSymbol;
        }
        // ServiceDescriptor(Type serviceType, object instance, ServiceLifetime lifetime)
        else if (semanticModel.GetTypeInfo(args.Value[1].Expression).Type is INamedTypeSymbol instanceType &&
                 instanceType.SpecialType == SpecialType.None) // Not a primitive
        {
             // If it's an instance, we consider the type of the instance as the implementation type
             implementationType = instanceType;
        }
        // ServiceDescriptor(Type serviceType, Func<IServiceProvider, object> factory, ServiceLifetime lifetime)
        else if (args.Value[1].Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
        {
            factoryExpression = args.Value[1].Expression;
        }

        return (serviceType, implementationType, factoryExpression, lifetime);
    }

    private static (INamedTypeSymbol? serviceType, INamedTypeSymbol? implementationType, ExpressionSyntax? factoryExpression) ExtractTypes(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel)
    {
        INamedTypeSymbol? serviceType = null;
        INamedTypeSymbol? implementationType = null;
        ExpressionSyntax? factoryExpression = null;

        // Check for factory delegate in arguments
        foreach (var arg in invocation.ArgumentList.Arguments)
        {
            if (arg.Expression is LambdaExpressionSyntax or AnonymousMethodExpressionSyntax)
            {
                factoryExpression = arg.Expression;
                break;
            }
        }

        // Pattern 1: Generic method AddXxx<TService>() or AddXxx<TService, TImplementation>()
        if (method.IsGenericMethod && method.TypeArguments.Length > 0)
        {
            serviceType = method.TypeArguments[0] as INamedTypeSymbol;
            
            if (method.TypeArguments.Length > 1)
            {
                implementationType = method.TypeArguments[1] as INamedTypeSymbol;
            }
            else if (factoryExpression is null)
            {
                // Only default to serviceType if NO factory is present.
                // If factory is present, implementation is unknown (null).
                implementationType = serviceType;
            }

            return (serviceType, implementationType, factoryExpression);
        }

        // Pattern 2: Non-generic with Type parameters AddXxx(typeof(TService)) or AddXxx(typeof(TService), typeof(TImpl))
        var arguments = invocation.ArgumentList.Arguments;

        // Skip the first argument if it's the IServiceCollection (extension method receiver)
        var typeofArgs = new List<INamedTypeSymbol>();
        foreach (var arg in arguments)
        {
            if (arg.Expression is TypeOfExpressionSyntax typeofExpr)
            {
                var typeInfo = semanticModel.GetTypeInfo(typeofExpr.Type);
                if (typeInfo.Type is INamedTypeSymbol namedType)
                {
                    typeofArgs.Add(namedType);
                }
            }
        }

        if (typeofArgs.Count >= 1)
        {
            serviceType = typeofArgs[0];
            if (typeofArgs.Count > 1)
            {
                implementationType = typeofArgs[1];
            }
            else if (factoryExpression is null)
            {
                 // Only default to serviceType if NO factory is present
                implementationType = serviceType;
            }
            
            return (serviceType, implementationType, factoryExpression);
        }

        return (null, null, null);
    }
}
