using System.Text.Json;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class DoctorCliJourneyTests
{
    [Fact]
    public async Task installation_only_has_no_solution_requirement_and_fails_with_nonzero_exit_when_unpackaged()
    {
        var result = await RtCliHost.RunAsync("inspect doctor --installation-only --json");

        result.ExitCode.ShouldBe(1);
        using var document = JsonDocument.Parse(result.Stdout);
        var root = document.RootElement;
        root.GetProperty("outcome").GetString().ShouldBe("Failed");
        var installation = root.GetProperty("data").GetProperty("installation");
        installation.GetProperty("sdkRequestState").GetString().ShouldBe("Valid");
        installation.GetProperty("globalJsonPath").GetString().ShouldEndWith("global.json");
        root.GetProperty("data").GetProperty("workspace").GetProperty("state")
            .GetString().ShouldBe("NotRequested");
    }

    [Fact]
    public async Task root_doctor_shortcut_reaches_installation_only_without_workspace_bootstrap()
    {
        var result = await RtCliHost.RunAsync("--json doctor --installation-only");

        result.ExitCode.ShouldBe(1);
        using var document = JsonDocument.Parse(result.Stdout);
        document.RootElement.GetProperty("data").GetProperty("workspace")
            .GetProperty("state").GetString().ShouldBe("NotRequested");
    }

    [Fact]
    public async Task trust_list_runs_with_an_isolated_empty_profile()
    {
        using var profile = new TemporaryDirectory();
        var result = await RtCliHost.RunAsync(
            "--json trust list",
            environment: new Dictionary<string, string?>
            {
                ["XDG_DATA_HOME"] = profile.Path,
                ["LOCALAPPDATA"] = profile.Path
            });

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var document = JsonDocument.Parse(result.Stdout);
        document.RootElement.GetProperty("repositories").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task packaged_cli_returns_the_same_versioned_trust_required_contract()
    {
        using var profile = new TemporaryDirectory();
        var solution = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");
        var result = await RtCliHost.RunAsync(
            $"inspect doctor --directory \"{RtCliHost.RepoRoot}\" --solution \"{solution}\" --json",
            environment: new Dictionary<string, string?>
            {
                ["XDG_DATA_HOME"] = profile.Path,
                ["LOCALAPPDATA"] = profile.Path
            });

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var document = JsonDocument.Parse(result.Stdout);
        var root = document.RootElement;
        root.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v1");
        root.GetProperty("operation").GetString().ShouldBe("doctor");
        root.GetProperty("outcome").GetString().ShouldBe("TrustRequired");
        root.GetProperty("data").GetProperty("installation")
            .GetProperty("daemonRequired").GetBoolean().ShouldBeFalse();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"rt-doctor-profile-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
