using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class MakeSealedTests
{
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
    public async Task MakeSealed_NoDerivedTypes_AddsSealedKeyword()
    {
        using var proj = new TestProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var slnPath = await proj.BuildAsync();

        await MakeSealedCommand.ExecuteAsync("Standalone", slnPath, dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("sealed class Standalone", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. Class that has derived types is rejected with a message listing them
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeSealed_HasDerivedType_Rejected()
    {
        using var proj = new TestProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var slnPath = await proj.BuildAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeSealedCommand.ExecuteAsync("Animal", slnPath, dryRun: false));

        Assert.Contains("Dog", ex.Message);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Abstract class is skipped (prints message, exits 0, no file change)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeSealed_AbstractClass_Skipped()
    {
        using var proj = new TestProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var slnPath = await proj.BuildAsync();

        var before = await proj.ReadSourceAsync("Types.cs");

        // Should not throw — just skip
        await MakeSealedCommand.ExecuteAsync("Shape", slnPath, dryRun: false);

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Already-sealed class is a no-op
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeSealed_AlreadySealed_NoOp()
    {
        using var proj = new TestProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var slnPath = await proj.BuildAsync();

        var before = await proj.ReadSourceAsync("Types.cs");

        await MakeSealedCommand.ExecuteAsync("Circle", slnPath, dryRun: false);

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. Dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MakeSealed_DryRun_NoWrites()
    {
        using var proj = new TestProjectBuilder("SealApp");
        proj.AddSource("Types.cs", SealAppSource);
        var slnPath = await proj.BuildAsync();

        var before = await proj.ReadSourceAsync("Types.cs");

        await MakeSealedCommand.ExecuteAsync("Standalone", slnPath, dryRun: true);

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }
}
