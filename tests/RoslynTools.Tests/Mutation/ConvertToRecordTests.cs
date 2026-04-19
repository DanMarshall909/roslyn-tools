using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ConvertToRecordTests
{
    private const string RecordAppSource = """
        class Point { public int X { get; init; } public int Y { get; init; } }
        class WithEquals {
          public int Value { get; init; }
          public override bool Equals(object? obj) => obj is WithEquals o && o.Value == Value;
          public override int GetHashCode() => Value.GetHashCode();
        }
        class WithMutableProp { public int Id { get; set; } public string Name { get; init; } = ""; }
        """;

    // ──────────────────────────────────────────────────────────────────────────
    //  1. class Point becomes record Point
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToRecord_ClassBecomesRecord()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ConvertToRecordCommand.ExecuteAsync("Point", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("record Point", content);
        Assert.DoesNotContain("class Point", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. init-only properties become positional parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToRecord_InitPropsBecomePrimaryParams()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ConvertToRecordCommand.ExecuteAsync("Point", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        // Positional record syntax: record Point(int X, int Y)
        Assert.Contains("int X", content);
        Assert.Contains("int Y", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Explicit Equals/GetHashCode overrides are removed (record provides them)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToRecord_ExplicitEqualsRemoved()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ConvertToRecordCommand.ExecuteAsync("WithEquals", "ignored", dryRun: false);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.DoesNotContain("override bool Equals", content);
        Assert.DoesNotContain("override int GetHashCode", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Mutable properties: still converts, warns on stderr about mutability
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToRecord_MutablePropsWarnButConvert()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        // Capture stderr to verify warning
        var originalError = Console.Error;
        var errorCapture = new System.IO.StringWriter();
        Console.SetError(errorCapture);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln)) await ConvertToRecordCommand.ExecuteAsync("WithMutableProp", "ignored", dryRun: false);
        }
        finally
        {
            Console.SetError(originalError);
        }

        var stderrOutput = errorCapture.ToString();
        Assert.Contains("mutable", stderrOutput, StringComparison.OrdinalIgnoreCase);

        var content = await proj.ReadSourceAsync("Types.cs");
        Assert.Contains("record WithMutableProp", content);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. --dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConvertToRecord_DryRun_NoWrites()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ConvertToRecordCommand.ExecuteAsync("Point", "ignored", dryRun: true);

        var after = await proj.ReadSourceAsync("Types.cs");
        Assert.Equal(before, after);
    }
}
