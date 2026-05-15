using RoslynTools.Feedback;

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
            Assert.Equal(["FB-001", "FB-002"], all.Select(e => e.Id).ToArray());
            Assert.Equal("FB-002", second.Id);
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
