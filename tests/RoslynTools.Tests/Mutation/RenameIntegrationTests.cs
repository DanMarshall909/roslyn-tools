using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class RenameIntegrationTests
{
    [Fact]
    public async Task RenameClass_renames_source_file_when_basename_matches_old_name()
    {
        const string source = """
            namespace Foo;
            public class OldType
            {
                public void Run() { }
            }
            """;

        using var project = new AdhocProjectBuilder("RenameSample")
            .AddSource("OldType.cs", source);

        var (workspace, solution) = project.Build();

        await new RenameService().ExecuteSingleAsync(
            sln: solution,
            symbol: "Foo.OldType",
            newName: "NewType",
            kind: "type",
            dryRun: false);

        Assert.False(project.SourceExists("OldType.cs"), "old file should be gone");
        Assert.True(project.SourceExists("NewType.cs"), "new file should exist");

        var renamed = await project.ReadSourceAsync("NewType.cs");
        Assert.Contains("class NewType", renamed);
    }

    [Fact]
    public async Task BatchRename_renames_each_source_file_when_basename_matches_old_name()
    {
        const string sourceA = """
            namespace Foo;
            public class OldA { }
            """;
        const string sourceB = """
            namespace Foo;
            public class OldB { }
            """;

        using var project = new AdhocProjectBuilder("RenameBatchSample")
            .AddSource("OldA.cs", sourceA)
            .AddSource("OldB.cs", sourceB);

        var (_, solution) = project.Build();

        var csvPath = Path.Combine(project.ProjectDir, "rename.csv");
        File.WriteAllText(csvPath, "source_symbol,target_name\nFoo.OldA,NewA\nFoo.OldB,NewB\n");

        await new RenameService().ExecuteBatchAsync(solution, csvPath, dryRun: false);

        Assert.False(project.SourceExists("OldA.cs"), "old file A should be gone");
        Assert.True(project.SourceExists("NewA.cs"), "new file A should exist");
        Assert.False(project.SourceExists("OldB.cs"), "old file B should be gone");
        Assert.True(project.SourceExists("NewB.cs"), "new file B should exist");
    }
}
