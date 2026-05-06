using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests.Analysis;

public class CircularDepsMigrationTests : IDisposable
{
    public CircularDepsMigrationTests()
    {
        RtContext.Format = OutputFormat.Text;
    }

    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
        GC.SuppressFinalize(this);
    }

    // Three namespaces that form a cycle: A -> B -> C -> A
    private static AdhocProjectBuilder BuildCycleProject()
    {
        var proj = new AdhocProjectBuilder("CycleApp");
        proj.AddSource("FileA.cs", "namespace CycleApp.A { using CycleApp.B; class A { } }");
        proj.AddSource("FileB.cs", "namespace CycleApp.B { using CycleApp.C; class B { } }");
        proj.AddSource("FileC.cs", "namespace CycleApp.C { using CycleApp.A; class C { } }");
        return proj;
    }

    private static AdhocProjectBuilder BuildCleanProject()
    {
        var proj = new AdhocProjectBuilder("CycleApp");
        proj.AddSource("FileX.cs", "namespace CycleApp.X { class X { } }");
        proj.AddSource("FileY.cs", "namespace CycleApp.Y { using CycleApp.X; class Y { } }");
        return proj;
    }

    // ── AC-3: RenderText reproduces prior plain-text output byte-for-byte ────────

    [Fact]
    public async Task RenderText_cycles_matches_AnalyzeAsync_output()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var legacyText = await CircularDepsCommand.AnalyzeAsync(sln, scope: null);

            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            result.RenderText(sw);
            // RenderText ends each line with newline; legacy used Console.WriteLine which adds one newline
            // The legacy output via Console.WriteLine(result) adds a trailing newline
            var rendered = sw.ToString();

            // Each cycle line in rendered should match the legacy text
            Assert.Equal(legacyText + Environment.NewLine, rendered);
        }
    }

    [Fact]
    public async Task RenderText_no_cycles_produces_expected_message()
    {
        using var proj = BuildCleanProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            result.RenderText(sw);

            Assert.Equal("No circular namespace dependencies found." + Environment.NewLine, sw.ToString());
        }
    }

    // ── AC-4: --json emits cycles as JSON arrays with ordered node lists ─────────

    [Fact]
    public async Task Json_format_emits_cycles_array_with_path_and_nodes()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            Assert.NotEmpty(cycles);

            var result = CircularDepsResult.FromCycles(cycles);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, sw);

            var doc = JsonDocument.Parse(sw.ToString());
            var root = doc.RootElement;

            Assert.True(root.TryGetProperty("hasCycles", out var hasCycles));
            Assert.True(hasCycles.GetBoolean());

            Assert.True(root.TryGetProperty("cycles", out var cyclesArr));
            Assert.Equal(JsonValueKind.Array, cyclesArr.ValueKind);
            Assert.True(cyclesArr.GetArrayLength() >= 1);

            var firstCycle = cyclesArr[0];
            Assert.True(firstCycle.TryGetProperty("path", out var pathProp));
            Assert.Contains("->", pathProp.GetString());

            Assert.True(firstCycle.TryGetProperty("nodes", out var nodesProp));
            Assert.Equal(JsonValueKind.Array, nodesProp.ValueKind);
            Assert.True(nodesProp.GetArrayLength() >= 2);
        }
    }

    // ── AC-5: --markdown emits a Markdown section per cycle ──────────────────────

    [Fact]
    public async Task Markdown_format_emits_h1_header_and_per_cycle_sections()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            Assert.NotEmpty(cycles);

            var result = CircularDepsResult.FromCycles(cycles);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Markdown;
            RtSerializer.Write(result, sw);
            var md = sw.ToString();

            Assert.Contains("# Circular Namespace Dependencies", md);
            Assert.Contains("## Cycle 1", md);
            Assert.Contains("**Path:**", md);
            Assert.Contains("->", md);
        }
    }

    [Fact]
    public async Task Markdown_no_cycles_emits_clean_message()
    {
        using var proj = BuildCleanProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Markdown;
            RtSerializer.Write(result, sw);
            var md = sw.ToString();

            Assert.Contains("# Circular Namespace Dependencies", md);
            Assert.Contains("No circular namespace dependencies found.", md);
        }
    }

    // ── AC-6: --llm emits compact one-cycle-per-record output ────────────────────

    [Fact]
    public async Task Llm_format_emits_hasCycles_and_path_fields()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            Assert.NotEmpty(cycles);

            var result = CircularDepsResult.FromCycles(cycles);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Llm;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            Assert.Contains("hasCycles:", output);
            Assert.Contains("True", output);
            Assert.Contains("path:", output);
            Assert.Contains("->", output);
        }
    }

    // ── AC-7: no flag produces unchanged legacy text output ───────────────────────

    [Fact]
    public async Task Text_format_produces_cycle_paths_matching_legacy_output()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var legacyText = await CircularDepsCommand.AnalyzeAsync(sln, scope: null);

            var cycles = await CircularDepsCommand.AnalyzeCyclesAsync(sln, scope: null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Text;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            Assert.Equal(legacyText + Environment.NewLine, output);
        }
    }

    // ── AC-8: RtCommandInfo.SupportedFormats includes all four formats ─────────────

    [Fact]
    public void CircularDepsCommand_Info_has_all_four_supported_formats()
    {
        var info = CircularDepsCommand.Info;
        var formats = info.SupportedFormats;
        Assert.NotNull(formats);
        Assert.Contains(OutputFormat.Text, formats);
        Assert.Contains(OutputFormat.Json, formats);
        Assert.Contains(OutputFormat.Markdown, formats);
        Assert.Contains(OutputFormat.Llm, formats);
    }
}
