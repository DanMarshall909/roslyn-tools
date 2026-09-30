using System.Text.Json.Serialization;

namespace RoslynTools.Core;

internal interface ICompactWorkspaceEvidence
{
    object CompactWorkspaceEvidence();
}

internal sealed record RtSourceIdentity(
    string? RepositoryRoot,
    string? SolutionPath,
    string? Revision);

internal sealed record RtWorkspaceProvenance(
    string State,
    int ProjectCount,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<RtWorkspaceInput>? Inputs = null,
    IReadOnlyList<string>? RequestedProjects = null,
    IReadOnlyList<string>? FailedProjects = null,
    string? CompilerVersion = null,
    string? EvaluatorPolicy = null,
    WorkspaceEvaluatorProvenance? Evaluator = null,
    string? ManifestState = null)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RtWorkspaceEvidenceSummary? EvidenceSummary { get; init; }
}

internal sealed record RtWorkspaceEvidenceSummary(
    int TotalInputs,
    int InputsOmitted,
    IReadOnlyList<RtEvidenceCount> InputStates,
    int TotalDiagnostics,
    int DiagnosticsOmitted,
    int DistinctDiagnosticGroups,
    int DiagnosticGroupsOmitted,
    IReadOnlyList<RtDiagnosticGroupCount> DiagnosticGroups);

internal sealed record RtEvidenceCount(string State, int Count);

internal sealed record RtDiagnosticGroupCount(
    string Code,
    string Severity,
    string Origin,
    string CompletenessImpact,
    int Count);

/// <summary>
/// A declared semantic input and the outcome of acquiring it. The input list makes a
/// workspace completeness claim inspectable instead of inferring it from a project count.
/// </summary>
internal sealed record RtWorkspaceInput(
    string Id,
    string Kind,
    string State,
    bool Required,
    string? Project = null,
    string? Detail = null,
    bool RequiredForMinimumScope = false,
    string? Context = null,
    string? Path = null,
    string? ExpectedFingerprint = null,
    string? ObservedFingerprint = null,
    bool ExclusionValidated = false);

internal sealed record RtConfidence(
    string Level,
    string Basis,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Limitations);

internal sealed record RtStructuredDiagnostic(
    string Code,
    string Severity,
    string Message,
    IReadOnlyDictionary<string, string>? Context = null);

internal sealed record RtNextAction(string Capability, string Reason);

internal sealed record RtResultSafety(
    IReadOnlyList<string> Effects,
    bool AppliesRepositoryChanges,
    bool ReviewRequired);

internal sealed record RtTiming(long ElapsedMilliseconds);

internal sealed record RtPaging(
    int TotalItems,
    int ReturnedItems,
    bool Truncated,
    string? ContinuationToken);

