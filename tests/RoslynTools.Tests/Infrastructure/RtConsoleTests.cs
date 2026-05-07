using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class RtConsoleTests
{
    [Fact]
    public void Default_console_routes_diagnostics_to_stderr_and_results_to_stdout()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            RtConsole.DefaultConsole.Log(RtLogLevel.Warning, "Skipped {Count} file(s)", 2);
            RtConsole.DefaultConsole.Log(RtLogLevel.Error, "Failed {Name}", "Build");
            RtConsole.DefaultConsole.WriteLine(RtOutputContext.Result, "result payload");
            RtConsole.DefaultConsole.WriteLine(RtOutputContext.Diff, "--- file.cs");
            RtConsole.DefaultConsole.WriteLine(RtOutputContext.Diagnostic, "diagnostic line");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        Assert.Contains("[WARNING] Skipped 2 file(s)", stderr.ToString());
        Assert.Contains("[ERROR] Failed Build", stderr.ToString());
        Assert.Contains("diagnostic line", stderr.ToString());
        Assert.Contains("result payload", stdout.ToString());
        Assert.Contains("--- file.cs", stdout.ToString());
        Assert.DoesNotContain("[WARNING]", stdout.ToString());
    }

    [Fact]
    public void UseConsole_restores_outer_console_after_inner_scope_disposes()
    {
        var outer = new CollectingConsole();
        var inner = new CollectingConsole();

        using (RtConsole.UseConsole(outer))
        {
            RtLog.Info("outer before");

            using (RtConsole.UseConsole(inner))
            {
                RtLog.Info("inner");
            }

            RtLog.Info("outer after");
        }

        Assert.Equal(["outer before", "outer after"], outer.LogEntries.Select(e => e.RenderedMessage));
        Assert.Equal(["inner"], inner.LogEntries.Select(e => e.RenderedMessage));
    }

    [Fact]
    public void RenderTemplate_supports_named_placeholders_and_invariant_formatting()
    {
        var rendered = RtConsole.RenderTemplate(
            "Processed {Count} item(s) in {Elapsed:0.00} ms",
            [3, 12.345]);

        Assert.Equal("Processed 3 item(s) in 12.35 ms", rendered);
    }

    [Fact]
    public void Legacy_logger_adapter_receives_rendered_diagnostic_messages()
    {
        var logger = new CollectingLogger();

        using (RtLog.UseLogger(logger))
        {
            RtLog.Warning("Skipped {Name}", "Widget");
        }

        var entry = Assert.Single(logger.LogEntries);
        Assert.Equal(RtLogLevel.Warning, entry.Level);
        Assert.Equal("Skipped Widget", entry.Message);
    }

    private sealed class CollectingConsole : IRtConsole
    {
        public List<LogEntry> LogEntries { get; } = [];

        public void Log(RtLogLevel level, string template, object?[] args)
        {
            var rendered = RtConsole.RenderTemplate(template, args);
            LogEntries.Add(new LogEntry(level, rendered));
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        {
        }

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        {
        }

        public string? ReadLine()
        {
            return null;
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
