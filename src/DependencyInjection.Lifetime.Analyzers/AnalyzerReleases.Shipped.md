; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules
Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DI001 | DependencyInjection | Warning | Service scope must be disposed
DI002 | DependencyInjection | Warning | Scoped service escapes scope
DI003 | DependencyInjection | Warning | Captive dependency detected
DI004 | DependencyInjection | Warning | Service used after scope disposed
DI005 | DependencyInjection | Warning | Use CreateAsyncScope in async methods
DI006 | DependencyInjection | Warning | Avoid caching IServiceProvider in static members
DI007 | DependencyInjection | Warning | Avoid service locator anti-pattern
DI008 | DependencyInjection | Warning | Transient service implements IDisposable
DI009 | DependencyInjection | Warning | Open generic captive dependency
