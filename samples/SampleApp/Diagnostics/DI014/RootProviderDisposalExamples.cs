using Microsoft.Extensions.DependencyInjection;

namespace SampleApp.Diagnostics.DI014;

#pragma warning disable DI007

public class RootProviderDisposalExamples
{
    public void BadExample()
    {
        var services = new ServiceCollection();
        
        // ⚠️ BAD: Root provider is not disposed
        // Disposable singletons will not be disposed
        var provider = services.BuildServiceProvider();
        
        var service = provider.GetService<IServiceScopeFactory>();
    }

    public void GoodExample()
    {
        var services = new ServiceCollection();

        // ✅ GOOD: Provider disposed via 'using'
        using var provider = services.BuildServiceProvider();
        
        var service = provider.GetService<IServiceScopeFactory>();
    }

    public void GoodExampleExplicit()
    {
        var services = new ServiceCollection();

        // ✅ GOOD: Explicit disposal
        var provider = services.BuildServiceProvider();
        try
        {
            // ...
        }
        finally
        {
            provider.Dispose();
        }
    }
}
