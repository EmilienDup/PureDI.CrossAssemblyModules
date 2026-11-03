# PureDI.CrossAssemblyModules

A layered application where every layer is its own assembly with `internal` implementations, and the application wires the layers together:

```
Data      ILogger, IRepository (public)    Repository (internal)     needs ILogger from the app
Domain    IOrderService, IClock (public)   OrderService (internal)   needs IRepository from Data, ILogger + IClock + ILogger("audit") from the app
App       ConsoleLogger, AuditLogger, SystemClock (SystemClock depends on IOrderService: a cycle across layers)
```

Requirements:

- no `InternalsVisibleTo` from a layer to the application;
- no layer setup source compiled into the application (Pure.DI only sees source of the project it runs in, so `DependsOn` a setup of a referenced project fails with DIE038);
- compile-time checked wiring, one instance per layer, lifetimes owned by the application.

## What stock Pure.DI already does

[Exported roots](https://github.com/DevTeam/Pure.DI/blob/master/readme/exported-roots.md): each layer has its own public composition, exports its contracts with `RootKinds.Exported`, and the application binds the composition type (`Bind().As(Singleton).To<DataModule>()`). A composition taking another as an `Arg` gets its exported roots too ([exported roots via arg](https://github.com/DevTeam/Pure.DI/blob/master/readme/exported-roots-via-arg.md)). Implementations stay internal.

What is missing is the other direction: a layer that needs something from the application. On 2.5.7 every such dependency is spelled out by hand, lazily so that layers can depend on each other:

```csharp
.Arg<Func<ILogger>>("logger")
.Bind<ILogger>().To(ctx =>
{
    ctx.Inject(out Func<ILogger> logger);
    return logger();
})
```

and a *tagged* dependency cannot be expressed at all: the generated constructor parameter carries no tag, so the application cannot tell `Func<ILogger>` from `Func<ILogger>("audit")`.

## The patch: `Import<T>(params object[] tags)`

```csharp
public partial class DomainModule
{
    private void Setup() =>
        DI.Setup()
            .Arg<DataModule>("data")
            .Import<ILogger>()
            .Import<ILogger>("audit")
            .Import<IClock>()
            .Bind().As(Singleton).To<OrderService>()
            .Root<IOrderService>("Orders", kind: RootKinds.Exported);
}
```

generates

```csharp
public DomainModule(
    DataModule data,
    Func<ILogger> logger,
    [Pure.DI.Tag("audit")] Func<ILogger> loggerAudit,
    Func<IClock> clock)
```

`Import<T>(tags)` is exactly the hand-written pair above (a `Func<T>` composition argument with a private tag, and a binding of `T` with `tags` that calls it on every injection), plus the `[Tag]` on the constructor parameter. The application needs nothing new: binding `DomainModule` already constructor-injects it from the application graph, and Pure.DI reads `[Tag]` on parameters of types from other assemblies.

[`Pure.DI.import.patch`](Pure.DI.import.patch) is against Pure.DI master `adaacfe` (2.5.7 + 3 commits): API declaration, `ApiInvocationProcessor.VisitImport`, the `[Tag]` in `ParameterizedConstructorBuilder`, and `ImportTests` (2 integration tests; `ArgsTests`, `BuildersTests`, `ExportAttributeTests`, `FactoryTests`, `SetupContextTests` still pass, 339 tests). `packages/Pure.DI.2.5.8-import.3.nupkg` is that commit with the patch.

## Run

```bash
dotnet run --project App                            # patched package from ./packages
dotnet build App -p:PureDIVersion='[2.5.7]'         # stock: CS1061 'IConfiguration' does not contain a definition for 'Import'
```

Output:

```
[log] Describing
[audit] Order #42 read
[log] Loading #42
Order #42 at 2026-11-03
```

Generated code: `*/obj/Debug/net10.0/generated/Pure.DI/`.

## Notes

- Import names are derived from the type and the first tag (`ILogger` → `logger`, `ILogger, "audit"` → `loggerAudit`, `string` → `stringImport`). The application never names them, only a hand-written `new DomainModule(...)` does.
- The supplier owns the lifetime: the import is resolved through the `Func` on every injection, so a singleton in the application stays one instance across all layers, and a transient stays transient.
- `Func` keeps cycles across layers working (`SystemClock` → `IOrderService` → `DomainModule` → `IClock`).
- Unrelated, also on stock 2.5.7: `.Bind().As(Singleton).To<DataModule>()` reports DIW003 "The binding is not used" although the instance is injected into `DomainModule`; it disappears once the application consumes one of `DataModule`'s own exported roots.

## Rebuild the patched package

```bash
git clone --depth 30 https://github.com/DevTeam/Pure.DI.git && cd Pure.DI && git checkout adaacfe
git apply ../Pure.DI.import.patch
cd src/Pure.DI
ROSLYN='-p:AnalyzerRoslynVersion=5.6 -p:AnalyzerRoslynPackageVersion=5.6.0 -p:RolsynVersions=ROSLYN5_6_OR_GREATER%3BROSLYN4_8_OR_GREATER%3BROSLYN4_3_OR_GREATER'
dotnet restore $ROSLYN
dotnet build -c Release -p:PackGenerator=true $ROSLYN --no-restore
dotnet pack -c Release -p:PackGenerator=true $ROSLYN --no-restore --no-build -p:PackageVersion=2.5.8-import.3 -o <repro>/packages
```

When re-packing the same version, delete `~/.nuget/packages/pure.di/2.5.8-import.3` first.
