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
