using Pure.DI;
using static Pure.DI.Lifetime;

namespace Data;

// Implemented by the application, consumed by the lower layers
public interface ILogger
{
    void Log(string message);
}

public interface IRepository
{
    string Load(int id);
}

// Internal: nothing outside this assembly can see or construct it
internal sealed class Repository(ILogger logger) : IRepository
{
    public string Load(int id)
    {
        logger.Log($"Loading #{id}");
        return $"Order #{id}";
    }
}

public partial class DataModule
{
    private void Setup() =>
        DI.Setup()
            // Supplied by whoever creates this composition
            .Import<ILogger>()

            .Bind().As(Singleton).To<Repository>()
            .Root<IRepository>("Repository", kind: RootKinds.Exported);
}
