using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Feedback;
using Shouldly;

namespace RoslynTools.Tests.Cli;

/// <summary>
///     US-076 regression: exceptions that TryHandleUserErrorAsync already
///     translates to a clean stderr message + exit 1 must NOT be captured to
///     feedback.json. Only genuinely unmapped failures (the "return false"
///     branch) are signal worth a human triage entry.
/// </summary>
public sealed class HandledUserErrorsDoNotPolluteFeedbackTests
{
    [Fact]
    public async Task missing_solution_file_exits_1_without_appending_feedback()
    {
        using var workspace = new FeedbackWorkspace();
        using (workspace.UseCurrentDirectory())
        {
            var sc = new ServiceCollection();
            RoslynTools.Bootstrap.RegisterServices(sc);
            using var services = sc.BuildServiceProvider();

            var exitCode = await App.RunAsync(
                ["inspect", "describe", "Foo", "--solution", "no-such-file.slnx"],
                services);

            exitCode.ShouldBe(1);
            FeedbackStore.LoadAll().ShouldBeEmpty(
                "FileNotFoundException is translated to a clean exit-1 by TryHandleUserErrorAsync; "
                + "capturing it adds noise without adding signal.");
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
