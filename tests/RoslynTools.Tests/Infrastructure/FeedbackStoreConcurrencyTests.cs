using RoslynTools.Feedback;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class FeedbackStoreConcurrencyTests
{
    [Fact]
    public async Task parallel_feedback_add_calls_produce_distinct_ids_and_persist_every_entry()
    {
        const int parallelCallCount = 16;

        using var workspace = new ConcurrencyWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            // Each task mirrors the `rt feedback add` handler: build an entry with an
            // initial (stale) id and let FeedbackStore.Append allocate a unique id
            // under lock. This is the same code path FeedbackCommand.BuildAdd drives.
            var ids = await Task.WhenAll(Enumerable.Range(0, parallelCallCount)
                .Select(i => Task.Run(() => FeedbackStore.Append(new FeedbackEntry
                {
                    Id = string.Empty,
                    Type = "user-feedback",
                    Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                    Description = $"parallel add #{i}"
                }))));

            ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(parallelCallCount);

            var all = FeedbackStore.LoadAll();
            all.Count.ShouldBe(parallelCallCount);

            var expected = Enumerable.Range(1, parallelCallCount)
                .Select(n => $"FB-{n:D3}")
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();
            all.Select(e => e.Id).OrderBy(s => s, StringComparer.Ordinal).ToArray()
                .ShouldBe(expected);
        }
    }

    [Fact]
    public async Task parallel_report_calls_create_only_one_issue_for_the_same_entry()
    {
        using var workspace = new ConcurrencyWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            FeedbackStore.Append(new FeedbackEntry
            {
                Type = "user-feedback",
                Description = "report me once"
            });
            var createCount = 0;

            async Task<(int Number, string Url)> CreateIssue(
                FeedbackEntry _,
                CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref createCount);
                await Task.Delay(50, cancellationToken);
                return (72, "https://github.com/example/repository/issues/72");
            }

            var results = await Task.WhenAll(
                FeedbackStore.ReportOnceAsync("FB-001", CreateIssue),
                FeedbackStore.ReportOnceAsync("FB-001", CreateIssue));

            createCount.ShouldBe(1);
            results.Count(result => result.AlreadyReported).ShouldBe(1);
            results.Count(result => !result.AlreadyReported).ShouldBe(1);
            results.ShouldAllBe(result => result.Entry.GitHubIssueNumber == 72);
            FeedbackStore.TryGetById("FB-001")!.GitHubIssueNumber.ShouldBe(72);
        }
    }

    [Fact]
    public async Task report_waits_beyond_the_legacy_lock_timeout_for_an_in_flight_process()
    {
        using var workspace = new ConcurrencyWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            FeedbackStore.Append(new FeedbackEntry
            {
                Type = "user-feedback",
                Description = "wait for the active reporter"
            });
            using var existingProcessLock = new FileStream(
                workspace.StoreLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.None);

            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var report = Task.Run(async () =>
            {
                started.SetResult();
                return await FeedbackStore.ReportOnceAsync(
                    "FB-001",
                    (_, _) => Task.FromResult((
                        72,
                        "https://github.com/example/repository/issues/72")));
            });

            await started.Task;
            await Task.Delay(TimeSpan.FromSeconds(4));
            report.IsCompleted.ShouldBeFalse();

            existingProcessLock.Dispose();
            var result = await report.WaitAsync(TimeSpan.FromSeconds(2));
            result.AlreadyReported.ShouldBeFalse();
            result.Entry.GitHubIssueNumber.ShouldBe(72);
        }
    }

    [Fact]
    public async Task report_lock_wait_honors_cancellation()
    {
        using var workspace = new ConcurrencyWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            FeedbackStore.Append(new FeedbackEntry
            {
                Type = "user-feedback",
                Description = "cancel the waiter"
            });
            using var existingProcessLock = new FileStream(
                workspace.StoreLockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1,
                FileOptions.None);
            using var cancellation = new CancellationTokenSource();

            var started = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var report = Task.Run(async () =>
            {
                started.SetResult();
                return await FeedbackStore.ReportOnceAsync(
                    "FB-001",
                    (_, _) => Task.FromResult((
                        72,
                        "https://github.com/example/repository/issues/72")),
                    cancellation.Token);
            });

            await started.Task;
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));
            await Should.ThrowAsync<OperationCanceledException>(() => report);
        }
    }

    private sealed class ConcurrencyWorkspace : IDisposable
    {
        private readonly string _previousDirectory;

        public ConcurrencyWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"rt-feedback-conc-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path.Combine(Root, "ralph"));
            _previousDirectory = Directory.GetCurrentDirectory();
        }

        private string Root { get; }

        public string StoreLockPath => Path.Combine(Root, "ralph", "feedback.json.lock");

        public IDisposable UseCurrentDirectory()
        {
            Directory.SetCurrentDirectory(Root);
            return new CurrentDirectoryScope(_previousDirectory);
        }

        public void Dispose()
        {
            Directory.SetCurrentDirectory(_previousDirectory);
            try
            {
                Directory.Delete(Root, true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }

        private sealed class CurrentDirectoryScope(string previousDirectory) : IDisposable
        {
            public void Dispose()
            {
                Directory.SetCurrentDirectory(previousDirectory);
            }
        }
    }
}
