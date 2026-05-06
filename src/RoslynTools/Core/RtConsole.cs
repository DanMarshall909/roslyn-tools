using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RoslynTools.Core;

/// <summary>
/// Shared console abstraction for CLI output. Call-site migration is intentionally incremental:
/// existing direct Console usage can be moved here command by command.
/// </summary>
internal static class RtConsole
{
    private static readonly AsyncLocal<IRtConsole?> Override = new();

    internal static readonly IRtConsole DefaultConsole = new ConsoleRtConsole();

    private static IRtConsole Current => Override.Value ?? DefaultConsole;

    internal static TextReader In => Console.In;

    internal static TextWriter Out => Current.Out;

    internal static void AddCancelKeyPressHandler(ConsoleCancelEventHandler handler) =>
        Console.CancelKeyPress += handler;

    internal static void SetOutputEncoding(Encoding encoding) =>
        Console.OutputEncoding = encoding;

    internal static void Log(RtLogLevel level, string template, params object?[] args) =>
        Current.Log(level, template, args);

    internal static void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null) =>
        Current.WriteLine(context, message, color);

    internal static void Write(RtOutputContext context, string message, ConsoleColor? color = null) =>
        Current.Write(context, message, color);

    internal static string? ReadLine() => Current.ReadLine();

    internal static bool IsOutputRedirected => Current.IsOutputRedirected;

    internal static IDisposable UseConsole(IRtConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        var previous = Override.Value;
        Override.Value = console;
        return new Scope(previous);
    }

    internal static string RenderTemplate(string template, object?[] args)
    {
        if (args.Length == 0)
            return template;

        var index = 0;
        var positioned = Regex.Replace(
            template,
            @"\{([a-zA-Z_][a-zA-Z0-9_]*)(:[^{}]*)?\}",
            match => $"{{{index++}{match.Groups[2].Value}}}");

        return string.Format(CultureInfo.InvariantCulture, positioned, args);
    }

    private sealed class Scope(IRtConsole? previous) : IDisposable
    {
        public void Dispose() => Override.Value = previous;
    }

    private sealed class ConsoleRtConsole : IRtConsole
    {
        public void Log(RtLogLevel level, string template, object?[] args)
        {
            var formatted = RenderTemplate(template, args);
            var prefix = level switch
            {
                RtLogLevel.Warning => "[WARNING] ",
                RtLogLevel.Error => "[ERROR] ",
                _ => string.Empty,
            };

            Console.Error.WriteLine($"{prefix}{formatted}");
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        {
            var writer = GetWriter(context);
            if (color.HasValue)
                Console.ForegroundColor = color.Value;

            writer.WriteLine(message);

            if (color.HasValue)
                Console.ResetColor();
        }

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        {
            var writer = GetWriter(context);
            if (color.HasValue)
                Console.ForegroundColor = color.Value;

            writer.Write(message);

            if (color.HasValue)
                Console.ResetColor();
        }

        public string? ReadLine() => Console.ReadLine();

        public bool IsOutputRedirected => Console.IsOutputRedirected;

        private static TextWriter GetWriter(RtOutputContext context) =>
            context.HasFlag(RtOutputContext.Diagnostic) ? Console.Error : Console.Out;
    }
}
