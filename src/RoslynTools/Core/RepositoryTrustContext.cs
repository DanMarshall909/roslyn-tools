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

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }
}

internal sealed class RepositoryTrustRequiredException : InvalidOperationException
{
    public RepositoryTrustRequiredException()
    {
    }

    public RepositoryTrustRequiredException(string? message) : base(message)
    {
    }

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
