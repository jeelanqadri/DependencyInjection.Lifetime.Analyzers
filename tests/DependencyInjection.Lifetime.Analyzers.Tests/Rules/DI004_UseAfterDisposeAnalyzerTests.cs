using DependencyInjection.Lifetime.Analyzers.Rules;
using DependencyInjection.Lifetime.Analyzers.Tests.Infrastructure;
using Xunit;

namespace DependencyInjection.Lifetime.Analyzers.Tests.Rules;

public class DI004_UseAfterDisposeAnalyzerTests
{
    private const string Usings = """
        using System;
        using System.Threading.Tasks;
        using Microsoft.Extensions.DependencyInjection;

        """;

    #region Should Report Diagnostic

    [Fact]
    public async Task ServiceUsedAfterScopeDisposed_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService service;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Using service after scope disposed!
                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(26, 9, 26, 25)
                .WithArguments("service"));
    }

    [Fact]
    public async Task UsingVarInNestedBlock_ServiceUsedAfterBlockEnds_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool condition)
                {
                    IMyService service = null;
                    if (condition)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    }
                    // Using service after scope disposed (block ended)!
                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(27, 9, 27, 25)
                .WithArguments("service"));
    }

    [Fact]
    public async Task MultipleScopes_ServiceFromFirstUsedAfterSecond_ReportsDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    IMyService firstService;
                    using (var scope1 = _scopeFactory.CreateScope())
                    {
                        firstService = scope1.ServiceProvider.GetRequiredService<IMyService>();
                    }

                    using (var scope2 = _scopeFactory.CreateScope())
                    {
                        var secondService = scope2.ServiceProvider.GetRequiredService<IMyService>();
                        secondService.DoWork();
                    }

                    // Using firstService after its scope disposed!
                    firstService.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyDiagnosticsAsync(
            source,
            AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>
                .Diagnostic(DiagnosticDescriptors.UseAfterScopeDisposed)
                .WithSpan(33, 9, 33, 30)
                .WithArguments("firstService"));
    }

    #endregion

    #region Should Not Report Diagnostic

    [Fact]
    public async Task ServiceUsedWithinScope_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                    service.DoWork();
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task ServiceUsedWithinUsingStatement_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task UsingVarInNestedBlock_ServiceUsedWithinBlock_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork(bool condition)
                {
                    if (condition)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
                        service.DoWork(); // This is fine - within the block
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    [Fact]
    public async Task NestedScopes_ServiceFromOuterUsedAfterInnerDisposed_NoDiagnostic()
    {
        var source = Usings + """
            public interface IMyService
            {
                void DoWork();
            }

            public class MyClass
            {
                private readonly IServiceScopeFactory _scopeFactory;

                public MyClass(IServiceScopeFactory scopeFactory)
                {
                    _scopeFactory = scopeFactory;
                }

                public void ProcessWork()
                {
                    using (var outerScope = _scopeFactory.CreateScope())
                    {
                        var outerService = outerScope.ServiceProvider.GetRequiredService<IMyService>();

                        using (var innerScope = _scopeFactory.CreateScope())
                        {
                            var innerService = innerScope.ServiceProvider.GetRequiredService<IMyService>();
                            innerService.DoWork();
                        }

                        // This is fine - outerService scope is still active
                        outerService.DoWork();
                    }
                }
            }
            """;

        await AnalyzerVerifier<DI004_UseAfterDisposeAnalyzer>.VerifyNoDiagnosticsAsync(source);
    }

    #endregion
}
