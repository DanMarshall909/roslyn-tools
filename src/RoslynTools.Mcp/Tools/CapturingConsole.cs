using System.Text;
using RoslynTools.Core;
using Spectre.Console;

namespace RoslynTools.Mcp.Tools;

/// <summary>
/// Class for capturing console output in the Roslyn MCP system. Routes every write through
/// a Spectre.Console <see cref="IAnsiConsole"/> configured with <see cref="ColorSystemSupport.NoColors"/>
/// so any markup that CLI commands embed (e.g. <c>[green]+[/]</c>) is parsed and stripped to plain text.
/// </summary>
internal sealed class CapturingConsole : IRtConsole, IDisposable
{
    private readonly StringWriter _out;
    private readonly IAnsiConsole _console;

    public CapturingConsole(StringBuilder capture)
    {
        _out = new StringWriter(capture);
        _console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(_out),
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
        });
        // MCP consumers parse the captured text (JSON, multi-line summaries) so do not
        // let Spectre wrap markup payloads at its default 80-column profile width.
        _console.Profile.Width = int.MaxValue;
    }

    public void Dispose()
    {
        _out.Dispose();
    }

    public TextWriter Out => _out;
    public bool IsOutputRedirected => true;

    public void Log(RtLogLevel level, string template, params object?[] args)
    {
    }

    public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
    {
        if (context.HasFlag(RtOutputContext.Diagnostic))
            return;

        if (CarriesMarkup(context))
            _console.MarkupLine(message);
        else
            _out.WriteLine(message);
    }

    public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
    {
        if (context.HasFlag(RtOutputContext.Diagnostic))
            return;

        if (CarriesMarkup(context))
            _console.Markup(message);
        else
            _out.Write(message);
    }

    private static bool CarriesMarkup(RtOutputContext context)
        => context.HasFlag(RtOutputContext.Ui) || context.HasFlag(RtOutputContext.Interactive);

    public string? ReadLine()
    {
        return null;
    }
}
