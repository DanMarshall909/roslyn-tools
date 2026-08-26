using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Daemon.Rpc;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonDependencyInjectionTests
{
    [Fact]
    public void Bootstrap_resolves_the_daemon_command_dispatcher()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<DaemonCommandDispatcher>().ShouldNotBeNull();
    }
}
