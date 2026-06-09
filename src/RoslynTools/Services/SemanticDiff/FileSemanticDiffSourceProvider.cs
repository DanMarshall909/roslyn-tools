namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Builds the two <see cref="SemanticDiffSource" /> snapshots that feed
///     <see cref="SemanticDiffService.Compare" /> from two files on disc, via an injected
///     <see cref="IFileTextReader" /> (so it is unit-testable with a fake). Unlike
///     <see cref="GitSemanticDiffSourceProvider" /> this touches no git state, so it works
///     anywhere — inside a repo, outside one, on files that were never committed.
/// </summary>
internal sealed class FileSemanticDiffSourceProvider : IRtService
{
    private readonly IFileTextReader _files;

    public FileSemanticDiffSourceProvider(IFileTextReader files) =>
        _files = files ?? throw new ArgumentNullException(nameof(files));

    /// <summary>
    ///     Load <paramref name="leftPath" /> as the "before" snapshot and
    ///     <paramref name="rightPath" /> as the "after" snapshot. A path that does not exist
    ///     throws <see cref="IOException" /> naming the missing file, which the CLI renders as
    ///     a clean error (exit 1, stderr) — never an unhandled exception. Each side carries its
    ///     own path as provenance, so a declaration present in only one file classifies as
    ///     Added / Removed and a same-FQN declaration is compared trivia-insensitively, exactly
    ///     as in git mode.
    /// </summary>
    public (IReadOnlyList<SemanticDiffSource> Before, IReadOnlyList<SemanticDiffSource> After) Load(
        string leftPath,
        string rightPath)
    {
        var leftText = _files.TryReadAllText(leftPath)
            ?? throw new IOException($"file not found: {leftPath}");
        var rightText = _files.TryReadAllText(rightPath)
            ?? throw new IOException($"file not found: {rightPath}");

        return (
            [new SemanticDiffSource(leftPath, leftText)],
            [new SemanticDiffSource(rightPath, rightText)]);
    }
}
