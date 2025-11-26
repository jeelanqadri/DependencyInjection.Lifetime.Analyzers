using System.Threading.Tasks;
using DependencyInjection.Lifetime.Analyzers.CodeFixes;
using DependencyInjection.Lifetime.Analyzers.Rules;
using Microsoft.CodeAnalysis.Testing;
using Xunit;
using VerifyCS = DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure.CodeFixVerifier<
    DependencyInjection.Lifetime.Analyzers.Rules.DI014_RootProviderNotDisposedAnalyzer,
    DependencyInjection.Lifetime.Analyzers.CodeFixes.DI014_RootProviderNotDisposedCodeFixProvider>;

namespace DependencyInjection.Lifetime.Analyzers.Tests.CodeFixes;

public class DI014_RootProviderNotDisposedCodeFixTests
{
    [Fact]
    public async Task Fixes_RootProvider_Not_Disposed()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using var provider = services.BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(9, 24); // Location of BuildServiceProvider() call

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Async_Method_With_Await_Using()
    {
        var test = @"
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public async Task Main()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
    }
}";

        var fixtest = @"
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public async Task Main()
    {
        var services = new ServiceCollection();
        await using var provider = services.BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(10, 24);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Fixes_Explicit_Type()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider provider = services.BuildServiceProvider();
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        using ServiceProvider provider = services.BuildServiceProvider();
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(9, 36);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }

    [Fact]
    public async Task Preserves_Trivia()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        // Create the provider
        var provider = services.BuildServiceProvider(); // End comment
    }
}";

        var fixtest = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        // Create the provider
        using var provider = services.BuildServiceProvider(); // End comment
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(10, 24);

        await VerifyCS.VerifyCodeFixAsync(test, expected, fixtest);
    }
}
