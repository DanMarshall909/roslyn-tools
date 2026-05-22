using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests;

/// <summary>
///     Integration tests for US-210: global --json/--llm/--markdown flags set RtContext.Format
///     via CommandLineBuilder middleware before any command handler runs.
///     We invoke "dump-help" (a real subcommand) so the full middleware pipeline executes and we
///     can observe RtContext.Format side-effects after App.RunAsync returns.
/// </summary>
public sealed class GlobalFormatFlag : IDisposable
{
    // Reset RtContext after each test so static state doesn't bleed between tests.
    public void Dispose()
    {
        RtContext.Format = OutputFormat.Text;
    }

    // ── AC-8: no flag → Text ───────────────────────────────────────────────────────

    [Fact]
    public async Task reports_no_format_flag_leaves_format_as_text()
    {
        RtContext.Format = OutputFormat.Json; // dirty state — middleware must reset it

        await App.RunAsync(["dump-help"]);

        RtContext.Format.ShouldBe(OutputFormat.Text);
    }

    // ── AC-6: --json sets Format to Json ──────────────────────────────────────────

    [Fact]
    public async Task emits_json_flag_sets_format_to_json()
    {
        await App.RunAsync(["dump-help", "--json"]);

        RtContext.Format.ShouldBe(OutputFormat.Json);
    }

    // ── --llm sets Format to Llm ──────────────────────────────────────────────────

    [Fact]
    public async Task emits_llm_flag_sets_format_to_llm()
    {
        await App.RunAsync(["dump-help", "--llm"]);

        RtContext.Format.ShouldBe(OutputFormat.Llm);
    }

    // ── --markdown sets Format to Markdown ────────────────────────────────────────

    [Fact]
    public async Task emits_markdown_flag_sets_format_to_markdown()
    {
        await App.RunAsync(["dump-help", "--markdown"]);

        RtContext.Format.ShouldBe(OutputFormat.Markdown);
    }

    // ── --yaml sets Format to Yaml ────────────────────────────────────────────────

    [Fact]
    public async Task emits_yaml_flag_sets_format_to_yaml()
    {
        await App.RunAsync(["dump-help", "--yaml"]);

        RtContext.Format.ShouldBe(OutputFormat.Yaml);
    }

    [Fact]
    public async Task emits_markdown_takes_precedence_over_yaml_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--markdown", "--yaml"]);

        RtContext.Format.ShouldBe(OutputFormat.Markdown);
    }

    [Fact]
    public async Task emits_yaml_takes_precedence_over_json_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--yaml", "--json"]);

        RtContext.Format.ShouldBe(OutputFormat.Yaml);
    }

    // ── AC-7: --markdown wins over --json (precedence) ────────────────────────────

    [Fact]
    public async Task emits_markdown_takes_precedence_over_json_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--markdown", "--json"]);

        RtContext.Format.ShouldBe(OutputFormat.Markdown);
    }

    // ── markdown wins over llm ────────────────────────────────────────────────────

    [Fact]
    public async Task emits_markdown_takes_precedence_over_llm_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--markdown", "--llm"]);

        RtContext.Format.ShouldBe(OutputFormat.Markdown);
    }

    // ── json wins over llm ────────────────────────────────────────────────────────

    [Fact]
    public async Task emits_json_takes_precedence_over_llm_when_both_supplied()
    {
        await App.RunAsync(["dump-help", "--json", "--llm"]);

        RtContext.Format.ShouldBe(OutputFormat.Json);
    }
}
