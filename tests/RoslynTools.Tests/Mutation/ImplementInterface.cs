using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Mutation;

public class ImplementInterface : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ImplementInterface(RtServicesFixture services) => _services = services;

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
    public async Task adds_stubs_for_unimplemented_members()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ImplementInterfaceCommand>().ExecuteAsync("Repo", "IRepository", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("GetAsync", content);
        Assert.Contains("Delete", content);
        Assert.Contains("Name", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Generated method bodies throw NotImplementedException
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task makes_stub_bodies_throw_not_implemented_exception()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ImplementInterfaceCommand>().ExecuteAsync("Repo", "IRepository", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("throw new NotImplementedException()", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. PartialRepo already has GetAsync — only Delete and Name stubs added
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task skips_members_that_are_already_implemented()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ImplementInterfaceCommand>().ExecuteAsync("PartialRepo", "IRepository", "ignored", false);
        }

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
    public async Task generates_property_stubs()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ImplementInterfaceCommand>().ExecuteAsync("Repo", "IRepository", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("get =>", content);
        Assert.Contains("throw new NotImplementedException()", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. --dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder("ImplApp");
        proj.AddSource("Types.cs", ImplAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ImplementInterfaceCommand>().ExecuteAsync("Repo", "IRepository", "ignored", true);
        }

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }
}
