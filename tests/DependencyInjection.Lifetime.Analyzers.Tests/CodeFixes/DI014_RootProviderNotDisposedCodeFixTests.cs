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
    public async Task No_Fix_For_Multi_Variable_Declaration()
    {
        var test = @"
using Microsoft.Extensions.DependencyInjection;

public class Program
{
    public void Main()
    {
        var services = new ServiceCollection();
        ServiceProvider provider = services.BuildServiceProvider(), other = null;
    }
}";

        var expected = VerifyCS.Diagnostic(DiagnosticDescriptors.RootProviderNotDisposed)
            .WithLocation(9, 36); // Location of BuildServiceProvider() call

        await VerifyCS.VerifyNoCodeFixOfferedAsync(test, expected);
    }
}
