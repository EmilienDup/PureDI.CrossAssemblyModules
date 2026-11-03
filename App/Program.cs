using Data;
using Domain;
using Pure.DI;
using static Pure.DI.Lifetime;

var composition = new AppComposition();
Console.WriteLine(composition.Shell.Run());

sealed class ConsoleLogger : ILogger
{
    public void Log(string message) => Console.WriteLine($"[log] {message}");
}

sealed class AuditLogger : ILogger
{
    public void Log(string message) => Console.WriteLine($"[audit] {message}");
}

// Depends on the domain layer, which imports it: a cycle across layers
sealed class SystemClock(Func<IOrderService> orders) : IClock
{
    public DateTime Now => new(2026, 11, 3);
}

sealed class Shell(IOrderService orders)
{
    public string Run() => orders.Describe(42);
}

partial class AppComposition
{
    private void Setup() =>
        DI.Setup()
            .Bind().As(Singleton).To<ConsoleLogger>()
            .Bind("audit").As(Singleton).To<AuditLogger>()
            .Bind().As(Singleton).To<SystemClock>()

            // One instance of each module, wired by the application
            .Bind().As(Singleton).To<DataModule>()
            .Bind().As(Singleton).To<DomainModule>()

            .Root<Shell>("Shell");
}
