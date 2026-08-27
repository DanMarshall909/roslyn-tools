using RoslynTools.Daemon.Rpc;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Daemon;

public sealed class McpDaemonLauncherTests
{
    [Fact]
    public void Default_idle_timeout_is_ten_minutes()
    {
        DaemonLaunchOptions.FromEnvironment(_ => null).IdleTimeout
            .ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void Idle_timeout_can_be_configured_for_installations()
    {
        DaemonLaunchOptions.FromEnvironment(
                name => name == DaemonLaunchOptions.IdleTimeoutEnvironmentVariable ? "45" : null)
            .IdleTimeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Published_mcp_host_launches_the_sibling_rt_apphost()
    {
        using var directory = new TemporaryDirectory();
        var executable = Path.Combine(directory.Path, "rt");
        File.WriteAllText(executable, string.Empty);

        var command = DaemonLauncher.ResolveLaunchCommand(
            directory.Path,
            isWindows: false,
            TimeSpan.FromMinutes(10));

        command.FileName.ShouldBe(executable);
        command.Arguments.ShouldBe([
            "--daemon-serve",
            "--idle-timeout-seconds",
            "600"
        ]);
    }

    [Fact]
    public void Source_build_falls_back_to_dotnet_and_the_sibling_rt_dll()
    {
        using var directory = new TemporaryDirectory();
        var assembly = Path.Combine(directory.Path, "rt.dll");
        File.WriteAllText(assembly, string.Empty);

        var command = DaemonLauncher.ResolveLaunchCommand(
            directory.Path,
            isWindows: false,
            TimeSpan.FromSeconds(5));

        command.FileName.ShouldBe("dotnet");
        command.Arguments.ShouldBe([
            assembly,
            "--daemon-serve",
            "--idle-timeout-seconds",
            "5"
        ]);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"roslyn-tools-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
