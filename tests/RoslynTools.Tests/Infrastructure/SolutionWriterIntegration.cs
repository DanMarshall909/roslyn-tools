using System.Collections.Immutable;
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
    public async Task removed_document_preview_reports_deletion_without_writing()
    {
        const string before = "class Removed { }";
        using var project = new AdhocProjectBuilder();
        project.AddSource("Removed.cs", before);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var output = new RecordingSolutionWriteOutput();

            await Core.SolutionWriter.PreviewChangesAsync(
                solution, solution.RemoveDocument(document.Id), output);

            output.DiffLines.ShouldContain($"\n--- {document.FilePath} (deleted file)");
            output.DiffLines.ShouldContain($"-{before}");
            output.DiffLines.ShouldContain("\n[dry-run] 1 file(s) would change.");
            output.Results.ShouldBeEmpty();
            File.ReadAllText(document.FilePath!).ShouldBe(before);
        }
    }

    [Fact]
    public async Task preview_counts_changed_and_removed_documents()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Changed.cs", "class Original { }");
        project.AddSource("Removed.cs", "class Removed { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var documents = solution.Projects.Single().Documents.ToArray();
            var changed = solution
                .WithDocumentText(documents[0].Id, SourceText.From("class Updated { }"))
                .RemoveDocument(documents[1].Id);
            var output = new RecordingSolutionWriteOutput();

            await Core.SolutionWriter.PreviewChangesAsync(solution, changed, output);

            output.DocumentDiffs.Count.ShouldBe(1);
            output.DiffLines.ShouldContain($"\n--- {documents[1].FilePath} (deleted file)");
            output.DiffLines.ShouldContain("\n[dry-run] 2 file(s) would change.");
            File.ReadAllText(documents[0].FilePath!).ShouldBe("class Original { }");
            File.ReadAllText(documents[1].FilePath!).ShouldBe("class Removed { }");
        }
    }

    [Fact]
    public async Task preview_rejects_added_project_before_reporting_document_change()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Changed.cs", "class Original { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var changed = solution.WithDocumentText(document.Id, SourceText.From("class Updated { }"));
            var added = changed.AddProject(ProjectInfo.Create(
                ProjectId.CreateNewId(), VersionStamp.Create(), "Added", "Added", LanguageNames.CSharp));
            var output = new RecordingSolutionWriteOutput();

            var exception = await Should.ThrowAsync<NotSupportedException>(() =>
                Core.SolutionWriter.PreviewChangesAsync(solution, added, output));

            exception.Message.ShouldContain("does not support adding or removing projects");
            output.DiffLines.ShouldBeEmpty();
            output.DocumentDiffs.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task preview_rejects_removed_project_before_reporting_document_change()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Changed.cs", "class Original { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var extraProjectId = ProjectId.CreateNewId();
            var original = solution.AddProject(ProjectInfo.Create(
                extraProjectId, VersionStamp.Create(), "Removed", "Removed", LanguageNames.CSharp));
            var changed = original
                .WithDocumentText(document.Id, SourceText.From("class Updated { }"))
                .RemoveProject(extraProjectId);
            var output = new RecordingSolutionWriteOutput();

            var exception = await Should.ThrowAsync<NotSupportedException>(() =>
                Core.SolutionWriter.PreviewChangesAsync(original, changed, output));

            exception.Message.ShouldContain("does not support adding or removing projects");
            output.DiffLines.ShouldBeEmpty();
            output.DocumentDiffs.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task unsupported_additional_document_prevents_partial_preview_or_apply()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Changed.cs", "class Original { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var document = solution.Projects.Single().Documents.Single();
            var additionalPath = Path.Combine(project.ProjectDir, "settings.json");
            var changed = solution
                .WithDocumentText(document.Id, SourceText.From("class Updated { }"))
                .AddAdditionalDocument(
                    DocumentId.CreateNewId(document.Project.Id), "settings.json", SourceText.From("{}"),
                    filePath: additionalPath);
            var output = new RecordingSolutionWriteOutput();
            var writer = new Core.SolutionChangeWriter(output);

            await Should.ThrowAsync<NotSupportedException>(() =>
                writer.ApplyChangesAsync(solution, changed, dryRun: true));
            await Should.ThrowAsync<NotSupportedException>(() =>
                writer.ApplyChangesAsync(solution, changed, dryRun: false));

            output.DiffLines.ShouldBeEmpty();
            output.DocumentDiffs.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
            File.ReadAllText(document.FilePath!).ShouldBe("class Original { }");
            File.Exists(additionalPath).ShouldBeFalse();
        }
    }

    [Fact]
    public async Task changed_document_order_prevents_partial_preview_or_apply()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("First.cs", "class First { }");
        project.AddSource("Second.cs", "class Second { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var projectId = solution.Projects.Single().Id;
            var documents = solution.Projects.Single().Documents.ToArray();
            var changed = solution
                .WithDocumentText(documents[0].Id, SourceText.From("class Updated { }"))
                .WithProjectDocumentsOrder(projectId,
                    ImmutableList.Create(documents[1].Id, documents[0].Id));
            var output = new RecordingSolutionWriteOutput();
            var writer = new Core.SolutionChangeWriter(output);

            await Should.ThrowAsync<NotSupportedException>(() =>
                writer.ApplyChangesAsync(solution, changed, dryRun: true));
            await Should.ThrowAsync<NotSupportedException>(() =>
                writer.ApplyChangesAsync(solution, changed, dryRun: false));

            output.DiffLines.ShouldBeEmpty();
            output.DocumentDiffs.ShouldBeEmpty();
            output.Results.ShouldBeEmpty();
            File.ReadAllText(documents[0].FilePath!).ShouldBe("class First { }");
            File.ReadAllText(documents[1].FilePath!).ShouldBe("class Second { }");
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

            await Should.ThrowAsync<NotSupportedException>(() =>
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

        internal List<string> DiffLines { get; } = [];

        internal List<string> Warnings { get; } = [];

        internal List<(string Before, string After, string Path)> DocumentDiffs { get; } = [];

        public void Result(string message) => Results.Add(message);

        public void Diff(string message)
        {
            DiffLines.Add(message);
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
