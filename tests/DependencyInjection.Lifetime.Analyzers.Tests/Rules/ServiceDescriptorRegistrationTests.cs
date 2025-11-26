using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure.AnalyzerVerifier<
    DependencyInjection.Lifetime.Analyzers.Rules.DI003_CaptiveDependencyAnalyzer>;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class ServiceDescriptorRegistrationTests
{
    [Fact]
    public async Task ServiceDescriptor_Registration_Detected()
    {
        var test = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public interface IScopedService { }
public class ScopedService : IScopedService { }

public interface ISingletonService { }
public class SingletonService : ISingletonService
{
    public SingletonService(IScopedService scoped) { }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register Scoped service using ServiceDescriptor
        services.Add(new ServiceDescriptor(typeof(IScopedService), typeof(ScopedService), ServiceLifetime.Scoped));

        // Register Singleton service using ServiceDescriptor
        services.Add(new ServiceDescriptor(typeof(ISingletonService), typeof(SingletonService), ServiceLifetime.Singleton));
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithLocation(22, 9) // Location of services.Add(...) for Singleton
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await VerifyCS.VerifyDiagnosticsAsync(test, expected);
    }

    [Fact]
    public async Task ServiceDescriptor_Describe_Detected()
    {
        var test = @"
using System;
using Microsoft.Extensions.DependencyInjection;

public interface IScopedService { }
public class ScopedService : IScopedService { }

public interface ISingletonService { }
public class SingletonService : ISingletonService
{
    public SingletonService(IScopedService scoped) { }
}

public class Startup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // Register Scoped service using ServiceDescriptor.Describe
        services.Add(ServiceDescriptor.Describe(typeof(IScopedService), typeof(ScopedService), ServiceLifetime.Scoped));

        // Register Singleton service using ServiceDescriptor.Describe
        services.Add(ServiceDescriptor.Describe(typeof(ISingletonService), typeof(SingletonService), ServiceLifetime.Singleton));
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.CaptiveDependency)
            .WithLocation(22, 9) // Location of services.Add(...) for Singleton
            .WithArguments("SingletonService", "scoped", "IScopedService");

        await VerifyCS.VerifyDiagnosticsAsync(test, expected);
    }
}
