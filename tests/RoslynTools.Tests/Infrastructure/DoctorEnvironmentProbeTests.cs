using System.Diagnostics;
using System.Globalization;
using RoslynTools;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DoctorEnvironmentProbeTests
{
    [Fact]
    public async Task controlled_mcp_process_must_complete_a_pure_initialize_and_tools_list_handshake()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TemporaryDirectory();
        var cli = Path.Combine(directory.Path, "rt");
        var mcp = Path.Combine(directory.Path, "roslyn-tools-mcp");
        var buildHost = Path.Combine(directory.Path, "BuildHost-netcore");
        Directory.CreateDirectory(buildHost);
        File.WriteAllText(cli, "#!/bin/sh\nexit 0\n");
        File.WriteAllText(mcp, """
            #!/bin/sh
            read initialize
            read tools
            printf '%s\n' '{"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","serverInfo":{"name":"roslyn-tools-mcp","version":"1.0"}}}'
            printf '%s\n' '{"jsonrpc":"2.0","id":2,"result":{"tools":[{"name":"outline","inputSchema":{"type":"object"}}]}}'
            """);
        File.SetUnixFileMode(cli, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(mcp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        foreach (var name in new[]
                 {
                     "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll",
                     "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.runtimeconfig.json",
                     "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json"
                 })
            File.WriteAllText(Path.Combine(buildHost, name), "fixture");
        var probe = new DoctorEnvironmentProbe(
            cli,
            mcp,
            buildHost,
            TimeSpan.FromSeconds(2),
            _ => new DoctorSdkSelection(true, null, null));

        var result = await probe.ProbeAsync(directory.Path, CancellationToken.None);

        result.IsHealthy.ShouldBeTrue();
        result.ServerName.ShouldBe("roslyn-tools-mcp");
        result.ProtocolVersion.ShouldBe("2024-11-05");
        result.DiscoveredTools.ShouldBe(1);
        result.RequestedSdk.ShouldBeNull();
        result.SelectedSdk.ShouldBeNull();
        result.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void stdout_diagnostics_fail_json_rpc_purity()
    {
        var result = DoctorEnvironmentProbe.ValidateMcpOutput(
            "diagnostic text\n",
            string.Empty,
            0);

        result.Healthy.ShouldBeFalse();
        result.Diagnostics.ShouldContain(message => message.Contains("not pure JSON-RPC", StringComparison.Ordinal));
    }

    [Fact]
    public void stderr_is_captured_separately_from_valid_stdout_and_fails_the_self_check()
    {
        var stdout = """
            {"jsonrpc":"2.0","id":1,"result":{"protocolVersion":"2024-11-05","serverInfo":{"name":"roslyn-tools-mcp"}}}
            {"jsonrpc":"2.0","id":2,"result":{"tools":[{"inputSchema":{"type":"object"}}]}}
            """;

        var result = DoctorEnvironmentProbe.ValidateMcpOutput(stdout, "host diagnostic", 0);

        result.Healthy.ShouldBeFalse();
        result.Diagnostics.ShouldContain("MCP stderr: host diagnostic");
        result.Diagnostics.ShouldNotContain(message => message.Contains("not pure JSON-RPC", StringComparison.Ordinal));
    }

    [Fact]
    public async Task timed_out_mcp_process_is_terminated()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TemporaryDirectory();
        var cli = Path.Combine(directory.Path, "rt");
        var mcp = Path.Combine(directory.Path, "roslyn-tools-mcp");
        var pidFile = Path.Combine(directory.Path, "mcp.pid");
        var buildHost = Path.Combine(directory.Path, "BuildHost-netcore");
        File.WriteAllText(cli, "#!/bin/sh\nexit 0\n");
        File.WriteAllText(mcp, $"#!/bin/sh\necho $$ > \"{pidFile}\"\nsleep 30\n");
        File.SetUnixFileMode(cli, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(mcp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        CreateBuildHost(buildHost);
        var probe = new DoctorEnvironmentProbe(
            cli,
            mcp,
            buildHost,
            TimeSpan.FromMilliseconds(150),
            _ => new DoctorSdkSelection(true, null, "10.0.201"));

        var result = await probe.ProbeAsync(directory.Path, CancellationToken.None);

        result.McpHandshakeHealthy.ShouldBeFalse();
        result.Diagnostics.ShouldContain(message => message.Contains("timed out", StringComparison.Ordinal));
        var processId = int.Parse(File.ReadAllText(pidFile), CultureInfo.InvariantCulture);
        Should.Throw<ArgumentException>(() => Process.GetProcessById(processId));
    }

    [Fact]
    public async Task non_executable_mcp_launcher_returns_an_unhealthy_diagnostic()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var directory = new TemporaryDirectory();
        var cli = Path.Combine(directory.Path, "rt");
        var mcp = Path.Combine(directory.Path, "roslyn-tools-mcp");
        var buildHost = Path.Combine(directory.Path, "BuildHost-netcore");
        File.WriteAllText(cli, "#!/bin/sh\nexit 0\n");
        File.WriteAllText(mcp, "not executable");
        File.SetUnixFileMode(cli, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        File.SetUnixFileMode(mcp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        CreateBuildHost(buildHost);
        var probe = new DoctorEnvironmentProbe(
            cli,
            mcp,
            buildHost,
            TimeSpan.FromSeconds(1),
            _ => new DoctorSdkSelection(true, null, null));

        var result = await probe.ProbeAsync(directory.Path, CancellationToken.None);

        result.McpHandshakeHealthy.ShouldBeFalse();
        result.Diagnostics.ShouldContain(message =>
            message.Contains("handshake failed", StringComparison.Ordinal));
    }

    [Fact]
    public void out_of_range_numeric_json_rpc_id_fails_stdout_purity_without_escaping()
    {
        var stdout = """
            {"jsonrpc":"2.0","id":99999999999999999999,"result":{}}
            """;

        var result = DoctorEnvironmentProbe.ValidateMcpOutput(stdout, string.Empty, 0);

        result.Healthy.ShouldBeFalse();
        result.Diagnostics.ShouldContain(message =>
            message.Contains("not pure JSON-RPC", StringComparison.Ordinal));
    }

    private static void CreateBuildHost(string path)
    {
        Directory.CreateDirectory(path);
        foreach (var name in new[]
                 {
                     "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.dll",
                     "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.runtimeconfig.json",
                     "Microsoft.CodeAnalysis.Workspaces.MSBuild.BuildHost.deps.json"
                 })
            File.WriteAllText(Path.Combine(path, name), "fixture");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rt-doctor-host-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
