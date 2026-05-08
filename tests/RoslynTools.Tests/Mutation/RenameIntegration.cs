using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class RenameIntegration
{
    [Fact]
    public async Task renames_class_renames_source_file_when_basename_matches_old_name()
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
            solution,
            "Foo.OldType",
            "NewType",
            "type",
            false);

        Assert.False(project.SourceExists("OldType.cs"), "old file should be gone");
        Assert.True(project.SourceExists("NewType.cs"), "new file should exist");

        var renamed = await project.ReadSourceAsync("NewType.cs");
        Assert.Contains("class NewType", renamed);
    }

    [Fact]
    public async Task batch_rename_renames_each_source_file_when_basename_matches_old_name()
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

        await new RenameService().ExecuteBatchAsync(solution, csvPath, false);

        Assert.False(project.SourceExists("OldA.cs"), "old file A should be gone");
        Assert.True(project.SourceExists("NewA.cs"), "new file A should exist");
        Assert.False(project.SourceExists("OldB.cs"), "old file B should be gone");
        Assert.True(project.SourceExists("NewB.cs"), "new file B should exist");
    }

    [Fact]
    public async Task batch_rename_skips_already_applied_prefixed_fqn_rows()
    {
        const string sourceA = """
                               namespace Foo;
                               public class NewA
                               {
                                   public void done() { }
                               }
                               """;
        const string sourceB = """
                               namespace Foo;
                               public class OldB
                               {
                                   public void Pending() { }
                               }
                               """;

        using var project = new AdhocProjectBuilder("RenameBatchResumeSample")
            .AddSource("NewA.cs", sourceA)
            .AddSource("OldB.cs", sourceB);

        var (_, solution) = project.Build();

        var csvPath = Path.Combine(project.ProjectDir, "rename.csv");
        File.WriteAllText(
            csvPath,
            """
            file,type_fqn,type_name,type_name_new,member_fqn,member_name,member_name_new
            tests/OldA.cs,Foo.OldA,OldA,NewA,Foo.OldA.Done,Done,done
            tests/OldB.cs,Foo.OldB,OldB,NewB,Foo.OldB.Pending,Pending,pending

            """);

        await new RenameService().ExecuteBatchAsync(solution, csvPath, false);

        Assert.True(project.SourceExists("NewA.cs"), "already-renamed file should remain");
        Assert.False(project.SourceExists("OldB.cs"), "pending old file should be gone");
        Assert.True(project.SourceExists("NewB.cs"), "pending new file should exist");

        var renamed = await project.ReadSourceAsync("NewB.cs");
        Assert.Contains("class NewB", renamed);
        Assert.Contains("void pending()", renamed);
    }
}
