namespace RoslynTools.Core;

internal sealed record RtSourceIdentity(
    string? RepositoryRoot,
    string? SolutionPath,
    string? Revision);

internal sealed record RtWorkspaceProvenance(
    string State,
    int ProjectCount,
    IReadOnlyList<string> Diagnostics);

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
    RtPaging Paging);
