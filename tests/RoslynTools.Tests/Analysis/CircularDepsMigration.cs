using System.Text.Json;
using RoslynTools.Commands;
using RoslynTools.Commands.Results;
using RoslynTools.Core.Output;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public class CircularDepsMigration : IClassFixture<RtServicesFixture>, IDisposable
{
    private readonly RtServicesFixture _services;
    public CircularDepsMigration(RtServicesFixture services)
    {
        _services = services;
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
    public async Task renders_text_for_cycles_matching_analyze_async_output()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var legacyText = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);

            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            result.RenderText(sw);
            // RenderText ends each line with newline; legacy used Console.WriteLine which adds one newline
            // The legacy output via Console.WriteLine(result) adds a trailing newline
            var rendered = sw.ToString();

            // Each cycle line in rendered should match the legacy text
            rendered.ShouldBe(legacyText + Environment.NewLine);
        }
    }

    [Fact]
    public async Task renders_text_message_when_no_cycles_exist()
    {
        using var proj = BuildCleanProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            result.RenderText(sw);

            sw.ToString().ShouldBe("No circular namespace dependencies found." + Environment.NewLine);
        }
    }

    // ── AC-4: --json emits cycles as JSON arrays with ordered node lists ─────────

    [Fact]
    public async Task emits_json_format_with_cycles_array_path_and_nodes()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            cycles.ShouldNotBeEmpty();

            var result = CircularDepsResult.FromCycles(cycles);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Json;
            RtSerializer.Write(result, sw);

            var doc = JsonDocument.Parse(sw.ToString());
            var root = doc.RootElement;

            root.TryGetProperty("hasCycles", out var hasCycles).ShouldBeTrue();
            hasCycles.GetBoolean().ShouldBeTrue();

            root.TryGetProperty("cycles", out var cyclesArr).ShouldBeTrue();
            cyclesArr.ValueKind.ShouldBe(JsonValueKind.Array);
            (cyclesArr.GetArrayLength() >= 1).ShouldBeTrue();

            var firstCycle = cyclesArr[0];
            firstCycle.TryGetProperty("path", out var pathProp).ShouldBeTrue();
            pathProp.GetString()!.ShouldContain("->", Case.Sensitive);

            firstCycle.TryGetProperty("nodes", out var nodesProp).ShouldBeTrue();
            nodesProp.ValueKind.ShouldBe(JsonValueKind.Array);
            (nodesProp.GetArrayLength() >= 2).ShouldBeTrue();
        }
    }

    // ── AC-5: --markdown emits a Markdown section per cycle ──────────────────────

    [Fact]
    public async Task emits_markdown_format_with_h1_header_and_per_cycle_sections()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            cycles.ShouldNotBeEmpty();

            var result = CircularDepsResult.FromCycles(cycles);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Markdown;
            RtSerializer.Write(result, sw);
            var md = sw.ToString();

            md.ShouldContain("# Circular Namespace Dependencies", Case.Sensitive);
            md.ShouldContain("## Cycle 1", Case.Sensitive);
            md.ShouldContain("**Path:**", Case.Sensitive);
            md.ShouldContain("->", Case.Sensitive);
        }
    }

    [Fact]
    public async Task emits_markdown_format_clean_message_when_no_cycles_exist()
    {
        using var proj = BuildCleanProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Markdown;
            RtSerializer.Write(result, sw);
            var md = sw.ToString();

            md.ShouldContain("# Circular Namespace Dependencies", Case.Sensitive);
            md.ShouldContain("No circular namespace dependencies found.", Case.Sensitive);
        }
    }

    // ── AC-6: --llm emits compact one-cycle-per-record output ────────────────────

    [Fact]
    public async Task emits_llm_format_with_has_cycles_and_path_fields()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            cycles.ShouldNotBeEmpty();

            var result = CircularDepsResult.FromCycles(cycles);
            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Llm;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            output.ShouldContain("hasCycles:", Case.Sensitive);
            output.ShouldContain("True", Case.Sensitive);
            output.ShouldContain("path:", Case.Sensitive);
            output.ShouldContain("->", Case.Sensitive);
        }
    }

    // ── AC-7: no flag produces unchanged legacy text output ───────────────────────

    [Fact]
    public async Task emits_text_format_with_legacy_cycle_paths()
    {
        using var proj = BuildCycleProject();
        var (workspace, sln) = proj.Build();
        using (workspace)
        {
            var legacyText = await _services.Resolve<CircularDepsCommand>().AnalyzeAsync(sln, null);

            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(sln, null);
            var result = CircularDepsResult.FromCycles(cycles);

            var sw = new StringWriter();
            RtContext.Format = OutputFormat.Text;
            RtSerializer.Write(result, sw);
            var output = sw.ToString();

            output.ShouldBe(legacyText + Environment.NewLine);
        }
    }

    // ── AC-8: RtCommandInfo.SupportedFormats includes all four formats ─────────────

    [Fact]
    public void reports_command_info_with_all_four_supported_formats()
    {
        var info = CircularDepsCommand.Info;
        var formats = info.SupportedFormats;
        formats.ShouldNotBeNull();
        formats.ShouldContain(OutputFormat.Text);
        formats.ShouldContain(OutputFormat.Json);
        formats.ShouldContain(OutputFormat.Markdown);
        formats.ShouldContain(OutputFormat.Llm);
    }
}
