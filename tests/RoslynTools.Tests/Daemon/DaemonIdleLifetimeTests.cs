using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonIdleLifetimeTests
{
    [Fact]
    public void Constructor_rejects_a_zero_idle_timeout()
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => new DaemonIdleLifetime(TimeSpan.Zero));
    }

    [Fact]
    public async Task WaitForActivity_returns_false_after_the_configured_idle_timeout()
    {
        var lifetime = new DaemonIdleLifetime(TimeSpan.FromMilliseconds(25));

        var activity = await lifetime.WaitForActivityAsync(
            cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
            CancellationToken.None);

        activity.ShouldBeFalse();
    }

    [Fact]
    public async Task Each_wait_receives_a_fresh_idle_period_after_activity()
    {
        var lifetime = new DaemonIdleLifetime(TimeSpan.FromMilliseconds(500));

        (await lifetime.WaitForActivityAsync(
                _ => Task.CompletedTask,
                CancellationToken.None))
            .ShouldBeTrue();

        await Task.Delay(300);

        (await lifetime.WaitForActivityAsync(
                async cancellationToken => await Task.Delay(300, cancellationToken),
                CancellationToken.None))
            .ShouldBeTrue();
    }

    [Fact]
    public async Task Shutdown_cancellation_is_not_reported_as_idle_expiry()
    {
        var lifetime = new DaemonIdleLifetime(TimeSpan.FromMinutes(1));
        using var shutdown = new CancellationTokenSource();
        shutdown.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(
            () => lifetime.WaitForActivityAsync(
                cancellationToken => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken),
                shutdown.Token));
    }
}
