using System.Text.Json;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class SuggestUnsupportedSurfaceTests
{
    [Fact]
    public async Task Plan_verification_request_has_an_honest_json_and_text_outcome()
    {
        var json = await RtCliHost.RunAsync(
            "suggest \"Verify a prepared plan without applying\" --top 3 --json");
        var text = await RtCliHost.RunAsync(
            "suggest \"Verify a prepared plan without applying\" --top 3 --llm");

        json.ExitCode.ShouldBe(0);
        using var payload = JsonDocument.Parse(json.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("verify-plan");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
        text.ExitCode.ShouldBe(0);
        text.Stdout.ShouldContain("Unsupported: verify-plan is not available yet.");

        var immutablePlan = await RtCliHost.RunAsync(
            "suggest \"I need an immutable revision-bound rename plan\" --top 3 --json");
        immutablePlan.ExitCode.ShouldBe(0);
        using var immutablePayload = JsonDocument.Parse(immutablePlan.Stdout);
        immutablePayload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        immutablePayload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("prepare-plan");
        immutablePayload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
    }
}
