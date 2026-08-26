namespace RoslynTools.Daemon;

/// <summary>Applies a fresh idle deadline while the daemon waits for each request.</summary>
internal sealed class DaemonIdleLifetime
{
    private readonly TimeSpan _idleTimeout;

    internal DaemonIdleLifetime(TimeSpan idleTimeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(idleTimeout, TimeSpan.Zero);

        _idleTimeout = idleTimeout;
    }

    internal async Task<bool> WaitForActivityAsync(
        Func<CancellationToken, Task> waitForActivity,
        CancellationToken shutdownToken)
    {
        using var idleDeadline = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
        idleDeadline.CancelAfter(_idleTimeout);

        try
        {
            await waitForActivity(idleDeadline.Token);
            return true;
        }
        catch (OperationCanceledException) when (
            idleDeadline.IsCancellationRequested
            && !shutdownToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
