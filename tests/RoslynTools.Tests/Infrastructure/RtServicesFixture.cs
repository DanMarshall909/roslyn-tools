using Microsoft.Extensions.DependencyInjection;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
/// xUnit class fixture that builds a real DI container via
/// <see cref="RoslynTools.Bootstrap.RegisterServices(IServiceCollection)"/>
/// and exposes <see cref="Resolve{T}"/> for tests that need a fully-wired
/// command or service instance — mirroring the production dependency graph.
/// </summary>
public sealed class RtServicesFixture : IDisposable
{
    private readonly ServiceProvider _services;

    public RtServicesFixture()
    {
        var sc = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(sc);
        _services = sc.BuildServiceProvider();
    }

    public T Resolve<T>() where T : notnull => _services.GetRequiredService<T>();

    public void Dispose() => _services.Dispose();
}
