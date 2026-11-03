# How should a composition in another assembly get services from the application?

## Use case

A layered application where each layer is a separate assembly with internal implementations. Each layer has its own composition and exports its public contracts with `RootKinds.Exported`; the application binds every layer composition once (`Bind().As(Lifetime.Singleton).To<DataModule>()`) and gets their exported roots. Implementations stay internal, and no setup source has to be shared with the application. This part works well.

The question is about the other direction. A lower layer often needs services that only the application provides (logging, platform services, configuration). Sometimes it needs several of the same type with different tags, and sometimes they come from a layer that itself depends on this one.

Constraints:

- no `InternalsVisibleTo` from a layer to the application;
- no layer setup source compiled into the application (`DependsOn` on a setup from a referenced project fails with DIE038);
- wiring checked at compile time, one instance per layer, lifetimes owned by the application.

## What we do today

Each such dependency is written by hand as a composition argument that is called lazily:

```c#
public partial class DomainModule
{
    private void Setup() =>
        DI.Setup()
            .Arg<Func<ILogger>>("logger")
            .Bind<ILogger>().To(ctx =>
            {
                ctx.Inject(out Func<ILogger> logger);
                return logger();
            })
            .Bind().As(Lifetime.Singleton).To<OrderService>()
            .Root<IOrderService>("Orders", kind: RootKinds.Exported);
}
```

The application binds `DomainModule` and fills `Func<ILogger> logger` through constructor injection. The `Func` lets the application keep the lifetime and lets dependencies run in a cycle across layers (for example `SystemClock` → `IOrderService` → `DomainModule` → `IClock`).

This works, but it is verbose, and it breaks down for tagged dependencies. The generated constructor parameter has no tag, so the application cannot tell `Func<ILogger>` from a `Func<ILogger>` that should be the one tagged `"audit"`.

## Workarounds we found

- **Construct the module by hand in the application** with a factory binding that injects each tagged `Func` and calls `new DomainModule(...)`. This works, but the application has to know every argument, and adding an import to a layer breaks the application.
- **One delegate or wrapper type per tag** (`delegate ILogger AuditLogger()` or similar) defined in the layer, so that the types differ instead of the tags. This avoids tags, but adds a type for every tagged dependency and a binding for it in the application.

## Questions

1. Is there an intended way in stock Pure.DI for a composition to declare a dependency that whoever creates it supplies, possibly tagged?
2. If not, is the hand-written `Arg<Func<T>>` + factory pattern above what you would recommend, and how would you handle the tagged case?
3. Would you accept something like `Import<T>(params object[] tags)` that generates the pattern above and puts `[Tag(tag)]` on the constructor parameter? We have a prototype (about 130 lines plus 2 integration tests, against master `adaacfe`) and can open a PR if that direction suits you. If another direction suits you better, we would rather follow that.

## Repro

The attached 3-assembly sample (`Data` ← `Domain` ← `App`) shows the use case, including a tagged import and a cycle across layers. `dotnet run --project App` runs it with the prototype package. `dotnet build App -p:PureDIVersion='[2.5.7]'` builds it against stock Pure.DI, which fails because `Import` does not exist there.

## Side note

`Bind().As(Lifetime.Singleton).To<DataModule>()` reports DIW003 "The binding is not used" when the application only passes the instance on to another composition (`Arg<DataModule>` of `DomainModule`) and uses none of `DataModule`'s own exported roots. This also happens on stock 2.5.7. Is that expected?
