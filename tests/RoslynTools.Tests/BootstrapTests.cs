using Microsoft.Extensions.Hosting;

namespace RoslynTools.Tests;

/// <summary>
///     Tests for US-022: DI host scaffold wired in RoslynTools.Cli startup.
/// </summary>
public sealed class BootstrapTests
{
    [Fact]
    public void Bootstrap_RegisterServices_does_not_throw()
    {
        // Build a real IHost using the same call pattern as Program.cs.
        // Assert that Bootstrap.RegisterServices completes without exception
        // and that the resulting host exposes a non-null service provider.
        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(Bootstrap.RegisterServices)
            .Build();

        Assert.NotNull(host.Services);
    }
}
