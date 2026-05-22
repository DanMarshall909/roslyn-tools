using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ConvertToRecord : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ConvertToRecord(RtServicesFixture services) => _services = services;

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
    public async Task converts_the_class_to_a_record()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ConvertToRecordCommand>().ExecuteAsync("Point", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        content.ShouldContain("record Point", Case.Sensitive);
        content.ShouldNotContain("class Point", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  2. init-only properties become positional parameters
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task converts_init_only_properties_to_primary_parameters()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ConvertToRecordCommand>().ExecuteAsync("Point", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        // Positional record syntax: record Point(int X, int Y)
        content.ShouldContain("int X", Case.Sensitive);
        content.ShouldContain("int Y", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  3. Explicit Equals/GetHashCode overrides are removed (record provides them)
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task removes_explicit_equality_members()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ConvertToRecordCommand>().ExecuteAsync("WithEquals", "ignored", false);
        }

        var content = await proj.ReadSourceAsync("Types.cs");
        content.ShouldNotContain("override bool Equals", Case.Sensitive);
        content.ShouldNotContain("override int GetHashCode", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  4. Mutable properties: still converts, warns on stderr about mutability
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task warns_but_still_converts_the_class()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        // Capture stderr to verify warning
        var originalError = Console.Error;
        var errorCapture = new StringWriter();
        Console.SetError(errorCapture);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
            {
                await _services.Resolve<ConvertToRecordCommand>().ExecuteAsync("WithMutableProp", "ignored", false);
            }
        }
        finally
        {
            Console.SetError(originalError);
        }

        var stderrOutput = errorCapture.ToString();
        Assert.Contains("mutable", stderrOutput, StringComparison.OrdinalIgnoreCase);

        var content = await proj.ReadSourceAsync("Types.cs");
        content.ShouldContain("record WithMutableProp", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  5. --dry-run shows diff but does not write
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task does_not_modify_files()
    {
        using var proj = new AdhocProjectBuilder("RecordApp");
        proj.AddSource("Types.cs", RecordAppSource);
        var (workspace, sln) = proj.Build();

        var before = await proj.ReadSourceAsync("Types.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ConvertToRecordCommand>().ExecuteAsync("Point", "ignored", true);
        }

        var after = await proj.ReadSourceAsync("Types.cs");
        after.ShouldBe(before);
    }
}
