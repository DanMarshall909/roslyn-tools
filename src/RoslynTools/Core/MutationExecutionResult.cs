using System.Text.Json.Serialization;

namespace RoslynTools.Core;

/// <summary>
///     Structured result of a daemon-owned mutation before any adapter renders it.
///     For a dry run, <see cref="AffectedPaths" /> contains the paths that would change.
/// </summary>
internal sealed record MutationExecutionResult<T>
{
    [JsonConstructor]
    internal MutationExecutionResult(
        MutationExecutionStatus status,
        T? value,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<MutationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(affectedPaths);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if ((status is MutationExecutionStatus.Reviewed or MutationExecutionStatus.NoOp)
            && affectedPaths.Count != 0)
            throw new ArgumentException(
                "A reviewed or no-op mutation cannot contain affected paths.",
                nameof(affectedPaths));

        if ((status is MutationExecutionStatus.DryRun or MutationExecutionStatus.Applied)
            && affectedPaths.Count == 0)
            throw new ArgumentException(
                "A dry-run or applied mutation must identify at least one affected path.",
                nameof(affectedPaths));

        Status = status;
        Value = value;
        AffectedPaths = affectedPaths.ToArray();
        Diagnostics = diagnostics.ToArray();
    }

    public MutationExecutionStatus Status { get; }

    public T? Value { get; }

    [JsonPropertyName("changedFiles")]
    public IReadOnlyList<string> AffectedPaths { get; }

    /// <summary>
    ///     Compatibility alias for callers that have not yet migrated to the broader
    ///     affected-path contract. The wire property remains <c>changedFiles</c>.
    /// </summary>
    [JsonIgnore]
    public IReadOnlyList<string> ChangedFiles => AffectedPaths;

    public IReadOnlyList<MutationDiagnostic> Diagnostics { get; }

    internal static MutationExecutionResult<T> Reviewed(
        T? value,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null) =>
        new(
            MutationExecutionStatus.Reviewed,
            value,
            [],
            diagnostics ?? []);

    internal static MutationExecutionResult<T> DryRun(
        T? value,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null) =>
        new(
            MutationExecutionStatus.DryRun,
            value,
            affectedPaths,
            diagnostics ?? []);

    internal static MutationExecutionResult<T> Applied(
        T? value,
        IReadOnlyList<string> affectedPaths,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null) =>
        new(
            MutationExecutionStatus.Applied,
            value,
            affectedPaths,
            diagnostics ?? []);

    internal static MutationExecutionResult<T> NoOp(
        T? value = default,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null) =>
        new(
            MutationExecutionStatus.NoOp,
            value,
            [],
            diagnostics ?? []);
}

internal enum MutationExecutionStatus
{
    Reviewed,
    DryRun,
    Applied,
    NoOp
}

/// <summary>A transport-neutral diagnostic accompanying a mutation result.</summary>
internal sealed record MutationDiagnostic(
    MutationDiagnosticSeverity Severity,
    string Message,
    string? Code = null,
    IReadOnlyDictionary<string, string>? Context = null);

internal enum MutationDiagnosticSeverity
{
    Information,
    Warning,
    Error
}
