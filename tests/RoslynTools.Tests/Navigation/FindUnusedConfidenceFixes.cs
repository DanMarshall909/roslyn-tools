using RoslynTools.Commands.Results;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

/// <summary>
/// US-138 AC6: a finding carries confidence + reason, and only a high-confidence finding
/// offers a deletion-candidate fix. rt does not decide what to do about a low-confidence finding —
/// the calling LLM does — but rt must not hand it a deletion command for something the
/// analysis could not prove is dead.
/// </summary>
public class FindUnusedConfidenceFixes
{
    private const string SolutionPath = "/path/to/App.slnx";

    [Fact]
    public void high_confidence_finding_offers_a_deletion_candidate_fix()
    {
        var symbol = new UnusedSymbol("OrphanType", "type", "/path/to/Models.cs", 7);

        var result = FindUnusedResult.FromSymbols([symbol], SolutionPath);

        result.Symbols.Single().SuggestedFixes.ShouldNotBeEmpty(
            "a finding the analysis can stand behind is eligible for candidate planning");
    }

    [Fact]
    public void low_confidence_finding_offers_no_deletion_candidate_fix()
    {
        var symbol = new UnusedSymbol(
            "ReflectiveService",
            "type",
            "/path/to/Services.cs",
            12,
            UnusedConfidence.Low,
            "Project registers services by assembly scan.");

        var result = FindUnusedResult.FromSymbols([symbol], SolutionPath);

        result.Symbols.Single().SuggestedFixes.ShouldBeEmpty(
            "an uncertain finding must never hand the caller a command that deletes live code");
    }

    [Fact]
    public void low_confidence_finding_is_still_reported_with_its_reason()
    {
        const string reason = "Project registers services by assembly scan.";
        var symbol = new UnusedSymbol(
            "ReflectiveService", "type", "/path/to/Services.cs", 12, UnusedConfidence.Low, reason);

        var result = FindUnusedResult.FromSymbols([symbol], SolutionPath);

        var rendered = result.Symbols.Single();
        rendered.Confidence.ShouldBe(UnusedConfidence.Low);
        rendered.Reason.ShouldBe(reason,
            "the reason is what lets the calling LLM decide whether to investigate or ignore");
    }
}
