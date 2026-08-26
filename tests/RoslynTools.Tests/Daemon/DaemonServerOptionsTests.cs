using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonServerOptionsTests
{
    [Fact]
    public void Manual_daemon_start_has_no_idle_timeout()
    {
        DaemonServer.ParseIdleTimeout(["--daemon-serve"]).ShouldBeNull();
    }

    [Fact]
    public void Mcp_daemon_start_applies_its_configured_idle_timeout()
    {
        DaemonServer.ParseIdleTimeout([
                "--daemon-serve",
                "--idle-timeout-seconds",
                "2.5"
            ])
            .ShouldBe(TimeSpan.FromSeconds(2.5));
    }
}
