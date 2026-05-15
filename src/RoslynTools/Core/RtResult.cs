namespace RoslynTools.Core;

internal sealed record RtResult<T>(
    RtResultStatus Status,
    T? Value,
    IReadOnlyList<string> Diagnostics,
    IReadOnlyList<string> ChangedFiles,
    int ExitCode)
{
    public static RtResult<T> Ok(T value) => new(
        RtResultStatus.Ok,
        value,
        [],
        [],
        0);
}

internal enum RtResultStatus
{
    Ok,
    NotFound,
    Ambiguous,
    Unsupported,
    Failed
}
