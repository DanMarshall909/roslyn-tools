using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class RenameIntegration
{
    [Fact]
    public async Task related_rename_edits_are_applied_only_when_their_ids_are_selected()
    {
        const string model = """
                             namespace Foo;
                             // OldType remains prose unless selected.
                             public class OldType
                             {
                                 public string WindowClass => "OldTypeWindow";
                             }
                             """;
        const string related = """
                               namespace Foo;
                               public class OldTypeTests { }
                               """;

        using var project = new AdhocProjectBuilder("RelatedRenameSample")
            .AddSource("OldType.cs", model)
            .AddSource("OldTypeTests.cs", related);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var service = new RenameService();
            var plan = await service.BuildRelatedRenamePlanAsync(solution, "Foo.OldType", "NewType", "type");
            var identifier = plan.Edits.Single(edit => edit.OldText == "OldTypeTests");
            var literal = plan.Edits.Single(edit => edit.Kind == "string");
            var selected = new HashSet<string>([identifier.Id, literal.Id], StringComparer.Ordinal);

            var withRelated = await service.ApplyRelatedRenameEditsAsync(solution, plan, selected);
            var renamed = await service.RenameAsync(withRelated, "Foo.OldType", "NewType", "type");
            var text = string.Join(
                "\n",
                await Task.WhenAll(renamed.Projects.SelectMany(p => p.Documents)
                    .Select(async document => (await document.GetTextAsync()).ToString())));

            text.ShouldContain("class NewTypeTests", Case.Sensitive);
            text.ShouldContain("\"NewTypeWindow\"", Case.Sensitive);
            text.ShouldContain("// OldType remains prose unless selected.", Case.Sensitive);
        }
    }

    [Fact]
    public async Task semantic_rename_does_not_apply_unselected_source_file_rename()
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

        Assert.True(project.SourceExists("OldType.cs"), "unselected file rename should not be applied");
        Assert.False(project.SourceExists("NewType.cs"), "unselected target file should not be created");

        var renamed = await project.ReadSourceAsync("OldType.cs");
        renamed.ShouldContain("class NewType", Case.Sensitive);
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
        renamed.ShouldContain("class NewB", Case.Sensitive);
        renamed.ShouldContain("void pending()", Case.Sensitive);
    }
}
