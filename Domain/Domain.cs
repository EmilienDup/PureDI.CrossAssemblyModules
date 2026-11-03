using Data;
using Pure.DI;
using static Pure.DI.Lifetime;

namespace Domain;

public interface IOrderService
{
    string Describe(int id);
}

// Implemented by the application (a platform service)
public interface IClock
{
    DateTime Now { get; }
}

internal sealed class OrderService(
    IRepository repository,
    IClock clock,
    ILogger logger,
    [Tag("audit")] ILogger audit)
    : IOrderService
{
    public string Describe(int id)
    {
        logger.Log("Describing");
        audit.Log($"Order #{id} read");
        return $"{repository.Load(id)} at {clock.Now:yyyy-MM-dd}";
    }
}

public partial class DomainModule
{
    private void Setup() =>
        DI.Setup()
            // The exported roots of the data module; the application owns its instance
            .Arg<DataModule>("data")

            .Import<ILogger>()
            .Import<ILogger>("audit")
            .Import<IClock>()

            .Bind().As(Singleton).To<OrderService>()
            .Root<IOrderService>("Orders", kind: RootKinds.Exported);
}
