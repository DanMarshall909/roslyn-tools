namespace RoslynTools.Core;

/// <summary>
///     Invocation-scoped authority for the trusted-local preview. Ordinary typed requests
///     cannot set this value; only a trusted host surface may establish the scope before
///     application code reaches the workspace loader.
/// </summary>
internal static class RepositoryTrustContext
{
    private static readonly AsyncLocal<TrustAuthorization?> ExplicitTrust = new();

    internal static bool IsExplicitlyTrusted => ExplicitTrust.Value is not null;

    internal static bool IsPersistentlyTrustedFor(string path) =>
        ExplicitTrust.Value?.PersistentRegistry?.IsTrustedFor(path) == true;

    internal static bool RequiresAbsoluteSolutionPath =>
        ExplicitTrust.Value?.RequireAbsoluteSolutionPath == true;

    internal static string? TrustedRepositoryRoot => ExplicitTrust.Value?.RepositoryRoot;

    internal static bool IsExplicitlyTrustedFor(string path)
    {
        var authorization = ExplicitTrust.Value;
        if (authorization is null)
            return false;

        if (authorization.PersistentRegistry is not null)
            return authorization.PersistentRegistry.IsTrustedFor(path);

        if (authorization.RepositoryRoot is null)
            return true;

        var fullPath = CanonicalizeForContainment(path);
        var relativePath = Path.GetRelativePath(authorization.RepositoryRoot, fullPath);
        return relativePath == "."
               || !Path.IsPathRooted(relativePath)
               && relativePath != ".."
               && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    internal static TrustedWorkspaceIdentity ResolveWorkspaceIdentity(string solutionPath)
    {
        var authorization = ExplicitTrust.Value
            ?? throw RepositoryTrustRequiredException.ForSolution(solutionPath);
        var canonicalSolution = RepositoryTrustRegistry.CanonicalizeExistingPath(
            Path.GetFullPath(solutionPath));
        if (!IsExplicitlyTrustedFor(canonicalSolution))
            throw RepositoryTrustRequiredException.ForSolution(canonicalSolution);

        var binding = RepositoryTrustRegistry.ResolveBinding(canonicalSolution);
        if (binding.IsOk)
        {
            var repository = binding.Unwrap();
            return new TrustedWorkspaceIdentity(
                canonicalSolution,
                repository.RepositoryId,
                repository.WorktreeRoot);
        }

        if (authorization.PersistentRegistry is not null)
            throw RepositoryTrustRequiredException.ForSolution(canonicalSolution);

        var trustedRoot = authorization.RepositoryRoot;
        if (trustedRoot is null)
        {
            trustedRoot = Path.GetDirectoryName(canonicalSolution)
                ?? throw new InvalidOperationException(
                    $"The solution path has no containing directory: '{canonicalSolution}'.");
        }

        var canonicalRoot = RepositoryTrustRegistry.CanonicalizeExistingPath(trustedRoot);
        if (!RepositoryTrustRegistry.IsContained(canonicalRoot, canonicalSolution))
            throw RepositoryTrustRequiredException.ForSolution(canonicalSolution);

        return new TrustedWorkspaceIdentity(
            canonicalSolution,
            $"explicit:{NormalizeIdentityPath(canonicalRoot)}",
            canonicalRoot);
    }

    private static string NormalizeIdentityPath(string path) => OperatingSystem.IsWindows()
        ? path.ToUpperInvariant()
        : path;

    internal static IDisposable UseExplicitTrust()
    {
        return UseExplicitTrust(new TrustAuthorization(null, null, false));
    }

    internal static IDisposable UseExplicitTrust(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Path.IsPathFullyQualified(repositoryRoot))
            throw new ArgumentException("The trusted repository root must be an absolute path.", nameof(repositoryRoot));

        var canonicalRoot = CanonicalizeForContainment(repositoryRoot);
        return UseExplicitTrust(new TrustAuthorization(canonicalRoot, null, false));
    }

    private static string CanonicalizeForContainment(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (OperatingSystem.IsWindows())
            return Path.TrimEndingDirectorySeparator(fullPath);

        var existingAncestor = fullPath;
        var missingSegments = new Stack<string>();
        while (!Path.Exists(existingAncestor))
        {
            var entry = new DirectoryInfo(existingAncestor);
            if (entry.LinkTarget is not null)
                return RepositoryTrustRegistry.CanonicalizeExistingPath(fullPath);

            var parent = Path.GetDirectoryName(existingAncestor);
            if (parent is null || parent == existingAncestor)
                break;

            missingSegments.Push(Path.GetFileName(existingAncestor));
            existingAncestor = parent;
        }

        if (missingSegments.Count > 0 && !Directory.Exists(existingAncestor))
            throw new IOException(
                $"Repository path traverses a non-directory component: '{existingAncestor}'.");

        var canonicalPath = RepositoryTrustRegistry.CanonicalizeExistingPath(existingAncestor);
        while (missingSegments.TryPop(out var segment))
            canonicalPath = Path.Combine(canonicalPath, segment);

        return Path.TrimEndingDirectorySeparator(canonicalPath);
    }

    internal static IDisposable UsePersistentTrust(
        RepositoryTrustRegistry registry,
        bool requireAbsoluteSolutionPath = false)
    {
        ArgumentNullException.ThrowIfNull(registry);
        return UseExplicitTrust(new TrustAuthorization(
            null,
            registry,
            requireAbsoluteSolutionPath));
    }

    private static Scope UseExplicitTrust(TrustAuthorization authorization)
    {
        var previous = ExplicitTrust.Value;
        ExplicitTrust.Value = authorization;
        return new Scope(() => ExplicitTrust.Value = previous);
    }

    private sealed record TrustAuthorization(
        string? RepositoryRoot,
        RepositoryTrustRegistry? PersistentRegistry,
        bool RequireAbsoluteSolutionPath);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        void IDisposable.Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

internal sealed record TrustedWorkspaceIdentity(
    string SolutionPath,
    string RepositoryId,
    string WorktreeRoot);

/// <summary>
///     Signals that a semantic workspace load was attempted without invocation-scoped
///     repository trust. A trusted host surface may establish that authority; request payloads cannot.
/// </summary>
internal sealed class RepositoryTrustRequiredException : InvalidOperationException
{
    /// <summary>Initializes an exception without a custom message.</summary>
    public RepositoryTrustRequiredException()
    {
    }

    /// <summary>Initializes an exception with an actionable trust-boundary message.</summary>
    public RepositoryTrustRequiredException(string? message) : base(message)
    {
    }

    /// <summary>Initializes an exception with a message and its underlying cause.</summary>
    public RepositoryTrustRequiredException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }

    internal string SolutionPath { get; private init; } = string.Empty;

    internal static RepositoryTrustRequiredException ForSolution(string solutionPath) => new(
        $"Repository evaluation is not trusted for '{solutionPath}'. "
        + "After review, run 'rt trust add <repository>' or use the invocation-scoped "
        + "--trust-repository option. MCP tool arguments cannot grant trust.")
    {
        SolutionPath = solutionPath
    };
}
