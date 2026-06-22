using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class RtConsole
{
    [Fact]
    public void handles_default_console_routes_diagnostics_to_stderr_and_results_to_stdout()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            Core.RtConsole.DefaultConsole.Log(RtLogLevel.Warning, "Skipped {Count} file(s)", 2);
            Core.RtConsole.DefaultConsole.Log(RtLogLevel.Error, "Failed {Name}", "Build");
            Core.RtConsole.DefaultConsole.WriteLine(RtOutputContext.Result, "result payload");
            Core.RtConsole.DefaultConsole.WriteLine(RtOutputContext.Diff, "--- file.cs");
            Core.RtConsole.DefaultConsole.WriteLine(RtOutputContext.Diagnostic, "diagnostic line");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        stderr.ToString().ShouldContain("[WARNING] Skipped 2 file(s)", Case.Sensitive);
        stderr.ToString().ShouldContain("[ERROR] Failed Build", Case.Sensitive);
        stderr.ToString().ShouldContain("diagnostic line", Case.Sensitive);
        stdout.ToString().ShouldContain("result payload", Case.Sensitive);
        stdout.ToString().ShouldContain("--- file.cs", Case.Sensitive);
        stdout.ToString().ShouldNotContain("[WARNING]", Case.Sensitive);
    }

    [Fact]
    public void restores_outer_console_after_inner_scope_disposes()
    {
        var outer = new CollectingConsole();
        var inner = new CollectingConsole();

        using (Core.RtConsole.UseConsole(outer))
        {
            RtLog.Info("outer before");

            using (Core.RtConsole.UseConsole(inner))
            {
                RtLog.Info("inner");
            }

            RtLog.Info("outer after");
        }

        outer.LogEntries.Select(e => e.RenderedMessage).ShouldBe(["outer before", "outer after"]);
        inner.LogEntries.Select(e => e.RenderedMessage).ShouldBe(["inner"]);
    }

    [Fact]
    public void supports_named_placeholders_and_invariant_formatting_in_render_template()
    {
        var rendered = Core.RtConsole.RenderTemplate(
            "Processed {Count} item(s) in {Elapsed:0.00} ms",
            [3, 12.345]);

        rendered.ShouldBe("Processed 3 item(s) in 12.35 ms");
    }

    [Fact]
    public void legacy_logger_adapter_receives_rendered_diagnostic_messages()
    {
        var logger = new CollectingLogger();

        using (RtLog.UseLogger(logger))
        {
            RtLog.Warning("Skipped {Name}", "Widget");
        }

        var entry = Assert.Single(logger.LogEntries);
        entry.Level.ShouldBe(RtLogLevel.Warning);
        entry.Message.ShouldBe("Skipped Widget");
    }

    [Fact]
    public void routes_input_reader_and_redirect_flag_through_overridden_console()
    {
        var fake = new PipedInputConsole("piped payload");

        using (Core.RtConsole.UseConsole(fake))
        {
            Core.RtConsole.IsInputRedirected.ShouldBeTrue();
            Core.RtConsole.In.ReadToEnd().ShouldBe("piped payload");
        }
    }

    private sealed class PipedInputConsole(string stdin) : IRtConsole
    {
        public TextReader In { get; } = new StringReader(stdin);
        public bool IsInputRedirected => true;
        public bool IsOutputRedirected => false;
        public void Log(RtLogLevel level, string template, object?[] args) { }
        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null) { }
        public void Write(RtOutputContext context, string message, ConsoleColor? color = null) { }
    }

    private sealed class CollectingConsole : IRtConsole
    {
        public List<LogEntry> LogEntries { get; } = [];

        public void Log(RtLogLevel level, string template, object?[] args)
        {
            var rendered = Core.RtConsole.RenderTemplate(template, args);
            LogEntries.Add(new LogEntry(level, rendered));
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        {
        }

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        {
        }

        public bool IsOutputRedirected => false;

        public sealed record LogEntry(RtLogLevel Level, string RenderedMessage);
    }

    private sealed class CollectingLogger : IRtLogger
    {
        public List<LogEntry> LogEntries { get; } = [];

        public void Log(RtLogLevel level, string message)
        {
            LogEntries.Add(new LogEntry(level, message));
        }

        public sealed record LogEntry(RtLogLevel Level, string Message);
    }
}
