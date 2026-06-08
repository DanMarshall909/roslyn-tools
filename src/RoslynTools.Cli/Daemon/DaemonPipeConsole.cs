using System.IO;
using System.Text;
using System.Text.Json;
using RoslynTools.Core;
using Spectre.Console;

namespace RoslynTools.Daemon;

/// <summary>
/// A console for daemon pipe communication. Markup-bearing messages from CLI-side commands
/// (Ui/Interactive context) are rendered through Spectre's NoColors profile and stripped to
/// plain text before being sent over the pipe; plain text from other contexts is escaped to
/// avoid accidental tag parsing.
/// </summary>
internal sealed class DaemonPipeConsole(StreamWriter writer, string requestId, string? stdin = null) : IRtConsole
{
    private readonly StringBuilder _scratch = new();

    public bool IsOutputRedirected => true;

    public bool IsInputRedirected { get; } = stdin is not null;

    public TextReader In { get; } = stdin is null ? TextReader.Null : new StringReader(stdin);

    // Structured output (find-refs --json etc.) is written via RtConsole.Out / RtSerializer, not
    // Write/WriteLine. Route it over the pipe as raw "stdout" — no markup stripping, since the
    // payload is JSON/data, not Spectre markup.
    private TextWriter? _out;
    public TextWriter Out => _out ??= new PipeTextWriter(this);

    public void Log(RtLogLevel level, string template, params object?[] args)
    {
        Send("stderr", RtConsole.RenderTemplate(template, args) + "\n");
    }

    public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
    {
        var stream = context.HasFlag(RtOutputContext.Diagnostic) ? "stderr" : "stdout";
        Send(stream, RenderToPlainText(context, message, newline: true));
    }

    public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
    {
        var stream = context.HasFlag(RtOutputContext.Diagnostic) ? "stderr" : "stdout";
        Send(stream, RenderToPlainText(context, message, newline: false));
    }

    public string? ReadLine()
    {
        return null;
    }

    private string RenderToPlainText(RtOutputContext context, string message, bool newline)
    {
        // Only `Ui` carries trusted host-authored markup. `Interactive` (the symbol
        // disambiguation prompt) emits untrusted candidate text whose brackets would
        // crash Spectre's markup parser, so it is treated as plain text (FB-363).
        var trustsMarkup = context.HasFlag(RtOutputContext.Ui);
        if (!trustsMarkup)
            return newline ? message + "\n" : message;

        // For Ui payloads we need Spectre to PARSE the markup tags
        // (`[cyan]name[/]` becomes `name`). Configure the console with an unconstrained
        // width so JSON or long lines are not wrapped at 80 cols.
        _scratch.Clear();
        using var writer = new StringWriter(_scratch);
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
        });
        console.Profile.Width = int.MaxValue;

        if (newline)
            console.MarkupLine(message);
        else
            console.Markup(message);

        return _scratch.ToString();
    }

    private void Send(string type, string data)
    {
        writer.WriteLine(JsonSerializer.Serialize(
            new DaemonResponse(requestId, type, data),
            DaemonProtocol.JsonOpts));
    }

    // Forwards every write straight to the pipe as a "stdout" response. The client concatenates
    // response.Data verbatim, so WriteLine appends the newline itself and Write emits none.
    private sealed class PipeTextWriter(DaemonPipeConsole owner) : TextWriter
    {
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(string? value)
        {
            if (!string.IsNullOrEmpty(value)) owner.Send("stdout", value);
        }

        public override void Write(char value)
        {
            owner.Send("stdout", value.ToString());
        }

        public override void WriteLine(string? value)
        {
            owner.Send("stdout", (value ?? string.Empty) + "\n");
        }
    }
}
