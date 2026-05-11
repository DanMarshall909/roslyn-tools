using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Commands;
using RoslynTools.Services;

namespace RoslynTools.Tests.Refactor;

public class Move
{
    [Fact]
    [Trait("Concern", "Metadata")]
    public void examples_include_to_project_usage()
    {
        var examples = MoveCommand.Info.Examples;
        Assert.Contains(examples, e => e.Contains("--to-project"));
    }

    [Fact]
    [Trait("Concern", "Metadata")]
    public void to_project_option_is_registered_with_optional_to_namespace_noted()
    {
        var cmd = MoveCommand.Build();

        var toProjectOpt = cmd.Options.FirstOrDefault(o => o.Name == "to-project");
        Assert.NotNull(toProjectOpt);

        var toNsOpt = cmd.Options.FirstOrDefault(o => o.Name == "to-namespace");
        Assert.NotNull(toNsOpt);
        Assert.Contains("--to-project", toNsOpt.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Concern", "CrossProjectMove")]
    public async Task file_moves_and_namespace_rewritten_and_consumers_updated_when_both_options_provided()
    {
        const string src = """
                           namespace Source.Models;
                           public class Invoice { }
                           """;
        const string consumerSrc = """
                                   using Source.Models;
                                   namespace App;
                                   public class Processor
                                   {
                                       public void Process(Invoice i) { }
                                   }
                                   """;

        using var builder = new AdhocProjectBuilder("SourceProject")
            .AddSource("Models/Invoice.cs", src)
            .AddSource("Consumer.cs", consumerSrc);
        builder.AddProject("TargetProject");

        var (workspace, solution) = builder.Build();

        await new MoveService().ExecuteCrossProjectMoveAsync(
            workspace, solution,
            "Source.Models.Invoice",
            "TargetProject",
            "Target.Domain",  // --to-namespace provided
            false);

        var targetDir = Path.GetDirectoryName(
            solution.Projects.First(p => p.Name == "TargetProject").FilePath)!;

        // File moved to mirrored path
        var movedContent = await File.ReadAllTextAsync(Path.Combine(targetDir, "Models", "Invoice.cs"));

        // Namespace rewritten in moved file
        Assert.Contains("namespace Target.Domain;", movedContent);
        Assert.DoesNotContain("namespace Source.Models;", movedContent);

        // Consumer using directive updated
        var consumerContent = await builder.ReadSourceAsync("Consumer.cs");
        Assert.Contains("using Target.Domain;", consumerContent);
    }

    [Fact]
    [Trait("Concern", "CrossProjectMove")]
    public async Task namespace_is_preserved_when_to_namespace_is_omitted()
    {
        const string src = """
                           namespace Source.Models;
                           public class Order { }
                           """;
        const string consumerSrc = """
                                   using Source.Models;
                                   namespace App;
                                   public class Handler
                                   {
                                       public void Handle(Order o) { }
                                   }
                                   """;

        using var builder = new AdhocProjectBuilder("SourceProject")
            .AddSource("Models/Order.cs", src)
            .AddSource("Handler.cs", consumerSrc);
        builder.AddProject("TargetProject");

        var (workspace, solution) = builder.Build();

        await new MoveService().ExecuteCrossProjectMoveAsync(
            workspace, solution,
            "Source.Models.Order",
            "TargetProject",
            null,   // no --to-namespace
            false);

        var targetDir = Path.GetDirectoryName(
            solution.Projects.First(p => p.Name == "TargetProject").FilePath)!;

        var movedContent = await File.ReadAllTextAsync(Path.Combine(targetDir, "Models", "Order.cs"));

        // Namespace declaration must be byte-identical
        Assert.Contains("namespace Source.Models;", movedContent);

        // Consumer file must be untouched
        var consumerContent = await builder.ReadSourceAsync("Handler.cs");
        Assert.Contains("using Source.Models;", consumerContent);
        Assert.DoesNotContain("using TargetProject", consumerContent);
    }

    [Fact]
    [Trait("Concern", "CrossProjectMove")]
    public async Task file_moves_to_target_project_dir_with_subfolder_mirrored()
    {
        const string src = """
                           namespace Source.Models;
                           public class Product { }
                           """;

        using var builder = new AdhocProjectBuilder("SourceProject")
            .AddSource("Models/Product.cs", src);
        builder.AddProject("TargetProject");

        var (workspace, solution) = builder.Build();

        var targetDir = Path.GetDirectoryName(
            solution.Projects.First(p => p.Name == "TargetProject").FilePath)!;

        await new MoveService().ExecuteCrossProjectMoveAsync(
            workspace, solution,
            "Source.Models.Product",
            "TargetProject",
            null,
            false);

        var expectedDest = Path.Combine(targetDir, "Models", "Product.cs");
        Assert.True(File.Exists(expectedDest), $"Expected file at {expectedDest}");
        Assert.False(builder.SourceExists("Models/Product.cs"), "Original file should have been removed");
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task moving_to_same_project_is_a_no_op_with_nothing_to_move_message()
    {
        const string src = """
                           namespace Foo;
                           public class Widget { }
                           """;

        using var builder = new AdhocProjectBuilder("SameProject")
            .AddSource("Widget.cs", src);

        var (workspace, solution) = builder.Build();

        // Both source and target are "SameProject" — should be a no-op
        await new MoveService().ExecuteCrossProjectMoveAsync(
            workspace, solution,
            "Foo.Widget",
            "SameProject",
            null,
            false);

        // File must still exist unchanged
        Assert.True(builder.SourceExists("Widget.cs"));
        var content = await builder.ReadSourceAsync("Widget.cs");
        Assert.Contains("namespace Foo;", content);
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task unknown_target_project_reports_project_name_and_throws()
    {
        const string src = """
                           namespace Foo;
                           public class Bar { }
                           """;

        using var project = new AdhocProjectBuilder("SourceProject")
            .AddSource("Bar.cs", src);

        var (workspace, solution) = project.Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new MoveService().ExecuteCrossProjectMoveAsync(
                workspace, solution,
                "Foo.Bar",
                "NonExistentProject",
                null,
                false));

        Assert.Contains("NonExistentProject", ex.Message);
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task source_file_outside_source_project_directory_throws_with_descriptive_message()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var sourceDir = Path.Combine(tempDir, "SourceProject");
        var targetDir = Path.Combine(tempDir, "TargetProject");
        var outsideDir = Path.Combine(tempDir, "outside");

        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(targetDir);
        Directory.CreateDirectory(outsideDir);

        // Source file intentionally placed OUTSIDE the source project directory
        var outsideFile = Path.Combine(outsideDir, "Orphan.cs");
        const string src = "namespace Foo;\npublic class Orphan { }";
        await File.WriteAllTextAsync(outsideFile, src);

        try
        {
            var bclRef = MetadataReference.CreateFromFile(typeof(object).Assembly.Location);
            var sourceId = ProjectId.CreateNewId();
            var targetId = ProjectId.CreateNewId();

            var doc = DocumentInfo.Create(
                DocumentId.CreateNewId(sourceId),
                "Orphan.cs",
                filePath: outsideFile,
                loader: TextLoader.From(TextAndVersion.Create(
                    SourceText.From(src), VersionStamp.Create(), outsideFile)));

            var sourceInfo = ProjectInfo.Create(
                sourceId, VersionStamp.Create(), "SourceProject", "SourceProject",
                LanguageNames.CSharp,
                filePath: Path.Combine(sourceDir, "SourceProject.csproj"),
                documents: [doc],
                metadataReferences: [bclRef]);

            var targetInfo = ProjectInfo.Create(
                targetId, VersionStamp.Create(), "TargetProject", "TargetProject",
                LanguageNames.CSharp,
                filePath: Path.Combine(targetDir, "TargetProject.csproj"),
                metadataReferences: [bclRef]);

            var workspace = new AdhocWorkspace();
            workspace.AddProject(sourceInfo);
            workspace.AddProject(targetInfo);

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new MoveService().ExecuteCrossProjectMoveAsync(
                    workspace, workspace.CurrentSolution,
                    "Foo.Orphan",
                    "TargetProject",
                    null,
                    false));

            Assert.Contains("Orphan", ex.Message);
            Assert.Contains(outsideFile, ex.Message);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
