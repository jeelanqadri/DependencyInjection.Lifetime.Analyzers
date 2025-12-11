using System.Collections.Immutable;
using System.Linq;
using DependencyInjection.Lifetime.Analyzers.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace DependencyInjection.Lifetime.Analyzers.Rules;

/// <summary>
/// Analyzer that detects captive dependencies - when a singleton captures a scoped or transient dependency.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class DI003_CaptiveDependencyAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(DiagnosticDescriptors.CaptiveDependency);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(compilationContext =>
        {
            var registrationCollector = RegistrationCollector.Create(compilationContext.Compilation);
            if (registrationCollector is null)
            {
                return;
            }

            // First pass: collect all registrations
            compilationContext.RegisterSyntaxNodeAction(
                syntaxContext => registrationCollector.AnalyzeInvocation(
                    (InvocationExpressionSyntax)syntaxContext.Node,
                    syntaxContext.SemanticModel),
                SyntaxKind.InvocationExpression);

            // Second pass: check for captive dependencies at compilation end
            compilationContext.RegisterCompilationEndAction(
                endContext => AnalyzeCaptiveDependencies(endContext, registrationCollector));
        });
    }

    private static void AnalyzeCaptiveDependencies(
        CompilationAnalysisContext context,
        RegistrationCollector registrationCollector)
    {
        foreach (var registration in registrationCollector.Registrations)
        {
            // Only check singletons and scoped services for captive dependencies
            if (registration.Lifetime == ServiceLifetime.Transient)
            {
                continue;
            }

            if (registration.FactoryExpression != null)
            {
                AnalyzeFactoryRegistration(context, registration, registrationCollector);
            }
            else if (registration.ImplementationType != null)
            {
                AnalyzeConstructorRegistration(context, registration, registrationCollector);
            }
        }
    }

    private static void AnalyzeFactoryRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector)
    {
        var factory = registration.FactoryExpression;
        if (factory == null) return;

        // Scan for GetService/GetRequiredService calls within the factory
        var invocations = factory.DescendantNodes().OfType<InvocationExpressionSyntax>();
        
        foreach (var invocation in invocations)
        {
            var methodName = GetMethodName(invocation);
            bool isKeyedResolution = methodName == "GetKeyedService" || methodName == "GetRequiredKeyedService";

            if (methodName != "GetService" && methodName != "GetRequiredService" && !isKeyedResolution)
            {
                continue;
            }

            // Extract the type argument T from GetService<T>()
            var typeArgument = GetGenericTypeArgument(invocation);
            if (typeArgument == null)
            {
                continue;
            }
            
            #pragma warning disable RS1030
            var semanticModel = context.Compilation.GetSemanticModel(invocation.SyntaxTree);
            #pragma warning restore RS1030
            var symbolInfo = semanticModel.GetSymbolInfo(typeArgument);
            var dependencyType = symbolInfo.Symbol as ITypeSymbol;

            if (dependencyType == null) continue;

            object? key = null;
            bool isKeyed = false;
            if (isKeyedResolution)
            {
                key = ExtractKeyFromResolution(invocation, semanticModel);
                isKeyed = true;
            }

            var dependencyLifetime = registrationCollector.GetLifetime(dependencyType, key, isKeyed);
            if (dependencyLifetime == null) continue;

            if (IsCaptiveDependency(registration.Lifetime, dependencyLifetime.Value))
            {
                var lifetimeName = dependencyLifetime.Value.ToString().ToLowerInvariant();
                var diagnostic = Diagnostic.Create(
                    DiagnosticDescriptors.CaptiveDependency,
                    invocation.GetLocation(),
                    registration.ServiceType.Name, // Use ServiceType name for factories
                    lifetimeName,
                    dependencyType.Name);

                context.ReportDiagnostic(diagnostic);
            }
        }
    }

    private static object? ExtractKeyFromResolution(InvocationExpressionSyntax invocation, SemanticModel semanticModel)
    {
        // provider.GetRequiredKeyedService<T>(key)
        // Extension method: argument 0 is key.
        if (invocation.ArgumentList.Arguments.Count > 0)
        {
            return ExtractConstantValue(invocation.ArgumentList.Arguments[0].Expression, semanticModel);
        }
        return null;
    }

    private static object? ExtractConstantValue(ExpressionSyntax expr, SemanticModel semanticModel)
    {
        var constantValue = semanticModel.GetConstantValue(expr);
        if (constantValue.HasValue)
        {
            return constantValue.Value;
        }
        return null;
    }

    private static string? GetMethodName(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
        {
            return memberAccess.Name.Identifier.Text;
        }
        return null;
    }

    private static TypeSyntax? GetGenericTypeArgument(InvocationExpressionSyntax invocation)
    {
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess &&
            memberAccess.Name is GenericNameSyntax genericName &&
            genericName.TypeArgumentList.Arguments.Count > 0)
        {
            return genericName.TypeArgumentList.Arguments[0];
        }
        return null;
    }

    private static void AnalyzeConstructorRegistration(
        CompilationAnalysisContext context,
        ServiceRegistration registration,
        RegistrationCollector registrationCollector)
    {
        // Analyze the implementation type's constructor parameters
        var implementationType = registration.ImplementationType!;
        var constructors = implementationType.Constructors;

        foreach (var constructor in constructors)
        {
            if (constructor.IsStatic || constructor.DeclaredAccessibility == Accessibility.Private)
            {
                continue;
            }

            foreach (var parameter in constructor.Parameters)
            {
                var parameterType = parameter.Type;
                var (key, isKeyed) = GetServiceKey(parameter);
                var dependencyLifetime = registrationCollector.GetLifetime(parameterType, key, isKeyed);

                if (dependencyLifetime is null)
                {
                    // Unknown dependency - don't report
                    continue;
                }

                // Check for captive dependency: longer-lived service capturing shorter-lived dependency
                if (IsCaptiveDependency(registration.Lifetime, dependencyLifetime.Value))
                {
                    var lifetimeName = dependencyLifetime.Value.ToString().ToLowerInvariant();
                    var diagnostic = Diagnostic.Create(
                        DiagnosticDescriptors.CaptiveDependency,
                        registration.Location,
                        implementationType.Name,
                        lifetimeName,
                        parameterType.Name);

                    context.ReportDiagnostic(diagnostic);
                }
            }
        }
    }

    private static (object? key, bool isKeyed) GetServiceKey(IParameterSymbol parameter)
    {
        foreach (var attribute in parameter.GetAttributes())
        {
            if (attribute.AttributeClass?.Name == "FromKeyedServicesAttribute" &&
                (attribute.AttributeClass.ContainingNamespace.ToDisplayString() == "Microsoft.Extensions.DependencyInjection"))
            {
                if (attribute.ConstructorArguments.Length > 0)
                {
                    return (attribute.ConstructorArguments[0].Value, true);
                }
            }
        }
        return (null, false);
    }

    private static bool IsCaptiveDependency(ServiceLifetime consumerLifetime, ServiceLifetime dependencyLifetime)
    {
        // A captive dependency occurs when a longer-lived service captures a shorter-lived one
        // Lifetime order: Singleton (longest) > Scoped > Transient (shortest)
        return consumerLifetime < dependencyLifetime;
    }
}
