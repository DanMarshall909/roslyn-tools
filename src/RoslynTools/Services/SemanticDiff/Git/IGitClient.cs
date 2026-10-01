namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>
///     Narrow abstraction over the <c>git</c> CLI, scoped to a single working
///     directory chosen at construction. Exists so
///     <see cref="GitSemanticDiffSourceProvider" /> can be unit-tested with a fake —
///     no real repository required. The one real implementation,
///     selected by host composition, shells <c>git</c> and carries integration
///     coverage against a temporary repository.
///     <para>
///         <b>Two error channels.</b> Repository / ref validation and changed-path
///         acquisition throw when git cannot establish a complete answer.
///         Validation (<see cref="EnsureRepository" />, <see cref="EnsureRefExists" />)
///         throws —
///         <see cref="InvalidOperationException" /> for "not a git repository" and
///         <see cref="ArgumentException" /> for "invalid git ref" — and those propagate
///         to the CLI's existing user-error handler (stderr + exit 1). Content reads
///         return <c>null</c> for a proven absent path, which lets a file that exists
///         on only one side classify correctly downstream. A failed Git content read
///         throws <see cref="GitContentReadException" />; the shared operation returns
///         it as a typed external-tool error to either host.
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
    ///     Resolve a symbolic or literal commit ref to one immutable Git object ID.
    ///     Throw <see cref="ArgumentException" /> for an invalid ref. Callers reuse
    ///     the returned ID for changed paths and content reads in one comparison.
    /// </summary>
    string ResolveCommit(string reference);

    /// <summary>
    ///     Fail before working-tree path discovery when a repository-contained content
    ///     read boundary is unavailable. Ref-vs-ref mode does not call this method.
    /// </summary>
    void EnsureWorkingTreeContentSupported();

    /// <summary>
    ///     Paths changed between two refs:
    ///     <c>git diff --no-renames --no-relative --name-only -z &lt;from&gt; &lt;to&gt; --</c>.
    ///     <c>--no-renames</c> is mandatory so a class moved A.cs→B.cs lists <i>both</i>
    ///     paths, enabling cross-file move detection downstream.
    ///     Throws <see cref="InvalidOperationException" /> if git fails.
    /// </summary>
    IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef);

    /// <summary>
    ///     Paths changed in the working tree relative to <paramref name="baselineRef" />
    ///     (defaults to <c>HEAD</c>): the union of
    ///     <c>git diff --no-renames --no-relative --name-only -z &lt;baseline&gt; --</c>
    ///     (tracked, modified/deleted across commits, index, and working tree)
    ///     and <c>git ls-files --others --exclude-standard --full-name -z -- :/</c>
    ///     (untracked, not ignored, across the repository root), so a new
    ///     un-added file counts as changed even from a nested working directory.
    ///     Both lists preserve literal repository-root-relative paths.
    ///     Throws <see cref="InvalidOperationException" /> if either git command
    ///     fails or git cannot start; an empty list requires successful commands.
    /// </summary>
    IReadOnlyList<string> GetWorkingTreeChangedPaths(string baselineRef = "HEAD");

    /// <summary>
    ///     Content of <paramref name="path" /> at <paramref name="reference" />
    ///     (<c>git show &lt;ref&gt;:&lt;path&gt;</c>). Returns <c>null</c> only after a successful
    ///     literal tree lookup proves the path absent. Throws
    ///     <see cref="GitContentReadException" /> if the lookup or content read fails.
    /// </summary>
    string? ReadFileAtRef(string reference, string path);

    /// <summary>
    ///     Content of <paramref name="path" /> from the working tree (a filesystem read,
    ///     resolved against the repository root). Returns <c>null</c> only for an
    ///     observed missing path on a platform with a contained read boundary.
    ///     Rejects traversal, symlinks, mount crossings, nonregular and unreadable
    ///     files with <see cref="GitContentReadException" />. On unsupported platforms,
    ///     throws the same contextual exception without reading working-tree content.
    /// </summary>
    string? ReadWorkingTreeFile(string path);
}
