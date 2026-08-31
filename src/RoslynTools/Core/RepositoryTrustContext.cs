namespace RoslynTools.Core;

/// <summary>
///     Invocation-scoped authority for the trusted-local preview. Ordinary typed requests
///     cannot set this value; only a trusted host surface may establish the scope before
///     application code reaches the workspace loader.
/// </summary>
internal static class RepositoryTrustContext
{
    private static readonly AsyncLocal<bool?> ExplicitTrust = new();

    internal static bool IsExplicitlyTrusted => ExplicitTrust.Value == true;

    internal static IDisposable UseExplicitTrust()
    {
        var previous = ExplicitTrust.Value;
        ExplicitTrust.Value = true;
        return new Scope(() => ExplicitTrust.Value = previous);
    }

    private sealed class Scope(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        void IDisposable.Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

/// <summary>
///     Signals that a semantic workspace load was attempted without invocation-scoped
///     repository trust. The trusted CLI host may grant that authority; request payloads cannot.
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
        + "Review the repository, then rerun the CLI command with --trust-repository. MCP requests cannot grant trust.")
    {
        SolutionPath = solutionPath
    };
}
