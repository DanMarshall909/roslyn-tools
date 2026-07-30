using RoslynTools.Feedback;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class FeedbackStoreTests
{
    [Fact]
    public void append_allocates_unique_id_when_caller_has_stale_existing_list()
    {
        using var workspace = new FeedbackWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            var first = new FeedbackEntry
            {
                Id = FeedbackStore.NextId([]),
                Type = "user-feedback",
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Description = "first"
            };
            var second = new FeedbackEntry
            {
                Id = FeedbackStore.NextId([]),
                Type = "user-feedback",
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Description = "second"
            };

            FeedbackStore.Append(first);
            FeedbackStore.Append(second);

            var all = FeedbackStore.LoadAll();
            all.Select(e => e.Id).ToArray().ShouldBe(["FB-001", "FB-002"]);
            second.Id.ShouldBe("FB-002");
        }
    }

    [Fact]
    public void mark_processed_persists_resolution_and_removes_entry_from_pending_work()
    {
        using var workspace = new FeedbackWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            FeedbackStore.Append(new FeedbackEntry
            {
                Type = "exception",
                Description = "fixed defect"
            });

            var processed = FeedbackStore.MarkProcessed("FB-001", "Covered by regression test.");

            processed.Id.ShouldBe("FB-001");
            processed.Status.ShouldBe("processed");
            processed.Resolution.ShouldBe("Covered by regression test.");
            processed.ProcessedAt.ShouldNotBeNullOrWhiteSpace();
            FeedbackStore.ListByStatus("pending").ShouldBeEmpty();
            FeedbackStore.ListByStatus("processed").Single().Id.ShouldBe("FB-001");
        }
    }

    [Fact]
    public void mark_processed_rejects_an_unknown_feedback_id()
    {
        using var workspace = new FeedbackWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            var exception = Should.Throw<ArgumentException>(
                () => FeedbackStore.MarkProcessed("FB-999", "not present"));

            exception.Message.ShouldContain("FB-999", Case.Sensitive);
        }
    }

    private sealed class FeedbackWorkspace : IDisposable
    {
        private readonly string _previousDirectory;

        public FeedbackWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), $"rt-feedback-{Guid.NewGuid():N}");
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
