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
