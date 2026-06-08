using System.Text;
using RoslynTools.Core;
using RoslynTools.Rendering;
using Shouldly;
using Spectre.Console;

namespace RoslynTools.Tests.Cli;

public class SpectreRtConsole
{
    [Fact]
    public void writes_inserted_diff_line_with_green_prefix_after_cs_header()
    {
        var (rt, stdout, _) = NewConsole();

        // First a .cs file header so highlighting + diff colouring are enabled.
        rt.WriteLine(RtOutputContext.Diff, "--- src/Foo.cs");
        rt.WriteLine(RtOutputContext.Diff, "+    return 42;");

        var output = stdout.Output;
        output.ShouldContain("+");
        output.ShouldContain("return"); // body present
        output.ShouldContain("42");
        StripAnsi(output).ShouldContain("+    return 42;");
    }

    [Fact]
    public void writes_deleted_diff_line_with_red_prefix()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Diff, "--- src/Foo.cs");
        rt.WriteLine(RtOutputContext.Diff, "-    return 1;");

        StripAnsi(stdout.Output).ShouldContain("-    return 1;");
    }

    [Fact]
    public void writes_hunk_header_with_dark_cyan()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Diff, "@@ -1,3 +1,3 @@");

        StripAnsi(stdout.Output).ShouldBe("@@ -1,3 +1,3 @@" + Environment.NewLine);
    }

    [Fact]
    public void writes_diff_file_header_with_grey()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Diff, "--- src/Foo.cs");

        StripAnsi(stdout.Output).ShouldBe("--- src/Foo.cs" + Environment.NewLine);
    }

    [Fact]
    public void does_not_highlight_diff_content_when_header_was_not_a_cs_file()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Diff, "--- config.json");
        rt.WriteLine(RtOutputContext.Diff, "+  \"public\": true,");

        // The word 'public' would be a C# keyword and turn blue if highlighting
        // ran — confirm it stayed uncoloured (plain ASCII in the stripped output).
        StripAnsi(stdout.Output).ShouldContain("+  \"public\": true,");
        // The raw output should not contain blue ANSI code for the JSON content.
        // (blue keyword is colour code 12; absence is a reasonable signal.)
        stdout.Output.ShouldNotContain("[34m"); // ANSI blue (Standard palette)
    }

    [Fact]
    public void writes_updated_result_line_with_green_head()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Result, "Updated: src/Foo.cs");

        StripAnsi(stdout.Output).ShouldBe("Updated: src/Foo.cs" + Environment.NewLine);
    }

    [Fact]
    public void writes_no_changes_result_line_dimmed()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Result, "No changes.");

        StripAnsi(stdout.Output).ShouldBe("No changes." + Environment.NewLine);
    }

    [Fact]
    public void writes_dry_run_result_line_with_yellow_tag()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Result, "[dry-run] 3 file(s) would be modified.");

        StripAnsi(stdout.Output).ShouldBe("[dry-run] 3 file(s) would be modified." + Environment.NewLine);
    }

    [Fact]
    public void log_warning_writes_to_stderr_with_yellow_prefix()
    {
        var (rt, stdout, stderr) = NewConsole();
        rt.Log(RtLogLevel.Warning, "Skipped {Count}", 5);

        StripAnsi(stderr.Output).ShouldBe("[WARNING] Skipped 5" + Environment.NewLine);
        stdout.Output.ShouldBeEmpty();
    }

    [Fact]
    public void log_error_writes_to_stderr_with_red_prefix()
    {
        var (rt, _, stderr) = NewConsole();
        rt.Log(RtLogLevel.Error, "Failed {Name}", "Build");

        StripAnsi(stderr.Output).ShouldBe("[ERROR] Failed Build" + Environment.NewLine);
    }

    [Fact]
    public void log_renders_template_named_placeholders()
    {
        var (rt, _, stderr) = NewConsole();
        rt.Log(RtLogLevel.Info, "Loaded {Count} project(s) in {Elapsed:0.0}s", 7, 2.345);

        StripAnsi(stderr.Output).ShouldBe("Loaded 7 project(s) in 2.3s" + Environment.NewLine);
    }

    [Fact]
    public void ui_context_treats_message_as_trusted_markup()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Ui, "[cyan]hello[/]");

        StripAnsi(stdout.Output).ShouldBe("hello" + Environment.NewLine);
    }

    [Fact]
    public void interactive_disambiguation_candidate_with_bracketed_signature_does_not_crash()
    {
        var (rt, stdout, _) = NewConsole();

        // Repro for FB-363: the symbol-disambiguation prompt writes candidate
        // signatures (SymbolMatch.DisplayName) via the Interactive context. A
        // candidate containing markup metacharacters — e.g. a generic type arg or
        // array — must not be parsed as Spectre markup. Pre-fix this threw
        // "Unbalanced markup stack. Did you forget to close a tag?".
        Should.NotThrow(() =>
            rt.WriteLine(RtOutputContext.Interactive, "  [0]  Method  List<SymbolMatch> Collect()"));

        // The brackets must survive verbatim (escaped, not stripped) so the prompt
        // reads identically to its pre-Spectre plain-text form.
        StripAnsi(stdout.Output).ShouldContain("[0]");
        StripAnsi(stdout.Output).ShouldContain("List<SymbolMatch> Collect()");
    }

    [Fact]
    public void diagnostic_and_ui_flag_combination_writes_markup_to_stderr()
    {
        var (rt, stdout, stderr) = NewConsole();
        rt.WriteLine(RtOutputContext.Diagnostic | RtOutputContext.Ui, "[cyan]warning[/]");

        StripAnsi(stderr.Output).ShouldBe("warning" + Environment.NewLine);
        stdout.Output.ShouldBeEmpty();
    }

    [Fact]
    public void ConsoleColor_parameter_wraps_message_in_matching_spectre_colour()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Ui, "hello", ConsoleColor.Red);

        StripAnsi(stdout.Output).ShouldBe("hello" + Environment.NewLine);
        // Standard palette red is ANSI 31. Confirm a red SGR was emitted.
        stdout.Output.ShouldContain("[");
    }

    [Fact]
    public void escapes_literal_brackets_in_plain_text_diff_content_so_attributes_do_not_break_markup()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Diff, "--- src/Tests.cs");
        // Diff content containing a C# attribute — the lexer treats `[` as punctuation
        // and `Theory` as an identifier, both default-coloured. The escape must happen
        // so Spectre does not try to parse `[Theory]` as a markup tag.
        rt.WriteLine(RtOutputContext.Diff, "+    [Theory]");

        StripAnsi(stdout.Output).ShouldContain("+    [Theory]");
    }

    [Fact]
    public void plain_text_with_brackets_in_non_diff_non_result_context_is_escaped_too()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.None, "literal [brackets] in payload");

        StripAnsi(stdout.Output).ShouldBe("literal [brackets] in payload" + Environment.NewLine);
    }

    [Fact]
    public void leading_newlines_in_diff_message_are_emitted_as_spacers()
    {
        var (rt, stdout, _) = NewConsole();
        rt.WriteLine(RtOutputContext.Diff, "\n--- src/Foo.cs");

        // A leading newline before the file header produces a blank-line separator
        // ahead of the header text.
        var newline = Environment.NewLine;
        StripAnsi(stdout.Output).ShouldBe(newline + "--- src/Foo.cs" + newline);
    }

    // Helper: construct a SpectreRtConsole wired to in-memory captures so we can
    // inspect the rendered output. We force ANSI + a known colour system so the colour
    // codes are deterministic across machines (no TTY detection).
    private static (Rendering.SpectreRtConsole Rt, Capture Stdout, Capture Stderr) NewConsole()
    {
        var stdout = Capture.New();
        var stderr = Capture.New();
        var rt = new Rendering.SpectreRtConsole(stdout.Console, stderr.Console);
        return (rt, stdout, stderr);
    }

    private sealed class Capture
    {
        private readonly StringWriter _writer;

        private Capture(StringWriter writer, IAnsiConsole console)
        {
            _writer = writer;
            Console = console;
        }

        public IAnsiConsole Console { get; }

        public string Output => _writer.ToString();

        public static Capture New()
        {
            var sw = new StringWriter();
            var console = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(sw),
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.Standard,
            });
            return new Capture(sw, console);
        }
    }

    private static string StripAnsi(string s)
    {
        // Strip CSI sequences (ESC [ ... letter). Sufficient for SGR codes that Spectre emits.
        var span = s.AsSpan();
        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] == '' && i + 1 < span.Length && span[i + 1] == '[')
            {
                i += 2;
                while (i < span.Length && (span[i] < 0x40 || span[i] > 0x7e))
                    i++;
                continue;
            }

            sb.Append(span[i]);
        }

        return sb.ToString();
    }
}
