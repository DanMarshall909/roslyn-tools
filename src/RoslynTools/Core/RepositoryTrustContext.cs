namespace RoslynTools.Core;

/// <summary>
///     Invocation-scoped authority for the trusted-local preview. Ordinary typed requests
///     cannot set this value; only a trusted host surface may establish the scope before
///     application code reaches the workspace loader.
/// </summary>
internal static class RepositoryTrustContext
{
    private static readonly AsyncLocal<TrustAuthorization?> ExplicitTrust = new();

    internal static bool IsExplicitlyTrusted => ExplicitTrust.Value is { } authorization
                                               && (authorization.RepositoryRoots is null
                                                   || authorization.RepositoryRoots.Count > 0);

    internal static bool IsExplicitlyTrustedFor(string path)
    {
        var authorization = ExplicitTrust.Value;
        if (authorization is null)
            return false;

        if (authorization.RepositoryRoots is null)
            return true;

        var fullPath = Path.GetFullPath(path);
        return authorization.RepositoryRoots.Any(root => IsWithinRoot(root, fullPath));
    }

    internal static IDisposable UseExplicitTrust()
    {
        return UseExplicitTrust(new TrustAuthorization(null));
    }

    internal static IDisposable UseExplicitTrust(string repositoryRoot)
    {
        return UseExplicitTrust([repositoryRoot]);
    }

    internal static IDisposable UseExplicitTrust(IEnumerable<string> repositoryRoots)
    {
        ArgumentNullException.ThrowIfNull(repositoryRoots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var roots = repositoryRoots.Select(root =>
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            if (!Path.IsPathFullyQualified(root))
                throw new ArgumentException("The trusted repository root must be an absolute path.", nameof(repositoryRoots));

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        }).Distinct(comparer).ToArray();

        return UseExplicitTrust(new TrustAuthorization(Array.AsReadOnly(roots)));
    }

    private static bool IsWithinRoot(string root, string fullPath)
    {
        var relativePath = Path.GetRelativePath(root, fullPath);
        var isWithinRoot = relativePath == "."
               || !Path.IsPathRooted(relativePath)
               && relativePath != ".."
               && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
        if (!isWithinRoot)
            return false;

        try
        {
            // Reject redirected descendants as well as redirected approved roots. This
            // is a conservative preview check, not a physical-identity or race-free lease.
            RepositoryTrustStore.RejectLinkedPath(fullPath);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static Scope UseExplicitTrust(TrustAuthorization authorization)
    {
        var previous = ExplicitTrust.Value;
        ExplicitTrust.Value = authorization;
        return new Scope(() => ExplicitTrust.Value = previous);
    }

    private sealed record TrustAuthorization(IReadOnlyList<string>? RepositoryRoots);

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        void IDisposable.Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

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
        + "After review, rerun the CLI command with --trust-repository or start the MCP host with "
        + "--trust-repository <absolute-repository-root>. MCP tool arguments cannot grant trust.")
    {
        SolutionPath = solutionPath
    };
}
