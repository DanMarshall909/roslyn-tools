using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SolutionWriterIntegration
{
    [Fact]
    public async Task writes_added_documents_to_new_files()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace TestProject;
                                           public class Calculator
                                           {
                                               public int Add(int a, int b) => a + b;
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        var output = new RecordingSolutionWriteOutput();
        using (workspace)
        {
            var roslynProject = sln.Projects.First();
            var newFilePath = Path.Combine(project.ProjectDir, "NewClass.cs");
            var newDoc = sln.AddDocument(
                DocumentInfo.Create(
                    DocumentId.CreateNewId(roslynProject.Id),
                    "NewClass.cs",
                    filePath: newFilePath,
                    loader: new NewClassTextLoader()));

            await Core.SolutionWriter.ApplyChangesAsync(sln, newDoc, false, output);

            Assert.True(File.Exists(newFilePath), $"Expected new file at {newFilePath}");
            var content = await File.ReadAllTextAsync(newFilePath);
            content.ShouldContain("class NewClass", Case.Sensitive);
            output.Results.ShouldContain($"Created: {newFilePath}");
            output.Results.ShouldContain("\nDone. 1 file(s) updated.");
        }
    }

    [Fact]
    public async Task added_document_without_a_path_is_skipped_with_a_warning()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", "class Calculator { }");
        var (workspace, solution) = project.Build();
        var output = new RecordingSolutionWriteOutput();

        using (workspace)
        {
            var projectId = solution.Projects.Single().Id;
            var withoutPath = solution.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                "NoPath.cs",
                loader: new NewClassTextLoader()));

            await Core.SolutionWriter.ApplyChangesAsync(solution, withoutPath, dryRun: false, output);

            output.Warnings.ShouldContain("Added document 'NoPath.cs' has no file path — skipped.");
            output.Results.ShouldContain("No changes.");
        }
    }

    [Fact]
    public void host_sink_preserves_warning_level_and_message()
    {
        var logger = new RecordingWarningLogger();

        using (RtLog.UseLogger(logger))
            new ConsoleSolutionWriteOutput().Warning("Added document '{Draft}' has no file path — skipped.");

        logger.Entries.ShouldHaveSingleItem();
        logger.Entries[0].Level.ShouldBe(RtLogLevel.Warning);
        logger.Entries[0].Message.ShouldBe("Added document '{Draft}' has no file path — skipped.");
    }

    [Fact]
    public async Task does_not_write_added_documents()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace TestProject;
                                           public class Calculator
                                           {
                                               public int Add(int a, int b) => a + b;
                                           }
                                           """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var roslynProject = sln.Projects.First();
            var newFilePath = Path.Combine(project.ProjectDir, "NewClass.cs");
            var newDoc = sln.AddDocument(
                DocumentInfo.Create(
                    DocumentId.CreateNewId(roslynProject.Id),
                    "NewClass.cs",
                    filePath: newFilePath,
                    loader: new NewClassTextLoader()));

            // Capture output
            var originalOut = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try
            {
                await Core.SolutionWriter.ApplyChangesAsync(
                    sln, newDoc, true, new ConsoleSolutionWriteOutput());
            }
            finally
            {
                Console.SetOut(originalOut);
            }

            Assert.False(File.Exists(newFilePath), "File should NOT be created in dry-run mode");
            var output = sw.ToString();
            output.ShouldContain("NewClass", Case.Sensitive);
        }
    }

    /// <summary>
    ///     Simple TextLoader that provides the content for the new document.
    /// </summary>
    private sealed class NewClassTextLoader : TextLoader
    {
        public override Task<TextAndVersion> LoadTextAndVersionAsync(
            LoadTextOptions options, CancellationToken cancellationToken)
        {
            var text = SourceText.From("""
                                       namespace TestProject;

                                       public class NewClass
                                       {
                                           public string Name => "New";
                                       }
                                       """);
            return Task.FromResult(TextAndVersion.Create(text, VersionStamp.Create()));
        }
    }

    private sealed class RecordingSolutionWriteOutput : ISolutionWriteOutput
    {
        internal List<string> Results { get; } = [];

        internal List<string> Warnings { get; } = [];

        public void Result(string message) => Results.Add(message);

        public void Diff(string message)
        {
        }

        public void Warning(string message) => Warnings.Add(message);
    }

    private sealed class RecordingWarningLogger : IRtLogger
    {
        internal List<(RtLogLevel Level, string Message)> Entries { get; } = [];

        public void Log(RtLogLevel level, string message) => Entries.Add((level, message));
    }
}
