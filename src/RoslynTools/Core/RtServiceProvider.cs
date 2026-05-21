namespace RoslynTools.Core;

/// <summary>
/// A class for providing services in the console.
/// </summary>
internal static class RtServiceProvider
{
    private static readonly AsyncLocal<IServiceProvider?> CurrentProvider = new();

    /// <summary>
    /// Returns an instance of the service provider if provided, otherwise creates a new one.
    /// </summary>
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

    /// <summary>
    ///     Resolve an interface (or any type without a public parameterless constructor)
    ///     from the ambient provider, falling back to the supplied factory when no
    ///     provider is wired up (test paths that exercise <c>App.RunAsync</c>
    ///     with <c>services: null</c>). Used for DI-only services such as
    ///     <see cref="IWorkspaceSessionFactory" />.
    /// </summary>
    internal static T GetOrDefault<T>(Func<T> defaultFactory)
        where T : class
    {
        var service = CurrentProvider.Value?.GetService(typeof(T)) as T;
        return service ?? defaultFactory();
    }

    private sealed class Scope(IServiceProvider? previous) : IDisposable
    {
        public void Dispose()
        {
            CurrentProvider.Value = previous;
        }
    }
}
