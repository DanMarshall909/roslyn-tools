using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands.Results;

/// <summary>CLI presentation over the canonical host-neutral find-unused envelope.</summary>
internal sealed class FindUnusedEnvelopeResult(RtResultEnvelope<FindUnusedAnalysisResult> envelope)
    : IRtOutput, IRtJsonOutput
{
    private readonly FindUnusedResult _symbols = FindUnusedResult.FromAnalysis(envelope.Data);

    [OutputField(OutputLevel.Llm, "workspace")]
    public string Workspace => envelope.Workspace.State;

    [OutputField(OutputLevel.Llm, "limitations")]
    public string Limitations => string.Join(" ", envelope.Limitations);

    [OutputField(OutputLevel.Llm, "diagnostics")]
    public string Diagnostics => string.Join(Environment.NewLine,
        DisplayedEnvelope().Diagnostics.Where(diagnostic => RtContext.ShowWorkspaceWarnings
                || diagnostic.Severity == "Error"
                || diagnostic.Context is { } context
                    && context.TryGetValue("completenessImpact", out var impact) && impact != "None")
            .Select(diagnostic => $"{diagnostic.Severity} {diagnostic.Code}: {diagnostic.Message}"));

    [OutputField(OutputLevel.Llm, "symbols")]
    public IReadOnlyList<UnusedSymbolResult> Symbols => _symbols.Symbols;

    [OutputField(OutputLevel.Llm, "profile")]
    public string? ProfileSummary => _symbols.ProfileSummary;

    public void RenderJson(TextWriter writer)
        => writer.Write(JsonSerializer.Serialize(DisplayedEnvelope(), JsonEncoders.Select(RtContext.RelaxJson)));

    private RtResultEnvelope<FindUnusedAnalysisResult> DisplayedEnvelope() =>
        RtContext.FullWorkspaceEvidence
            ? envelope
            : (RtResultEnvelope<FindUnusedAnalysisResult>)((ICompactWorkspaceEvidence)envelope).CompactWorkspaceEvidence();

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"Workspace: {Workspace}");
        if (Limitations.Length > 0)
            writer.WriteLine($"Limitations: {Limitations}");
        if (Diagnostics.Length > 0)
            writer.WriteLine(Diagnostics);
        _symbols.RenderText(writer);
    }
}
