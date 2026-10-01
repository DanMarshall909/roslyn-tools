using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Formatting;
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
    public void rejects_non_source_document_deltas_before_affected_paths_are_derived()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(), "PreparedChanges", "PreparedChanges", LanguageNames.CSharp));

        var additionalId = DocumentId.CreateNewId(projectId);
        var analyzerConfigId = DocumentId.CreateNewId(projectId);
        var additional = original.AddAdditionalDocument(
            additionalId, "settings.json", SourceText.From("{}"), filePath: "settings.json");
        var analyzerConfig = original.AddAnalyzerConfigDocument(
            analyzerConfigId, ".editorconfig", SourceText.From("root = true"),
            filePath: ".editorconfig");

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, additional))
            .Message.ShouldContain("additional documents");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, analyzerConfig))
            .Message.ShouldContain("analyzer config documents");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(
                additional, additional.WithAdditionalDocumentText(additionalId, SourceText.From("{ \"x\": 1 }"))))
            .Message.ShouldContain("additional documents");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(
                analyzerConfig, analyzerConfig.RemoveAnalyzerConfigDocument(analyzerConfigId)))
            .Message.ShouldContain("analyzer config documents");
    }

    [Fact]
    public void rejects_project_reference_and_metadata_deltas()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var referencedProjectId = ProjectId.CreateNewId();
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "PreparedChanges", "PreparedChanges", LanguageNames.CSharp))
            .AddProject(ProjectInfo.Create(
                referencedProjectId, VersionStamp.Create(), "Referenced", "Referenced", LanguageNames.CSharp));
        var withReference = original.AddProjectReference(projectId, new ProjectReference(referencedProjectId));
        var withAssemblyName = original.WithProjectAssemblyName(projectId, "ChangedAssembly");

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, withReference))
            .Message.ShouldContain("project references");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, withAssemblyName))
            .Message.ShouldContain("project metadata");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(
                original, withReference,
                projectFileEdits: [new PreparedFileWrite("PreparedChanges.csproj", "<Project />")]))
            .Message.ShouldContain("project references");
    }

    [Fact]
    public void rejects_source_document_path_changes_that_writer_cannot_represent()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "PreparedChanges", "PreparedChanges", LanguageNames.CSharp))
            .AddDocument(documentId, "Before.cs", SourceText.From("class Before;"), filePath: "Before.cs");
        var changed = original.WithDocumentFilePath(documentId, "After.cs");

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, changed))
            .Message.ShouldContain("document metadata");
    }

    [Fact]
    public void rejects_source_document_order_changes_instead_of_reporting_no_changes()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var firstId = DocumentId.CreateNewId(projectId);
        var secondId = DocumentId.CreateNewId(projectId);
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "PreparedChanges", "PreparedChanges", LanguageNames.CSharp))
            .AddDocument(firstId, "First.cs", SourceText.From("class First;"), filePath: "First.cs")
            .AddDocument(secondId, "Second.cs", SourceText.From("class Second;"), filePath: "Second.cs");
        var reordered = original.WithProjectDocumentsOrder(
            projectId, ImmutableList.Create(secondId, firstId));

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, reordered))
            .Message.ShouldContain("document order");
    }

    [Fact]
    public void rejects_solution_analyzer_references_alone_or_with_a_source_edit()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var documentId = DocumentId.CreateNewId(projectId);
        var original = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "PreparedChanges", "PreparedChanges", LanguageNames.CSharp))
            .AddDocument(documentId, "Source.cs", SourceText.From("class Before;"), filePath: "Source.cs");
        var withAnalyzer = original.AddAnalyzerReference(new TestAnalyzerReference());
        var mixed = withAnalyzer.WithDocumentText(documentId, SourceText.From("class After;"));

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, withAnalyzer))
            .Message.ShouldContain("solution analyzer references");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, mixed))
            .Message.ShouldContain("solution analyzer references");
    }

    [Fact]
    public void rejects_solution_options_that_have_no_file_write_representation()
    {
        using var workspace = new AdhocWorkspace();
        var original = workspace.CurrentSolution;
        var changed = original.WithOptions(original.Options.WithChangedOption(
            FormattingOptions.UseTabs, LanguageNames.CSharp, true));

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, changed))
            .Message.ShouldContain("solution metadata");
    }

    [Fact]
    public void rejects_project_additions_and_removals_even_without_file_paths()
    {
        using var workspace = new AdhocWorkspace();
        var projectId = ProjectId.CreateNewId();
        var original = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
            projectId, VersionStamp.Create(), "PreparedChanges", "PreparedChanges", LanguageNames.CSharp));
        var added = original.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "Added", "Added", LanguageNames.CSharp));

        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, added))
            .Message.ShouldContain("adding or removing projects");
        Should.Throw<NotSupportedException>(() => new PreparedChangeSet(original, original.RemoveProject(projectId)))
            .Message.ShouldContain("adding or removing projects");
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
    public void rejects_duplicate_move_destinations_before_application()
    {
        Should.Throw<ArgumentException>(() => new PreparedChangeSet(
            movedFiles:
            [
                new PreparedFileMove("src/First.cs", "src/Target.cs"),
                new PreparedFileMove("src/Second.cs", "src/./Target.cs")
            ]));
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
        var directory = CreatePhysicalTempDirectory("rt-prepared-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "Before.cs");
            var destinationPath = Path.Combine(directory.FullName, "After.cs");
            var textPath = Path.Combine(directory.FullName, "notes.txt");
            await File.WriteAllTextAsync(sourcePath, "class Before;");
            await File.WriteAllTextAsync(textPath, "before");
            var writer = new PreparedChangeSetWriter(
                new PhysicalPreparedFileSystem(), directory.FullName);
            var changeSet = new PreparedChangeSet(
                movedFiles: [new PreparedFileMove(sourcePath, destinationPath)],
                textEdits: [new PreparedFileWrite(textPath, "after")]);

            await writer.ApplyAsync(changeSet);

            File.Exists(sourcePath).ShouldBeFalse();
            (await File.ReadAllTextAsync(destinationPath)).ShouldBe("class Before;");
            (await File.ReadAllTextAsync(textPath)).ShouldBe("after");
            (await File.ReadAllBytesAsync(textPath)).ShouldBe(Encoding.UTF8.GetBytes("after"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_preserves_encoding_of_a_changed_roslyn_document()
    {
        var directory = CreatePhysicalTempDirectory("rt-encoding-");
        try
        {
            const string before = "class Before { }";
            const string after = "class After { }";
            var path = Path.Combine(directory.FullName, "Changed.cs");
            await File.WriteAllTextAsync(path, before, Encoding.Unicode);
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var original = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(),
                    "Encoded", "Encoded", LanguageNames.CSharp))
                .AddDocument(documentId, "Changed.cs", SourceText.From(before, Encoding.Unicode),
                    filePath: path);
            var changed = original.WithDocumentText(documentId,
                SourceText.From(after, Encoding.Unicode));
            var writer = new PreparedChangeSetWriter(
                new PhysicalPreparedFileSystem(), directory.FullName);

            await writer.ApplyAsync(new PreparedChangeSet(original, changed));

            (await File.ReadAllBytesAsync(path)).ShouldBe(
                Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(after)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_preserves_encoding_of_an_added_roslyn_document()
    {
        var directory = CreatePhysicalTempDirectory("rt-encoding-");
        try
        {
            const string content = "class Added { }";
            var path = Path.Combine(directory.FullName, "Added.cs");
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var original = workspace.CurrentSolution.AddProject(ProjectInfo.Create(
                projectId, VersionStamp.Create(), "Encoded", "Encoded", LanguageNames.CSharp));
            var changed = original.AddDocument(documentId, "Added.cs",
                SourceText.From(content, Encoding.BigEndianUnicode), filePath: path);
            var writer = new PreparedChangeSetWriter(
                new PhysicalPreparedFileSystem(), directory.FullName);

            await writer.ApplyAsync(new PreparedChangeSet(original, changed));

            (await File.ReadAllBytesAsync(path)).ShouldBe(
                Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(content)));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_an_out_of_root_write_before_staging()
    {
        var parent = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var repositoryRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo")).FullName;
            var outsideRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo-sibling")).FullName;
            var outsidePath = Path.Combine(outsideRoot, "Escape.cs");
            using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
            var changeSet = new PreparedChangeSet(newFiles: [new PreparedFileWrite(outsidePath, "escape")]);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            File.Exists(outsidePath).ShouldBeFalse();
            Directory.EnumerateFileSystemEntries(repositoryRoot).ShouldBeEmpty();
            Directory.EnumerateFileSystemEntries(outsideRoot).ShouldBeEmpty();
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_requires_a_trusted_root_before_staging()
    {
        var directory = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var outputPath = Path.Combine(directory.FullName, "New.cs");
            var changeSet = new PreparedChangeSet(newFiles: [new PreparedFileWrite(outputPath, "class New;")]);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            var error = await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            error.Message.ShouldContain("no trusted repository root", Case.Sensitive);
            Directory.EnumerateFileSystemEntries(directory.FullName).ShouldBeEmpty();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_a_symlinked_output_directory_before_staging()
    {
        if (OperatingSystem.IsWindows())
            return;

        var parent = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var repositoryRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo")).FullName;
            var outsideRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "outside")).FullName;
            var link = Path.Combine(repositoryRoot, "linked");
            Directory.CreateSymbolicLink(link, outsideRoot);
            var outsidePath = Path.Combine(outsideRoot, "Escape.cs");
            using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
            var changeSet = new PreparedChangeSet(newFiles:
            [
                new PreparedFileWrite(Path.Combine(link, "Escape.cs"), "escape")
            ]);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            File.Exists(outsidePath).ShouldBeFalse();
            Directory.EnumerateFileSystemEntries(outsideRoot).ShouldBeEmpty();
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_a_symlinked_trusted_root_before_staging()
    {
        var parent = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(parent.FullName, "target")).FullName;
            var linkedRoot = Path.Combine(parent.FullName, "linked-root");
            try
            {
                Directory.CreateSymbolicLink(linkedRoot, target);
            }
            catch (Exception exception) when (OperatingSystem.IsWindows()
                                               && exception is UnauthorizedAccessException or IOException)
            {
                return;
            }

            using var trust = RepositoryTrustContext.UseExplicitTrust(linkedRoot);
            var changeSet = new PreparedChangeSet(newFiles:
            [
                new PreparedFileWrite(Path.Combine(linkedRoot, "New.cs"), "class New;")
            ]);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            Directory.EnumerateFileSystemEntries(target).ShouldBeEmpty();
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_parent_traversal_before_lexical_normalization()
    {
        if (OperatingSystem.IsWindows())
            return;

        var parent = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var repositoryRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo")).FullName;
            var outsideRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "outside")).FullName;
            var link = Path.Combine(repositoryRoot, "linked");
            Directory.CreateSymbolicLink(link, outsideRoot);
            using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
            var changeSet = new PreparedChangeSet(newFiles:
            [
                new PreparedFileWrite(Path.Combine(link, "..", "Inside.cs"), "escape")
            ]);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            File.Exists(Path.Combine(repositoryRoot, "Inside.cs")).ShouldBeFalse();
            Directory.EnumerateFileSystemEntries(outsideRoot).ShouldBeEmpty();
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_an_out_of_root_move_destination_before_staging()
    {
        var parent = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var repositoryRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo")).FullName;
            var outsideRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "outside")).FullName;
            var sourcePath = Path.Combine(repositoryRoot, "Source.cs");
            var destinationPath = Path.Combine(outsideRoot, "Destination.cs");
            await File.WriteAllTextAsync(sourcePath, "source");
            using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
            var changeSet = new PreparedChangeSet(movedFiles:
            [
                new PreparedFileMove(sourcePath, destinationPath)
            ]);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            (await File.ReadAllTextAsync(sourcePath)).ShouldBe("source");
            File.Exists(destinationPath).ShouldBeFalse();
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_an_out_of_root_solution_document_before_staging()
    {
        var parent = CreatePhysicalTempDirectory("rt-contained-");
        try
        {
            var repositoryRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "repo")).FullName;
            var outsideRoot = Directory.CreateDirectory(Path.Combine(parent.FullName, "outside")).FullName;
            var outsidePath = Path.Combine(outsideRoot, "Changed.cs");
            await File.WriteAllTextAsync(outsidePath, "class Before;");
            using var workspace = new AdhocWorkspace();
            var projectId = ProjectId.CreateNewId();
            var documentId = DocumentId.CreateNewId(projectId);
            var original = workspace.CurrentSolution
                .AddProject(ProjectInfo.Create(projectId, VersionStamp.Create(),
                    "Outside", "Outside", LanguageNames.CSharp))
                .AddDocument(documentId, "Changed.cs", SourceText.From("class Before;"),
                    filePath: outsidePath);
            var changed = original.WithDocumentText(documentId, SourceText.From("class After;"));
            using var trust = RepositoryTrustContext.UseExplicitTrust(repositoryRoot);
            var writer = new PreparedChangeSetWriter(new PhysicalPreparedFileSystem());

            await Should.ThrowAsync<IOException>(() =>
                writer.ApplyAsync(new PreparedChangeSet(original, changed)));

            (await File.ReadAllTextAsync(outsidePath)).ShouldBe("class Before;");
            Directory.EnumerateFileSystemEntries(repositoryRoot).ShouldBeEmpty();
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_a_move_conflict_before_any_write()
    {
        var directory = CreatePhysicalTempDirectory("rt-prepared-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "Before.cs");
            var destinationPath = Path.Combine(directory.FullName, "After.cs");
            var textPath = Path.Combine(directory.FullName, "notes.txt");
            await File.WriteAllTextAsync(sourcePath, "class Before;");
            await File.WriteAllTextAsync(destinationPath, "class After;");
            await File.WriteAllTextAsync(textPath, "before");
            var writer = new PreparedChangeSetWriter(
                new PhysicalPreparedFileSystem(), directory.FullName);
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

    [Fact]
    public async Task full_writer_creates_missing_move_destination_directories()
    {
        var directory = CreatePhysicalTempDirectory("rt-prepared-");
        try
        {
            var sourcePath = Path.Combine(directory.FullName, "Before.cs");
            var destinationPath = Path.Combine(directory.FullName, "nested", "After.cs");
            await File.WriteAllTextAsync(sourcePath, "class Before;");
            var writer = new PreparedChangeSetWriter(
                new PhysicalPreparedFileSystem(), directory.FullName);
            var changeSet = new PreparedChangeSet(
                movedFiles: [new PreparedFileMove(sourcePath, destinationPath)]);

            await writer.ApplyAsync(changeSet);

            File.Exists(sourcePath).ShouldBeFalse();
            (await File.ReadAllTextAsync(destinationPath)).ShouldBe("class Before;");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rolls_back_prior_writes_and_moves_when_a_later_move_fails()
    {
        var directory = CreatePhysicalTempDirectory("rt-prepared-");
        try
        {
            var firstSource = Path.Combine(directory.FullName, "First.cs");
            var firstDestination = Path.Combine(directory.FullName, "moved", "First.cs");
            var secondSource = Path.Combine(directory.FullName, "Second.cs");
            var secondDestination = Path.Combine(directory.FullName, "moved", "Second.cs");
            var projectPath = Path.Combine(directory.FullName, "App.csproj");
            await File.WriteAllTextAsync(firstSource, "first");
            await File.WriteAllTextAsync(secondSource, "second");
            await File.WriteAllTextAsync(projectPath, "before");
            var physical = new PhysicalPreparedFileSystem();
            var writer = new PreparedChangeSetWriter(new FaultingPreparedFileSystem(
                physical,
                (source, destination) =>
                    PathComparer.Equals(source, secondSource)
                    && PathComparer.Equals(destination, secondDestination)), directory.FullName);
            var changeSet = new PreparedChangeSet(
                movedFiles:
                [
                    new PreparedFileMove(firstSource, firstDestination),
                    new PreparedFileMove(secondSource, secondDestination)
                ],
                projectFileEdits: [new PreparedFileWrite(projectPath, "after")]);

            await Should.ThrowAsync<IOException>(() => writer.ApplyAsync(changeSet));

            (await File.ReadAllTextAsync(firstSource)).ShouldBe("first");
            (await File.ReadAllTextAsync(secondSource)).ShouldBe("second");
            (await File.ReadAllTextAsync(projectPath)).ShouldBe("before");
            File.Exists(firstDestination).ShouldBeFalse();
            File.Exists(secondDestination).ShouldBeFalse();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task full_writer_rejects_stale_solution_content_before_writing()
    {
        var directory = CreatePhysicalTempDirectory("rt-prepared-");
        try
        {
            var path = Path.Combine(directory.FullName, "Changed.cs");
            await File.WriteAllTextAsync(path, "class Stale;");
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
                    "Changed.cs",
                    SourceText.From("class Before;"),
                    filePath: path);
            var changed = original.WithDocumentText(
                documentId,
                SourceText.From("class After;"));
            var writer = new PreparedChangeSetWriter(
                new PhysicalPreparedFileSystem(), directory.FullName);

            await Should.ThrowAsync<IOException>(() =>
                writer.ApplyAsync(new PreparedChangeSet(original, changed)));

            (await File.ReadAllTextAsync(path)).ShouldBe("class Stale;");
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task solution_writer_preserves_legacy_dry_run_rendering()
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
                "Changed.cs",
                SourceText.From("class Before;"),
                filePath: "Changed.cs");
        var changed = original.WithDocumentText(
            documentId,
            SourceText.From("class After;"));
        var console = new CollectingConsole();
        var output = new RecordingSolutionWriteOutput();

        using (RtConsole.UseConsole(console))
            await new SolutionChangeWriter(output).ApplyChangesAsync(original, changed, dryRun: true);

        output.DiffLines.ShouldContain(line => line.Contains("--- Changed.cs", StringComparison.Ordinal));
        output.DiffLines.ShouldContain(line => line.Contains("class After;", StringComparison.Ordinal));
        output.DiffLines.ShouldContain(line => line.Contains("[dry-run]", StringComparison.Ordinal));
        console.Diff.ShouldBeEmpty();
    }

    private sealed class RecordingSolutionWriteOutput : ISolutionWriteOutput
    {
        internal List<string> DiffLines { get; } = [];

        public void Result(string message)
        {
        }

        public void Diff(string message) => DiffLines.Add(message);

        public void DocumentDiff(string oldText, string newText, string filePath) =>
            Adapters.SolutionDiffRenderer.PrintDiff(oldText, newText, filePath, this);

        public void Warning(string message)
        {
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

    private sealed class FaultingPreparedFileSystem(
        IPreparedFileSystem inner,
        Func<string, string, bool> shouldFailMove) : IPreparedFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public bool DirectoryIsEmpty(string path) => inner.DirectoryIsEmpty(path);

        public IEnumerable<string> EnumerateFiles(string path) => inner.EnumerateFiles(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            inner.CopyFile(sourcePath, destinationPath, overwrite);

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (shouldFailMove(sourcePath, destinationPath))
                throw new IOException("Injected move failure.");
            inner.MoveFile(sourcePath, destinationPath, overwrite);
        }

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public Task WriteAllTextAsync(
            string path,
            string content,
            Encoding? encoding,
            CancellationToken cancellationToken) =>
            inner.WriteAllTextAsync(path, content, encoding, cancellationToken);
    }

    private sealed class CollectingConsole : IRtConsole
    {
        internal List<string> Diff { get; } = [];

        public bool IsOutputRedirected => true;

        public void Log(RtLogLevel level, string template, params object?[] args)
        {
        }

        public void WriteLine(
            RtOutputContext context,
            string message = "",
            ConsoleColor? color = null)
        {
            if (context == RtOutputContext.Diff)
                Diff.Add(message);
        }

        public void Write(
            RtOutputContext context,
            string message,
            ConsoleColor? color = null)
        {
            if (context == RtOutputContext.Diff)
                Diff.Add(message);
        }
    }

    private sealed class TestAnalyzerReference : AnalyzerReference
    {
        public override string FullPath => "TestAnalyzer.dll";

        public override object Id { get; } = new();

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language) => [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages() => [];
    }

    private static DirectoryInfo CreatePhysicalTempDirectory(string prefix) =>
        new(RepositoryTrustRegistry.CanonicalizeExistingPath(
            Directory.CreateTempSubdirectory(prefix).FullName));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
