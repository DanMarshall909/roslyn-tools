namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>
///     Narrow abstraction over the <c>git</c> CLI, scoped to a single working
///     directory chosen at construction. Exists so
///     <see cref="GitSemanticDiffSourceProvider" /> can be unit-tested with a fake —
///     no real repository required. The one real implementation,
///     selected by host composition, shells <c>git</c> and carries integration
///     coverage against a temporary repository.
///     <para>
///         <b>Two error channels.</b> Repository / ref validation
///         (<see cref="EnsureRepository" />, <see cref="EnsureRefExists" />) throws —
///         <see cref="InvalidOperationException" /> for "not a git repository" and
///         <see cref="ArgumentException" /> for "invalid git ref" — and those propagate
///         to the CLI's existing user-error handler (stderr + exit 1). Content reads
///         (<see cref="ReadFileAtRef" />, <see cref="ReadWorkingTreeFile" />) never throw
///         for an absent path: they return <c>null</c>, which is what lets a file that
///         exists on only one side classify correctly downstream.
///     </para>
/// </summary>
internal interface IGitClient
{
    /// <summary>
    ///     Throw <see cref="InvalidOperationException" /> ("not a git repository: &lt;dir&gt;")
    ///     when the working directory is not inside a git repository
    ///     (<c>git rev-parse --git-dir</c> fails). No-op otherwise.
    /// </summary>
    void EnsureRepository();

    /// <summary>
    ///     Throw <see cref="ArgumentException" /> ("invalid git ref: &lt;ref&gt;") when
    ///     <paramref name="reference" /> does not resolve to a commit
    ///     (<c>git rev-parse --verify &lt;ref&gt;^{commit}</c> fails). No-op otherwise.
    /// </summary>
    void EnsureRefExists(string reference);

    /// <summary>
    ///     Paths changed between two refs:
    ///     <c>git diff --no-renames --name-only &lt;from&gt; &lt;to&gt;</c>.
    ///     <c>--no-renames</c> is mandatory so a class moved A.cs→B.cs lists <i>both</i>
    ///     paths, enabling cross-file move detection downstream.
    /// </summary>
    IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef);

    /// <summary>
    ///     Paths changed in the working tree relative to <c>HEAD</c>: the union of
    ///     <c>git diff --no-renames --name-only HEAD</c> (tracked, modified/deleted) and
    ///     <c>git ls-files --others --exclude-standard</c> (untracked, not ignored), so a
    ///     new un-added file counts as changed.
    /// </summary>
    IReadOnlyList<string> GetWorkingTreeChangedPaths();

    /// <summary>
    ///     Content of <paramref name="path" /> at <paramref name="reference" />
    ///     (<c>git show &lt;ref&gt;:&lt;path&gt;</c>). Returns <c>null</c> when the path did
    ///     not exist at that ref (git exits nonzero) rather than throwing — only repository
    ///     / ref validation is an error.
    /// </summary>
    string? ReadFileAtRef(string reference, string path);

    /// <summary>
    ///     Content of <paramref name="path" /> from the working tree (a filesystem read,
    ///     resolved against the client's working directory). Returns <c>null</c> when the
    ///     file is absent.
    /// </summary>
    string? ReadWorkingTreeFile(string path);
}
