using RoslynTools;
using RoslynTools.Core.Output;

namespace RoslynTools.Tests;

/// <summary>
/// Integration tests for US-210: global --json/--llm/--markdown flags set RtContext.Format
/// via CommandLineBuilder middleware before any command handler runs.
///
/// We invoke "dump-help" (a real subcommand) so the full middleware pipeline executes and we
/// can observe RtContext.Format side-effects after App.RunAsync returns.
/// </summary>
public sealed class GlobalFormatFlagTests : IDisposable
{
    // Reset RtContext after each test so static state doesn't bleed between tests.
    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
    }

    // ── AC-8: no flag → Text ───────────────────────────────────────────────────────

    [Fact]
    public async Task No_format_flag_leaves_Format_as_Text()
    {
        RtContext.Format = OutputFormat.Json; // dirty state — middleware must reset it

        await App.RunAsync(["dump-help"]);

        Assert.Equal(OutputFormat.Text, RtContext.Format);
    }

    // ── AC-6: --json sets Format to Json ──────────────────────────────────────────

    [Fact]
    public async Task Json_flag_sets_Format_to_Json()
    {
        await App.RunAsync(["dump-help", "--json"]);

        Assert.Equal(OutputFormat.Json, RtContext.Format);
    }

    // ── --llm sets Format to Llm ──────────────────────────────────────────────────

    [Fact]
    public async Task Llm_flag_sets_Format_to_Llm()
    {
        await App.RunAsync(["dump-help", "--llm"]);

        Assert.Equal(OutputFormat.Llm, RtContext.Format);
    }

    // ── --markdown sets Format to Markdown ────────────────────────────────────────

    [Fact]
    public async Task Markdown_flag_sets_Format_to_Markdown()
    {
        await App.RunAsync(["dump-help", "--markdown"]);

        Assert.Equal(OutputFormat.Markdown, RtContext.Format);
    }

    // ── AC-7: --markdown wins over --json (precedence) ────────────────────────────

    [Fact]
    public async Task Markdown_takes_precedence_over_json_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--markdown", "--json"]);

        Assert.Equal(OutputFormat.Markdown, RtContext.Format);
    }

    // ── markdown wins over llm ────────────────────────────────────────────────────

    [Fact]
    public async Task Markdown_takes_precedence_over_llm_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--markdown", "--llm"]);

        Assert.Equal(OutputFormat.Markdown, RtContext.Format);
    }

    // ── json wins over llm ────────────────────────────────────────────────────────

    [Fact]
    public async Task Json_takes_precedence_over_llm_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--json", "--llm"]);

        Assert.Equal(OutputFormat.Json, RtContext.Format);
    }
}
