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

        await RenameService.ExecuteSingleAsync(
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
}
