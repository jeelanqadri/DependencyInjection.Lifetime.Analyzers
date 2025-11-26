using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI011;

/// <summary>
/// ⚠️ BAD: Injecting IServiceProvider directly hides dependencies.
/// </summary>
public class ServiceProviderInjectionExample
{
    private readonly IServiceProvider _provider;

    public ServiceProviderInjectionExample(IServiceProvider provider)
    {
        _provider = provider;
    }

    public void DoWork()
    {
        var service = _provider.GetService<IServiceScopeFactory>();
    }
}

/// <summary>
/// ✅ GOOD: Explicit dependencies.
/// </summary>
public class ExplicitDependencyExample
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ExplicitDependencyExample(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
}

public static class Registration
{
    public static void Register(IServiceCollection services)
    {
        // Trigger DI011
        services.AddTransient<ServiceProviderInjectionExample>();
        
        // Good
        services.AddTransient<ExplicitDependencyExample>();
    }
}
