using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
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
        using var proj = new TestProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var slnPath = await proj.BuildAsync();

        await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", slnPath, dryRun: false);

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
        using var proj = new TestProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var slnPath = await proj.BuildAsync();

        await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", slnPath, dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("throw new NotImplementedException()", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. PartialRepo already has GetAsync — only Delete and Name stubs added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_AlreadyImplemented_Skipped()
    {
        using var proj = new TestProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var slnPath = await proj.BuildAsync();

        await ImplementInterfaceCommand.ExecuteAsync("PartialRepo", "IRepository", slnPath, dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        // Delete and Name should be added as stubs
        Assert.Contains("Delete", content);
        Assert.Contains("Name", content);
        // GetAsync is already fully implemented — should not be duplicated
        var getAsyncCount = content.Split("GetAsync").Length - 1;
        Assert.Equal(1, getAsyncCount);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Name property stub uses get => throw new NotImplementedException()
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImplementInterface_PropertyStub_Generated()
    {
        using var proj = new TestProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var slnPath = await proj.BuildAsync();

        await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", slnPath, dryRun: false);

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
        using var proj = new TestProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var slnPath = await proj.BuildAsync();

        var before = await proj.ReadSourceAsync("Types.cs");

        await ImplementInterfaceCommand.ExecuteAsync("Repo", "IRepository", slnPath, dryRun: true);

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }
}
