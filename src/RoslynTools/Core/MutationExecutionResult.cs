using System.Text.Json.Serialization;

namespace RoslynTools.Core;

/// <summary>
///     Structured result of a daemon-owned mutation before any adapter renders it.
///     For a dry run, <see cref="ChangedFiles" /> contains the files that would change.
/// </summary>
internal sealed record MutationExecutionResult<T>
{
    [JsonConstructor]
    internal MutationExecutionResult(
        MutationExecutionStatus status,
        T? value,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<MutationDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(changedFiles);
        ArgumentNullException.ThrowIfNull(diagnostics);

        if (status == MutationExecutionStatus.NoOp && changedFiles.Count != 0)
            throw new ArgumentException("A no-op mutation cannot contain changed files.", nameof(changedFiles));

        if (status != MutationExecutionStatus.NoOp && changedFiles.Count == 0)
            throw new ArgumentException(
                "A dry-run or applied mutation must identify at least one changed file.",
                nameof(changedFiles));

        Status = status;
        Value = value;
        ChangedFiles = changedFiles;
        Diagnostics = diagnostics;
    }

    public MutationExecutionStatus Status { get; }

    public T? Value { get; }

    public IReadOnlyList<string> ChangedFiles { get; }

    public IReadOnlyList<MutationDiagnostic> Diagnostics { get; }

    internal static MutationExecutionResult<T> DryRun(
        T? value,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null) =>
        new(
            MutationExecutionStatus.DryRun,
            value,
            changedFiles,
            diagnostics ?? []);

    internal static MutationExecutionResult<T> Applied(
        T? value,
        IReadOnlyList<string> changedFiles,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null) =>
        new(
            MutationExecutionStatus.Applied,
            value,
            changedFiles,
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
