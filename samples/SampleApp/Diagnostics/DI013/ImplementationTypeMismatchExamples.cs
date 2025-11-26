using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI013;

public interface IRepository { }
public class SqlRepository : IRepository { }
public class WrongType { }

public static class ImplementationTypeMismatchExamples
{
    public static void Register(IServiceCollection services)
    {
        // ✅ GOOD: Correct implementation type
        services.AddSingleton(typeof(IRepository), typeof(SqlRepository));

        // ⚠️ BAD: WrongType does not implement IRepository
        // This will throw an ArgumentException at runtime
        services.AddSingleton(typeof(IRepository), typeof(WrongType));
        
        // ⚠️ BAD: String does not implement IRepository
        services.AddScoped(typeof(IRepository), typeof(string));
    }
}