/// <summary>The first versioned, host-neutral structured result contract.</summary>
internal sealed record RtResultEnvelope<T>(
    string SchemaVersion,
    string Operation,
    string Outcome,
    RtSourceIdentity Source,
    RtWorkspaceProvenance Workspace,
    T Data,
    RtConfidence Confidence,
    IReadOnlyList<RtStructuredDiagnostic> Diagnostics,
    IReadOnlyList<string> Limitations,
    IReadOnlyList<RtNextAction> NextActions,
    RtResultSafety Safety,
    RtTiming Timing,
    RtPaging Paging) : ICompactWorkspaceEvidence
{
    private const int MaximumInputSamples = 16;
    private const int MaximumDiagnosticGroups = 24;
    private const int MaximumFieldLength = 512;

    object ICompactWorkspaceEvidence.CompactWorkspaceEvidence()
    {
        var inputs = Workspace.Inputs ?? [];
        if (inputs.Count <= MaximumInputSamples
            && Diagnostics.Count <= MaximumDiagnosticGroups
            && Workspace.Diagnostics.Count <= 8)
            return this;

        var shownInputs = inputs
            .Where(input => !string.Equals(input.State, "Loaded", StringComparison.Ordinal))
            .OrderBy(input => InputStateRank(input.State))
            .ThenBy(input => input.Id, StringComparer.Ordinal)
            .Take(MaximumInputSamples)
            .Select(CompactInput)
            .ToArray();
        var groups = Diagnostics
            .GroupBy(diagnostic => new
            {
                diagnostic.Code,
                diagnostic.Severity,
                Origin = ContextValue(diagnostic, "origin"),
                CompletenessImpact = ContextValue(diagnostic, "completenessImpact")
            })
            .OrderByDescending(group => group.Key.CompletenessImpact == "RequiredInputIncomplete")
            .ThenBy(group => Math.Min(SeverityRank(group.Key.Severity), 2))
            .ThenByDescending(group => group.Count())
            .ThenBy(group => SeverityRank(group.Key.Severity))
            .ThenBy(group => group.Key.Code, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Origin, StringComparer.Ordinal)
            .ToArray();
        var shownGroups = groups.Take(MaximumDiagnosticGroups).ToArray();
        var shownDiagnostics = shownGroups.Select(group =>
        {
            var first = group.First();
            return new RtStructuredDiagnostic(
                Truncate(first.Code),
                Truncate(first.Severity),
                Truncate(first.Message),
                new Dictionary<string, string>
                {
                    ["origin"] = Truncate(group.Key.Origin),
                    ["completenessImpact"] = Truncate(group.Key.CompletenessImpact)
                });
        }).ToArray();
        var shownLegacyDiagnostics = Diagnostics.Count == 0
            ? Workspace.Diagnostics.Distinct(StringComparer.Ordinal).Take(8).Select(Truncate).ToArray()
            : [];
        var totalDiagnostics = Diagnostics.Count == 0 ? Workspace.Diagnostics.Count : Diagnostics.Count;
        var summary = new RtWorkspaceEvidenceSummary(
            inputs.Count,
            inputs.Count - shownInputs.Length,
            inputs.GroupBy(input => input.State, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new RtEvidenceCount(Truncate(group.Key), group.Count()))
                .ToArray(),
            totalDiagnostics,
            totalDiagnostics - (Diagnostics.Count == 0 ? shownLegacyDiagnostics.Length : shownDiagnostics.Length),
            groups.Length,
            groups.Length - shownGroups.Length,
            shownGroups.Select(group => new RtDiagnosticGroupCount(
                Truncate(group.Key.Code),
                Truncate(group.Key.Severity),
                Truncate(group.Key.Origin),
                Truncate(group.Key.CompletenessImpact),
                group.Count())).ToArray());
        return this with
        {
            SchemaVersion = "rt.result.v2",
            Workspace = Workspace with
            {
                Diagnostics = shownLegacyDiagnostics,
                Inputs = shownInputs,
                EvidenceSummary = summary
            },
            Diagnostics = shownDiagnostics
        };
    }

    private static RtWorkspaceInput CompactInput(RtWorkspaceInput input) => input with
    {
        Id = Truncate(input.Id),
        Kind = Truncate(input.Kind),
        State = Truncate(input.State),
        Project = TruncateOptional(input.Project),
        Detail = TruncateOptional(input.Detail),
        Context = TruncateOptional(input.Context),
        Path = TruncateOptional(input.Path)
    };

    private static string ContextValue(RtStructuredDiagnostic diagnostic, string key) =>
        diagnostic.Context is not null && diagnostic.Context.TryGetValue(key, out var value)
            ? value
            : string.Empty;

    private static int SeverityRank(string severity) => severity switch
    {
        "Error" => 0,
        "Warning" => 1,
        "Info" => 2,
        _ => 3
    };

    private static int InputStateRank(string state) => state switch
    {
        "Failed" or "PolicyBlocked" or "Unknown" => 0,
        "Excluded" => 1,
        "Resolved" => 2,
        _ => 3
    };

    private static string Truncate(string value) =>
        value.Length <= MaximumFieldLength
            ? value
            : string.Concat(value.AsSpan(0, MaximumFieldLength - 1), "…");

    private static string? TruncateOptional(string? value) =>
        value is null ? null : Truncate(value);
}
