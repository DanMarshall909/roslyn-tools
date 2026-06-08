using RoslynTools.Services.SemanticDiff.Git;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Builds the two <see cref="SemanticDiffSource" /> snapshots that feed
///     <see cref="SemanticDiffService.Compare" /> from a git working directory, via an
///     injected <see cref="IGitClient" /> (so it is unit-testable with a fake). The CLI
///     command (batch 5) hands the result to the engine; this type is host-agnostic.
/// </summary>
internal sealed class GitSemanticDiffSourceProvider : IRtService
{
    private readonly IGitClient _git;

    public GitSemanticDiffSourceProvider(IGitClient git) =>
        _git = git ?? throw new ArgumentNullException(nameof(git));

    /// <summary>
    ///     Load the before / after source snapshots for a semantic diff.
    ///     <list type="bullet">
    ///         <item>
    ///             <b>Default mode</b> (<paramref name="toRef" /> <c>null</c>): compare the
    ///             working tree against <paramref name="fromRef" /> (defaults to
    ///             <c>HEAD</c>). Before = each changed <c>.cs</c> at the from-ref; After =
    ///             each changed <c>.cs</c> in the working tree (incl. untracked new files,
    ///             which appear After-only → Added downstream).
    ///         </item>
    ///         <item>
    ///             <b>Ref-vs-ref mode</b> (<paramref name="toRef" /> non-null): both refs are
    ///             validated; Before = content at <paramref name="fromRef" />, After =
    ///             content at <paramref name="toRef" />.
    ///         </item>
    ///     </list>
    ///     Only <c>.cs</c> paths (case-insensitive) are fed. For every changed path a
    ///     Before source is added iff it exists on the from-side and an After source iff it
    ///     exists on the to-side (union of both sides) — a path absent on a side is omitted
    ///     there, which makes it Added or Removed downstream and lets a moved class
    ///     (A.cs Before-only, B.cs After-only) match by FQN and classify as Unchanged.
    /// </summary>
    public (IReadOnlyList<SemanticDiffSource> Before, IReadOnlyList<SemanticDiffSource> After) Load(
        string? fromRef,
        string? toRef)
    {
        _git.EnsureRepository();

        var from = string.IsNullOrEmpty(fromRef) ? "HEAD" : fromRef;

        // toRef null => working-tree-vs-HEAD; otherwise ref-vs-ref. Validate every ref we
        // are about to read so a bad ref fails fast as an ArgumentException.
        _git.EnsureRefExists(from);
        if (toRef is not null)
            _git.EnsureRefExists(toRef);

        var changedPaths = toRef is null
            ? _git.GetWorkingTreeChangedPaths()
            : _git.GetChangedPathsBetween(from, toRef);

        var before = new List<SemanticDiffSource>();
        var after = new List<SemanticDiffSource>();

        foreach (var path in changedPaths)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            // Before side is always content at the from-ref.
            var beforeText = _git.ReadFileAtRef(from, path);
            if (beforeText is not null)
                before.Add(new SemanticDiffSource(path, beforeText));

            // After side is the working tree (default mode) or the to-ref (ref-vs-ref).
            var afterText = toRef is null
                ? _git.ReadWorkingTreeFile(path)
                : _git.ReadFileAtRef(toRef, path);
            if (afterText is not null)
                after.Add(new SemanticDiffSource(path, afterText));
        }

        return (before, after);
    }
}
