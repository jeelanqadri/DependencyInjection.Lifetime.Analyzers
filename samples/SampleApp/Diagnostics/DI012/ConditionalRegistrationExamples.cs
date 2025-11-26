using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace SampleApp.Diagnostics.DI012;

public interface IService { }
public class ServiceA : IService { }
public class ServiceB : IService { }

public static class ConditionalRegistrationExamples
{
    public static void Register(IServiceCollection services)
    {
        // ⚠️ BAD: TryAdd will be ignored because ServiceA is already registered
        services.AddSingleton<IService, ServiceA>();
        services.TryAddSingleton<IService, ServiceB>(); // Diagnostic here

        // ⚠️ BAD: Multiple Add calls (Duplicate Registration)
        // The second registration overrides the first for single resolution
        services.AddScoped<ServiceA>();
        services.AddScoped<ServiceA>(); // Diagnostic here
    }
}
