using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ImplementInterfaceTests
{
    // Source that compiles: Repo and PartialRepo don't yet declare IRepository,
    // so they build successfully before implement-interface runs.
    private const string ImplAppSource = """
        interface IRepository {
          System.Threading.Tasks.Task<string> GetAsync(int id);
          void Delete(int id);
          string Name { get; }
        }
        class Repo { }
        class PartialRepo {
          public System.Threading.Tasks.Task<string> GetAsync(int id) => System.Threading.Tasks.Task.FromResult("x");
        }
        """;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. Repo gets stubs for all three unimplemented members
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_StubsAddedForUnimplementedMembers()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("GetAsync", content);
        Assert.Contains("Delete", content);
        Assert.Contains("Name", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Generated method bodies throw NotImplementedException
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_StubBodyThrowsNotImplementedException()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("throw new NotImplementedException()", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. PartialRepo already has GetAsync — only Delete and Name stubs added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_AlreadyImplemented_Skipped()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ImplementInterfaceCommand.ExecuteAsync("PartialRepo", "IRepository", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        // Delete and Name should be added as stubs
        Assert.Contains("Delete", content);
        Assert.Contains("Name", content);
        // GetAsync is already fully implemented — should not be duplicated
        // (count = 2: once in IRepository definition, once in PartialRepo body; not 3)
        var getAsyncCount = content.Split("GetAsync").Length - 1;
        Assert.Equal(2, getAsyncCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Name property stub uses get => throw new NotImplementedException()
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_PropertyStub_Generated()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("get =>", content);
        Assert.Contains("throw new NotImplementedException()", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. --dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_DryRun_NoWrites()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", "ignored", dryRun: true);

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }
}
