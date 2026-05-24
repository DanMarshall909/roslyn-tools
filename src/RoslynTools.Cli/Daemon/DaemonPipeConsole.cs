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
internal sealed class DaemonPipeConsole(StreamWriter writer, string requestId) : IRtConsole
{
    private readonly StringBuilder _scratch = new();

    public bool IsOutputRedirected => true;

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
        var trustsMarkup = context.HasFlag(RtOutputContext.Ui) || context.HasFlag(RtOutputContext.Interactive);
        if (!trustsMarkup)
            return newline ? message + "\n" : message;

        // For Ui/Interactive payloads we need Spectre to PARSE the markup tags
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
}
