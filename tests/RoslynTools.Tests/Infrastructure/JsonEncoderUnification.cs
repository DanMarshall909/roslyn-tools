using System.Text.Json;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     US-135 Batch 1 polish: every JSON output path (CLI StructuredOutput, IRtOutput
///     JsonRenderer, native MCP handlers) must converge on a single encoder helper —
///     <see cref="JsonEncoders.Select" /> — so the global <c>--relax-json</c> flag
///     toggles all of them coherently. Default behaviour is strict (non-ASCII as
///     <c>\uXXXX</c>) for downstream deserialiser portability; <c>--relax-json</c>
///     opts into <see cref="System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping" />.
///     MCP-side equivalents live in <c>RoslynTools.Tests.Mcp</c>.
/// </summary>
public class JsonEncoderUnification
{
    [Fact]
    public void helper_default_escapes_non_ascii_and_html_sensitive_chars()
    {
        // Default (relaxJson = false) === JavaScriptEncoder.Default semantics.
        // The arrow becomes → and the less-than sign becomes <.
        var opts = JsonEncoders.Select(relaxJson: false);
        var json = JsonSerializer.Serialize(new { s = "a→b<c" }, opts);

        json.ShouldContain("\\u2192");
        json.ShouldContain("\\u003C");
        json.ShouldNotContain("→");
    }

    [Fact]
    public void helper_relaxed_emits_raw_unicode_and_html_chars()
    {
        // relaxJson = true === JavaScriptEncoder.UnsafeRelaxedJsonEscaping semantics.
        var opts = JsonEncoders.Select(relaxJson: true);
        var json = JsonSerializer.Serialize(new { s = "a→b<c" }, opts);

        json.ShouldContain("→");
        json.ShouldContain("<");
        json.ShouldNotContain("\\u2192");
        json.ShouldNotContain("\\u003C");
    }

    [Fact]
    public async Task cli_describe_default_escapes_html_chars_in_signatures()
    {
        // Test A — current strict default preserved across the CLI wiring: generic
        // signatures like Task<...> emit <, not raw <.
        var slnxPath = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");

        var result = await RtCliHost.RunAsync(
            $"inspect describe RoslynTools.Core.SymbolLocator --solution \"{slnxPath}\"");

        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldContain("\\u003C");
    }

    [Fact]
    public async Task cli_describe_with_relax_json_emits_raw_html_chars()
    {
        // Test B — --relax-json opts out of escaping.
        var slnxPath = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");

        var result = await RtCliHost.RunAsync(
            $"inspect describe RoslynTools.Core.SymbolLocator --solution \"{slnxPath}\" --relax-json");

        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldContain("Task<");
        result.Stdout.ShouldNotContain("\\u003C");
    }

}
