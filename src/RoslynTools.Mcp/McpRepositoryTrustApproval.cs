using RoslynTools.Services;

namespace RoslynTools.Mcp;

/// <summary>
///     Binds repository approval to the originating MCP request. Ordinary tool arguments
///     cannot supply this callback or approve a root outside a live client interaction.
/// </summary>
internal sealed class McpRepositoryTrustApproval : IRepositoryTrustApproval
{
    private readonly AsyncLocal<RequestApproval?> _approve = new();

    internal McpRepositoryTrustApproval(TimeSpan? timeout = null)
    {
        Timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (Timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    internal TimeSpan Timeout { get; }

    public Task<bool> ApproveAsync(string canonicalRoot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _approve.Value?.InvokeAsync(canonicalRoot, cancellationToken) ?? Task.FromResult(false);
    }

    internal IDisposable EnterRequestScope(Func<string, CancellationToken, Task<bool>> approve)
    {
        var previous = _approve.Value;
        var current = new RequestApproval(approve);
        _approve.Value = current;
        return new Scope(() =>
        {
            current.Revoke();
            _approve.Value = previous;
        });
    }

    private sealed class RequestApproval(Func<string, CancellationToken, Task<bool>> approve)
    {
        private int _active = 1;

        internal Task<bool> InvokeAsync(string root, CancellationToken cancellationToken) =>
            Volatile.Read(ref _active) == 1 ? approve(root, cancellationToken) : Task.FromResult(false);

        internal void Revoke() => Interlocked.Exchange(ref _active, 0);
    }

    private sealed class Scope(Action restore) : IDisposable
    {
        private Action? _restore = restore;

        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
