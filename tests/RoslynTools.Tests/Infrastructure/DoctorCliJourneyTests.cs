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
        root.GetProperty("data").GetProperty("workspace").GetProperty("state")
            .GetString().ShouldBe("NotRequested");
    }

    [Fact]
    public async Task packaged_cli_returns_the_same_versioned_trust_required_contract()
    {
        var solution = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");
        var result = await RtCliHost.RunAsync(
            $"inspect doctor --directory \"{RtCliHost.RepoRoot}\" --solution \"{solution}\" --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var document = JsonDocument.Parse(result.Stdout);
        var root = document.RootElement;
        root.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v1");
        root.GetProperty("operation").GetString().ShouldBe("doctor");
        root.GetProperty("outcome").GetString().ShouldBe("TrustRequired");
        root.GetProperty("data").GetProperty("installation")
            .GetProperty("daemonRequired").GetBoolean().ShouldBeFalse();
    }
}
