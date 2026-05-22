using Microsoft.Extensions.Hosting;
using Shouldly;

namespace RoslynTools.Tests;

/// <summary>
///     Tests for US-022: DI host scaffold wired in RoslynTools.Cli startup.
/// </summary>
public sealed class Bootstrap
{
    [Fact]
    public void register_services_does_not_throw()
    {
        // Build a real IHost using the same call pattern as Program.cs.
        // Assert that Bootstrap.RegisterServices completes without exception
        // and that the resulting host exposes a non-null service provider.
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(RoslynTools.Bootstrap.RegisterServices)
            .Build();

        host.Services.ShouldNotBeNull();
    }
}
