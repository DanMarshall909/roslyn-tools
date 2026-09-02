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

    internal static bool IsExplicitlyTrustedFor(string path)
    {
        var authorization = ExplicitTrust.Value;
        if (authorization is null)
            return false;

        if (authorization.RepositoryRoot is null)
            return true;

        var fullPath = Path.GetFullPath(path);
        var relativePath = Path.GetRelativePath(authorization.RepositoryRoot, fullPath);
        return relativePath == "."
               || !Path.IsPathRooted(relativePath)
               && relativePath != ".."
               && !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relativePath.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    internal static IDisposable UseExplicitTrust()
    {
        return UseExplicitTrust(new TrustAuthorization(null));
    }

    internal static IDisposable UseExplicitTrust(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        if (!Path.IsPathFullyQualified(repositoryRoot))
            throw new ArgumentException("The trusted repository root must be an absolute path.", nameof(repositoryRoot));

        var canonicalRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repositoryRoot));
        return UseExplicitTrust(new TrustAuthorization(canonicalRoot));
    }

    private static Scope UseExplicitTrust(TrustAuthorization authorization)
    {
        var previous = ExplicitTrust.Value;
        ExplicitTrust.Value = authorization;
        return new Scope(() => ExplicitTrust.Value = previous);
    }

    private sealed record TrustAuthorization(string? RepositoryRoot);

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
