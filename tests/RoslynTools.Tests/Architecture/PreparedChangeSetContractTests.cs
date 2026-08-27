using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

public sealed class PreparedChangeSetContractTests
{
    [Fact]
    public void derives_changed_added_and_removed_document_paths_from_the_solution_delta()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var changedId = DocumentId.CreateNewId(projectId);
        var removedId = DocumentId.CreateNewId(projectId);
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "PreparedChanges",
                "PreparedChanges",
                LanguageNames.CSharp))
            .AddDocument(changedId, "Changed.cs", SourceText.From("class Before;"), filePath: "src/Changed.cs")
            .AddDocument(removedId, "Removed.cs", SourceText.From("class Removed;"), filePath: "src/Removed.cs");
        var addedId = DocumentId.CreateNewId(projectId);
        var changed = original
            .WithDocumentText(changedId, SourceText.From("class After;"))
            .RemoveDocument(removedId)
            .AddDocument(addedId, "Added.cs", SourceText.From("class Added;"), filePath: "src/Added.cs");

        var changeSet = new PreparedChangeSet(original, changed);

        changeSet.AffectedPaths.ShouldBe([
            "src/Added.cs",
            "src/Changed.cs",
            "src/Removed.cs"
        ]);
    }

    [Fact]
    public void rejects_a_changed_roslyn_document_without_a_file_path()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "PreparedChanges",
                "PreparedChanges",
                LanguageNames.CSharp))
            .AddDocument(documentId, "Changed.cs", SourceText.From("class Before;"));
        var changed = original.WithDocumentText(documentId, SourceText.From("class After;"));

        var exception = Should.Throw<ArgumentException>(() => new PreparedChangeSet(original, changed));

        exception.Message.ShouldContain("no file path");
    }

    [Fact]
    public void models_roslyn_and_filesystem_changes_and_reports_every_affected_path()
    {
        var changeSet = new PreparedChangeSet(
            originalSolution: null,
            changedSolution: null,
            newFiles: [new PreparedFileWrite("src/New.cs", "class New;")],
            deletedFiles: ["src/Old.cs"],
            movedFiles: [new PreparedFileMove("src/Before.cs", "src/After.cs")],
            projectFileEdits: [new PreparedFileWrite("App.csproj", "<Project />")],
            textEdits: [new PreparedFileWrite("README.md", "updated")]);

        changeSet.AffectedPaths.ShouldBe([
            "App.csproj",
            "README.md",
            "src/After.cs",
            "src/Before.cs",
            "src/New.cs",
            "src/Old.cs"
        ]);
    }

    [Fact]
    public void rejects_unpaired_solutions_duplicate_normalized_paths_and_conflicting_operations()
    {
        using var workspace = new AdhocWorkspace();

        Should.Throw<ArgumentException>(() => new PreparedChangeSet(
            workspace.CurrentSolution,
            changedSolution: null));

        Should.Throw<ArgumentException>(() => new PreparedChangeSet(
            newFiles:
            [
                new PreparedFileWrite("src/A.cs", "one"),
                new PreparedFileWrite("src/./A.cs", "two")
            ]));

        Should.Throw<ArgumentException>(() => new PreparedChangeSet(
            newFiles: [new PreparedFileWrite("src/A.cs", "one")],
            deletedFiles: ["src/A.cs"]));
    }

    [Fact]
    public void move_sources_and_destinations_cannot_conflict_with_writes_or_deletes()
    {
        Should.Throw<ArgumentException>(() => new PreparedChangeSet(
            movedFiles: [new PreparedFileMove("src/Before.cs", "src/After.cs")],
            newFiles: [new PreparedFileWrite("src/Before.cs", "replacement")]));

        Should.Throw<ArgumentException>(() => new PreparedChangeSet(
            movedFiles: [new PreparedFileMove("src/Before.cs", "src/After.cs")],
            deletedFiles: ["src/After.cs"]));
    }

    [Fact]
    public void a_solution_edit_can_be_followed_by_a_move_of_the_changed_document()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId,
                VersionStamp.Create(),
                "PreparedChanges",
                "PreparedChanges",
                LanguageNames.CSharp))
            .AddDocument(
                documentId,
                "Before.cs",
                SourceText.From("class Before;"),
                filePath: "src/Before.cs");
        var changed = original.WithDocumentText(documentId, SourceText.From("class After;"));

        var changeSet = new PreparedChangeSet(
            original,
            changed,
            movedFiles: [new PreparedFileMove("src/Before.cs", "src/After.cs")]);

        changeSet.AffectedPaths.ShouldBe(["src/After.cs", "src/Before.cs"]);
    }

    [Fact]
    public async Task preview_returns_exact_paths_without_applying_and_apply_writes_exactly_once()
    {
        var changeSet = new PreparedChangeSet(
            newFiles: [new PreparedFileWrite("src/New.cs", "class New;")],
            textEdits: [new PreparedFileWrite("README.md", "updated")]);
        var writer = new RecordingPreparedChangeSetWriter();
        var executor = new PreparedChangeSetExecutor(writer);

        var preview = executor.Preview("value", changeSet);

        preview.Status.ShouldBe(MutationExecutionStatus.DryRun);
        preview.AffectedPaths.ShouldBe(["README.md", "src/New.cs"]);
        writer.ApplyCount.ShouldBe(0);

        var applied = await executor.ApplyAsync("value", changeSet);

        applied.Status.ShouldBe(MutationExecutionStatus.Applied);
        applied.AffectedPaths.ShouldBe(preview.AffectedPaths);
        writer.ApplyCount.ShouldBe(1);
        writer.LastChangeSet.ShouldBeSameAs(changeSet);
    }

    [Fact]
    public async Task empty_apply_is_rejected_and_cancellation_before_apply_never_writes()
    {
        var writer = new RecordingPreparedChangeSetWriter();
        var executor = new PreparedChangeSetExecutor(writer);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            executor.ApplyAsync("value", new PreparedChangeSet()));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var changeSet = new PreparedChangeSet(
            deletedFiles: ["src/Old.cs"]);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            executor.ApplyAsync("value", changeSet, cancellationToken: cancellation.Token));

        writer.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task cancellation_during_apply_is_propagated_and_the_prepared_set_cannot_be_retried()
    {
        using var cancellation = new CancellationTokenSource();
        var writer = new CancellingPreparedChangeSetWriter(cancellation);
        var executor = new PreparedChangeSetExecutor(writer);
        var changeSet = new PreparedChangeSet(
            textEdits: [new PreparedFileWrite("README.md", "updated")]);

        await Should.ThrowAsync<OperationCanceledException>(() =>
            executor.ApplyAsync("value", changeSet, cancellationToken: cancellation.Token));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            executor.ApplyAsync("value", changeSet));
        writer.ApplyCount.ShouldBe(1);
    }

    [Fact]
    public async Task executor_applies_distinct_sets_once_each_but_rejects_the_same_set_twice()
    {
        var writer = new RecordingPreparedChangeSetWriter();
        var executor = new PreparedChangeSetExecutor(writer);
        var first = new PreparedChangeSet(
            textEdits: [new PreparedFileWrite("README.md", "first")]);
        var second = new PreparedChangeSet(
            textEdits: [new PreparedFileWrite("CONTRIBUTING.md", "second")]);

        await executor.ApplyAsync("first", first);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            executor.ApplyAsync("first-again", first));
        await executor.ApplyAsync("second", second);

        writer.ApplyCount.ShouldBe(2);
        writer.LastChangeSet.ShouldBeSameAs(second);
    }

    [Fact]
    public async Task full_writer_applies_text_and_move_effects_after_preflight()
    {
        var directory = Directory.CreateTempSubdirectory("rt-prepared-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "Before.cs");
            var destinationPath = Path.Combine(directory.FullName, "After.cs");
            var textPath = Path.Combine(directory.FullName, "notes.txt");
            await File.WriteAllTextAsync(sourcePath, "class Before;");
            await File.WriteAllTextAsync(textPath, "before");
            var writer = new PreparedChangeSetWriter(new CapturingSolutionChangeWriter());
            var changeSet = new PreparedChangeSet(
                movedFiles: [new PreparedFileMove(sourcePath, destinationPath)],
                textEdits: [new PreparedFileWrite(textPath, "after")]);

            await writer.ApplyAsync(changeSet);

            File.Exists(sourcePath).ShouldBeFalse();
            (await File.ReadAllTextAsync(destinationPath)).ShouldBe("class Before;");
            (await File.ReadAllTextAsync(textPath)).ShouldBe("after");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_a_move_conflict_before_any_write()
    {
        var directory = Directory.CreateTempSubdirectory("rt-prepared-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "Before.cs");
            var destinationPath = Path.Combine(directory.FullName, "After.cs");
            var textPath = Path.Combine(directory.FullName, "notes.txt");
            await File.WriteAllTextAsync(sourcePath, "class Before;");
            await File.WriteAllTextAsync(destinationPath, "class After;");
            await File.WriteAllTextAsync(textPath, "before");
            var writer = new PreparedChangeSetWriter(new CapturingSolutionChangeWriter());
            var changeSet = new PreparedChangeSet(
                movedFiles: [new PreparedFileMove(sourcePath, destinationPath)],
                textEdits: [new PreparedFileWrite(textPath, "after")]);

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            (await File.ReadAllTextAsync(textPath)).ShouldBe("before");
            File.Exists(sourcePath).ShouldBeTrue();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class RecordingPreparedChangeSetWriter : IPreparedChangeSetWriter
    {
        internal int ApplyCount { get; private set; }
        internal PreparedChangeSet? LastChangeSet { get; private set; }

        public Task ApplyAsync(
            PreparedChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ApplyCount++;
            LastChangeSet = changeSet;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingPreparedChangeSetWriter(CancellationTokenSource cancellation)
        : IPreparedChangeSetWriter
    {
        internal int ApplyCount { get; private set; }

        public Task ApplyAsync(
            PreparedChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }
}
