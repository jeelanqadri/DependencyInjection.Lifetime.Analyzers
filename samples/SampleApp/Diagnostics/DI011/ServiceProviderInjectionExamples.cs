using Microsoft.Extensions.DependencyInjection;
using SampleApp.Services;

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
        var service = _provider.GetService<ITransientService>();
    }
}

/// <summary>
/// ✅ GOOD: Explicit dependencies.
/// Inject the service you need directly, rather than the provider.
/// </summary>
public class ExplicitDependencyExample
{
    private readonly ITransientService _service;

    public ExplicitDependencyExample(ITransientService service)
    {
        _service = service;
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
