using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Mutation;

public class MakeSealed : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public MakeSealed(RtServicesFixture services) => _services = services;

    private const string SealAppSource = """
                                         class Standalone { public void Go() { } }
                                         class Animal { }
                                         class Dog : Animal { }
                                         abstract class Shape { }
                                         sealed class Circle { }
                                         """;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. Class with no derived types gets sealed modifier
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task adds_the_sealed_keyword()
    {
        using var proj = new AdhocProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeSealedCommand>().ExecuteAsync("Standalone", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("sealed class Standalone", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Class that has derived types is rejected with a message listing them
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task throws_an_error()
    {
        using var proj = new AdhocProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _services.Resolve<MakeSealedCommand>().ExecuteAsync("Animal", "ignored", false));

            Assert.Contains("Dog", ex.Message);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Abstract class is skipped (prints message, exits 0, no file change)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task leaves_the_abstract_type_unchanged()
    {
        using var proj = new AdhocProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        // Should not throw — just skip
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeSealedCommand>().ExecuteAsync("Shape", "ignored", false);
        }

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Already-sealed class is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task leaves_an_already_sealed_type_unchanged()
    {
        using var proj = new AdhocProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeSealedCommand>().ExecuteAsync("Circle", "ignored", false);
        }

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeSealedCommand>().ExecuteAsync("Standalone", "ignored", true);
        }

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task reject_missing_types()
    {
        using var proj = new AdhocProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var (_, sln) = proj.Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _services.Resolve<MakeSealedService>().RunAsync(sln, "Missing", false));

        Assert.Contains("Class 'Missing' not found", exception.Message);
    }
}
