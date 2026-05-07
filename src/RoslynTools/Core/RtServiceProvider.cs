namespace RoslynTools.Core;

internal static class RtServiceProvider
{
    private static readonly AsyncLocal<IServiceProvider?> CurrentProvider = new();

    internal static IDisposable Use(IServiceProvider? services)
    {
        var previous = CurrentProvider.Value;
        CurrentProvider.Value = services;
        return new Scope(previous);
    }

    internal static T Get<T>()
        where T : class, new()
    {
        var service = CurrentProvider.Value?.GetService(typeof(T)) as T;
        return service ?? new T();
    }

    private sealed class Scope(IServiceProvider? previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentProvider.Value = previous;
        }
    }
}
