using RoslynTools.Core;
using Spectre.Console;

namespace RoslynTools.Rendering;

/// <summary>
/// CLI-side <see cref="IRtConsole"/> implementation that delegates rendering to Spectre.Console.
/// Inspects <see cref="RtOutputContext"/> + message content to add colour and C# syntax
/// highlighting at the host boundary. Core continues to emit plain text.
/// </summary>
internal sealed class SpectreRtConsole : IRtConsole
{
    private readonly IAnsiConsole _stdout;
    private readonly IAnsiConsole _stderr;
    private bool _highlightCSharp;

    public SpectreRtConsole()
        : this(CreateDefault(System.Console.Out), CreateDefault(System.Console.Error))
    {
    }

    // Constructor for tests that need to capture/inspect the rendered output. Production
    // code goes through the parameterless constructor above.
    internal SpectreRtConsole(IAnsiConsole stdout, IAnsiConsole stderr)
    {
        _stdout = stdout;
        _stderr = stderr;
    }

    private static IAnsiConsole CreateDefault(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(writer),
            Ansi = AnsiSupport.Detect,
            ColorSystem = ColorSystemSupport.Detect,
        });
        // Disable Spectre's hard line wrapping. Terminals handle soft wrap themselves
        // and programmatic consumers (subprocess stdout/stderr capture, log scrapers)
        // would otherwise see long paths or diagnostic messages broken by inserted \n.
        console.Profile.Width = int.MaxValue;
        return console;
    }

    public bool IsOutputRedirected => System.Console.IsOutputRedirected;

    public TextWriter Out => System.Console.Out;

    public string? ReadLine() => System.Console.ReadLine();

    public void Log(RtLogLevel level, string template, params object?[] args)
    {
        var body = Markup.Escape(RtConsole.RenderTemplate(template, args));
        var prefix = level switch
        {
            RtLogLevel.Warning => "[yellow][[WARNING]][/] ",
            RtLogLevel.Error => "[red][[ERROR]][/] ",
            _ => string.Empty,
        };
        _stderr.MarkupLine($"{prefix}{body}");
    }

    public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        => Render(context, message, color, newline: true);

    public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        => Render(context, message, color, newline: false);

    private void Render(RtOutputContext context, string message, ConsoleColor? color, bool newline)
    {
        var target = context.HasFlag(RtOutputContext.Diagnostic) ? _stderr : _stdout;

        if (color.HasValue)
        {
            EmitColoured(target, message, color.Value, newline);
            return;
        }

        // Flag-based routing: `Diagnostic | Ui` writes trusted markup to stderr,
        // letting App.cs colour its unknown-command hits without splitting streams.
        if (context.HasFlag(RtOutputContext.Diff))
        {
            RenderDiffLine(message, newline);
            return;
        }

        if (context.HasFlag(RtOutputContext.Ui) || context.HasFlag(RtOutputContext.Interactive))
        {
            EmitMarkupTrusted(target, message, newline);
            return;
        }

        if (context.HasFlag(RtOutputContext.Result))
        {
            RenderResultLine(message, newline);
            return;
        }

        EmitPlain(target, message, newline);
    }

    private void RenderDiffLine(string message, bool newline)
    {
        var (leading, body) = SplitLeadingNewlines(message);
        if (leading.Length > 0)
            _stdout.Write(leading);

        if (body.Length == 0)
        {
            if (newline)
                _stdout.WriteLine();
            return;
        }

        if (StartsWith(body, "@@ "))
        {
            EmitWrappedLine("darkcyan", body, newline);
            return;
        }

        if (StartsWith(body, "--- ") || StartsWith(body, "+++ "))
        {
            UpdateHighlightStateFromHeader(body);
            EmitWrappedLine("grey", body, newline);
            return;
        }

        if (body[0] == '+')
        {
            EmitDiffContentLine("green", '+', body[1..], newline);
            return;
        }

        if (body[0] == '-')
        {
            EmitDiffContentLine("red", '-', body[1..], newline);
            return;
        }

        if (body[0] == ' ')
        {
            EmitDiffContentLine(prefixColour: null, prefixChar: ' ', body[1..], newline);
            return;
        }

        EmitPlain(_stdout, body, newline);
    }

    private void RenderResultLine(string message, bool newline)
    {
        if (StartsWith(message, "Updated: "))
        {
            EmitColouredHead("green", "Updated:", message["Updated:".Length..], newline);
            return;
        }

        if (StartsWith(message, "Created: "))
        {
            EmitColouredHead("green", "Created:", message["Created:".Length..], newline);
            return;
        }

        if (StartsWith(message, "Done. "))
        {
            EmitColouredHead("green", "Done.", message["Done.".Length..], newline);
            return;
        }

        // SolutionWriter emits "\nDone. N file(s) updated." — handle a leading newline.
        if (StartsWith(message, "\nDone. "))
        {
            _stdout.WriteLine();
            EmitColouredHead("green", "Done.", message["\nDone.".Length..], newline);
            return;
        }

        if (message == "No changes." || StartsWith(message, "No changes."))
        {
            EmitWrappedLine("grey", message, newline);
            return;
        }

        if (StartsWith(message, "[dry-run] "))
        {
            var tail = message["[dry-run]".Length..];
            var styled = $"[yellow][[dry-run]][/]{Markup.Escape(tail)}";
            if (newline) _stdout.MarkupLine(styled); else _stdout.Markup(styled);
            return;
        }

        if (StartsWith(message, "\n[dry-run] "))
        {
            _stdout.WriteLine();
            var tail = message["\n[dry-run]".Length..];
            var styled = $"[yellow][[dry-run]][/]{Markup.Escape(tail)}";
            if (newline) _stdout.MarkupLine(styled); else _stdout.Markup(styled);
            return;
        }

        EmitPlain(_stdout, message, newline);
    }

    private void EmitDiffContentLine(string? prefixColour, char prefixChar, string content, bool newline)
    {
        var contentMarkup = _highlightCSharp ? CSharpHighlighter.ToMarkup(content) : Markup.Escape(content);
        var prefixMarkup = prefixColour is null
            ? prefixChar.ToString()
            : $"[{prefixColour}]{prefixChar}[/]";

        var line = prefixMarkup + contentMarkup;
        if (newline) _stdout.MarkupLine(line); else _stdout.Markup(line);
    }

    private void EmitColoured(IAnsiConsole console, string message, ConsoleColor color, bool newline)
    {
        var safe = Markup.Escape(message);
        var name = MapConsoleColor(color);
        var styled = $"[{name}]{safe}[/]";
        if (newline) console.MarkupLine(styled); else console.Markup(styled);
    }

    private static void EmitMarkupTrusted(IAnsiConsole console, string message, bool newline)
    {
        if (newline) console.MarkupLine(message); else console.Markup(message);
    }

    private static void EmitPlain(IAnsiConsole console, string message, bool newline)
    {
        var safe = Markup.Escape(message);
        if (newline) console.MarkupLine(safe); else console.Markup(safe);
    }

    private void EmitWrappedLine(string colorName, string message, bool newline)
    {
        var safe = Markup.Escape(message);
        var styled = $"[{colorName}]{safe}[/]";
        if (newline) _stdout.MarkupLine(styled); else _stdout.Markup(styled);
    }

    private void EmitColouredHead(string colorName, string head, string tail, bool newline)
    {
        var styled = $"[{colorName}]{Markup.Escape(head)}[/]{Markup.Escape(tail)}";
        if (newline) _stdout.MarkupLine(styled); else _stdout.Markup(styled);
    }

    private void UpdateHighlightStateFromHeader(string headerLine)
    {
        // Forms: "--- {path}" / "+++ {path}" / "+++ {path} (new file)"
        var spaceIdx = headerLine.IndexOf(' ');
        if (spaceIdx < 0 || spaceIdx >= headerLine.Length - 1)
        {
            _highlightCSharp = false;
            return;
        }

        var pathPart = headerLine[(spaceIdx + 1)..];
        var suffixIdx = pathPart.IndexOf(" (", StringComparison.Ordinal);
        if (suffixIdx >= 0)
            pathPart = pathPart[..suffixIdx];

        _highlightCSharp = pathPart.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
    }

    private static (string Leading, string Body) SplitLeadingNewlines(string message)
    {
        var i = 0;
        while (i < message.Length && message[i] == '\n')
            i++;

        return i == 0 ? (string.Empty, message) : (message[..i], message[i..]);
    }

    private static bool StartsWith(string text, string prefix)
        => text.Length >= prefix.Length && text.AsSpan(0, prefix.Length).SequenceEqual(prefix);

    private static string MapConsoleColor(ConsoleColor color) => color switch
    {
        ConsoleColor.Black => "black",
        ConsoleColor.DarkBlue => "blue3",
        ConsoleColor.DarkGreen => "green4",
        ConsoleColor.DarkCyan => "darkcyan",
        ConsoleColor.DarkRed => "darkred",
        ConsoleColor.DarkMagenta => "purple3",
        ConsoleColor.DarkYellow => "orange3",
        ConsoleColor.Gray => "silver",
        ConsoleColor.DarkGray => "grey",
        ConsoleColor.Blue => "blue",
        ConsoleColor.Green => "green",
        ConsoleColor.Cyan => "cyan1",
        ConsoleColor.Red => "red",
        ConsoleColor.Magenta => "magenta1",
        ConsoleColor.Yellow => "yellow",
        ConsoleColor.White => "white",
        _ => "default",
    };
}
