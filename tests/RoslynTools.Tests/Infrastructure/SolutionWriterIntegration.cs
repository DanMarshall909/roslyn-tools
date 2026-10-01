using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class SolutionWriterIntegration
{
    [Fact]
    public async Task changed_documents_preview_without_writing()
    {
        const string before = "class Original { }";
        const string after = "class Updated { }";
        using var project = new AdhocProjectBuilder();
        project.AddSource("Changed.cs", before);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var path = document.FilePath!;
            var originalBytes = File.ReadAllBytes(path);
            var updated = solution.WithDocumentText(document.Id, SourceText.From(after, Encoding.Unicode));
            var output = new RecordingSolutionWriteOutput();

            await Core.SolutionWriter.PreviewChangesAsync(solution, updated, output);

            File.ReadAllBytes(path).ShouldBe(originalBytes);
            output.DocumentDiffs.ShouldBe([(before, after, path)]);
            output.Results.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task prepared_writer_applies_changed_documents_with_explicit_encoding()
    {
        const string before = "class Original { }";
        const string after = "class Updated { }";
        using var project = new AdhocProjectBuilder();
        project.AddSource("Changed.cs", before);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var path = document.FilePath!;
            var updated = solution.WithDocumentText(document.Id, SourceText.From(after, Encoding.Unicode));

            await project.ApplySolutionAsync(solution, updated);

            File.ReadAllBytes(path).ShouldBe(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(after)));
        }
    }

    [Fact]
    public async Task prepared_writer_writes_added_documents_to_new_files()
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

            await project.ApplySolutionAsync(sln, newDoc);

            Assert.True(File.Exists(newFilePath), $"Expected new file at {newFilePath}");
            var content = await File.ReadAllTextAsync(newFilePath);
            content.ShouldContain("class NewClass", Case.Sensitive);
        }
    }

    [Fact]
    public async Task preview_rejects_pathless_document_before_reporting_other_changes()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", "class Calculator { }");
        var (workspace, solution) = project.Build();
        var output = new RecordingSolutionWriteOutput();

        using (workspace)
        {
            var projectId = solution.Projects.Single().Id;
            var existingDocument = solution.Projects.Single().Documents.Single();
            var changed = solution.WithDocumentText(existingDocument.Id,
                SourceText.From("class Updated { }"));
            var withoutPath = changed.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                "NoPath.cs",
                loader: new NewClassTextLoader()));

            var exception = await Should.ThrowAsync<ArgumentException>(() =>
                Core.SolutionWriter.PreviewChangesAsync(solution, withoutPath, output));

            exception.Message.ShouldContain("no file path");
            output.DocumentDiffs.ShouldBeEmpty();
            output.Warnings.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
            (await project.ReadSourceAsync("Calculator.cs")).ShouldBe("class Calculator { }");
        }
    }

    [Fact]
    public async Task preview_rejects_changed_document_that_loses_its_path_before_reporting_other_changes()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("First.cs", "class First { }");
        project.AddSource("Second.cs", "class Second { }");
        var (workspace, solution) = project.Build();
        var output = new RecordingSolutionWriteOutput();
        using (workspace)
        {
            var documents = solution.Projects.Single().Documents.ToArray();
            var changed = solution
                .WithDocumentText(documents[0].Id, SourceText.From("class FirstUpdated { }"))
                .WithDocumentFilePath(documents[1].Id, null)
                .WithDocumentText(documents[1].Id, SourceText.From("class SecondUpdated { }"));

            var exception = await Should.ThrowAsync<ArgumentException>(() =>
                Core.SolutionWriter.PreviewChangesAsync(solution, changed, output));

            exception.Message.ShouldContain("no file path");
            output.DocumentDiffs.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
            (await project.ReadSourceAsync("First.cs")).ShouldBe("class First { }");
            (await project.ReadSourceAsync("Second.cs")).ShouldBe("class Second { }");
        }
    }

    [Fact]
    public async Task preview_rejects_conflicting_paths_before_reporting_other_changes()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("First.cs", "class First { }");
        var (workspace, solution) = project.Build();
        var output = new RecordingSolutionWriteOutput();
        using (workspace)
        {
            var existing = solution.Projects.Single().Documents.Single();
            var changed = solution.WithDocumentText(existing.Id,
                SourceText.From("class FirstUpdated { }"));
            var conflicting = changed.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(existing.Project.Id),
                "Duplicate.cs",
                filePath: existing.FilePath,
                loader: new NewClassTextLoader()));

            var exception = await Should.ThrowAsync<ArgumentException>(() =>
                Core.SolutionWriter.PreviewChangesAsync(solution, conflicting, output));

            exception.Message.ShouldContain("affected more than once");
            output.DocumentDiffs.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
            (await project.ReadSourceAsync("First.cs")).ShouldBe("class First { }");
        }
    }

    [Fact]
    public async Task prepared_writer_rejects_added_document_without_a_path()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", "class Calculator { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var projectId = solution.Projects.Single().Id;
            var withoutPath = solution.AddDocument(DocumentInfo.Create(
                DocumentId.CreateNewId(projectId),
                "NoPath.cs",
                loader: new NewClassTextLoader()));

            await Should.ThrowAsync<ArgumentException>(() =>
                project.ApplySolutionAsync(solution, withoutPath));

            project.SourceExists("NoPath.cs").ShouldBeFalse();
        }
    }

    [Fact]
    public async Task prepared_writer_rejects_retargeted_document_without_writing_either_path()
    {
        const string before = "class Original { }";
        using var project = new AdhocProjectBuilder();
        project.AddSource("Original.cs", before);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var destination = Path.Combine(project.ProjectDir, "Moved.cs");
            var changed = solution.WithDocumentFilePath(document.Id, destination)
                .WithDocumentText(document.Id, SourceText.From("class Moved { }"));

            await Should.ThrowAsync<FileNotFoundException>(() =>
                project.ApplySolutionAsync(solution, changed));

            File.ReadAllText(document.FilePath!).ShouldBe(before);
            File.Exists(destination).ShouldBeFalse();
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
                await Core.SolutionWriter.PreviewChangesAsync(
                    sln, newDoc, new ConsoleSolutionWriteOutput());
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

        internal List<(string Before, string After, string Path)> DocumentDiffs { get; } = [];

        public void Result(string message) => Results.Add(message);

        public void Diff(string message)
        {
        }

        public void DocumentDiff(string oldText, string newText, string filePath)
        {
            DocumentDiffs.Add((oldText, newText, filePath));
            Adapters.SolutionDiffRenderer.PrintDiff(oldText, newText, filePath, this);
        }

        public void Warning(string message) => Warnings.Add(message);
    }

    private sealed class RecordingWarningLogger : IRtLogger
    {
        internal List<(RtLogLevel Level, string Message)> Entries { get; } = [];

        public void Log(RtLogLevel level, string message) => Entries.Add((level, message));
    }
}
