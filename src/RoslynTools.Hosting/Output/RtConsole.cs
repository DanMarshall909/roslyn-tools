using System.Text;

namespace RoslynTools.Core;

/// <summary>
///     Shared console abstraction for CLI output. Call-site migration is intentionally incremental:
///     existing direct Console usage can be moved here command by command.
/// </summary>
internal static class RtConsole
{
    private static readonly AsyncLocal<IRtConsole?> Override = new();

    internal static readonly IRtConsole DefaultConsole = new ConsoleRtConsole();

    private static IRtConsole Current => Override.Value ?? DefaultConsole;

    /// <summary>
    /// Reads input from the console.
    /// </summary>
    internal static TextReader In => Current.In;

    /// <summary>
    /// Checks if the console input is redirected (piped) rather than a TTY.
    /// </summary>
    internal static bool IsInputRedirected => Current.IsInputRedirected;

    /// <summary>
    /// Writes output to the console.
    /// </summary>
    internal static TextWriter Out => Current.Out;

    /// <summary>
    /// Checks if the console output is redirected.
    /// </summary>
    internal static bool IsOutputRedirected => Current.IsOutputRedirected;

    /// <summary>
    /// Registers a handler for cancel key presses.
    /// </summary>
    internal static void AddCancelKeyPressHandler(ConsoleCancelEventHandler handler)
    {
        Console.CancelKeyPress += handler;
    }

    /// <summary>
    /// Sets the output encoding for the console.
    /// </summary>
    internal static void SetOutputEncoding(Encoding encoding)
    {
        Console.OutputEncoding = encoding;
    }

    /// <summary>
    /// Logs a message with the specified log level.
    /// </summary>
    internal static void Log(RtLogLevel level, string template, params object?[] args)
    {
        Current.Log(level, template, args);
    }

    /// <summary>
    /// Writes a formatted string to the console with optional color.
    /// </summary>
    internal static void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
    {
        Current.WriteLine(context, message, color);
    }

    /// <summary>
    /// Writes a string to the console with optional color.
    /// </summary>
    internal static void Write(RtOutputContext context, string message, ConsoleColor? color = null)
    {
        Current.Write(context, message, color);
    }

    /// <summary>
    /// Configures the console for use with the specified provider.
    /// </summary>
    internal static IDisposable UseConsole(IRtConsole console)
    {
        ArgumentNullException.ThrowIfNull(console);

        var previous = Override.Value;
        Override.Value = console;
        return new Scope(previous);
    }

    /// <summary>
    /// Renders a template with the provided data.
    /// </summary>
    internal static string RenderTemplate(string template, object?[] args)
        => RtMessageTemplateFormatter.Render(template, args);

    private sealed class Scope(IRtConsole? previous) : IDisposable
    {
        public void Dispose()
        {
            Override.Value = previous;
        }
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
                _ => string.Empty
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

        public bool IsOutputRedirected => Console.IsOutputRedirected;

        private static TextWriter GetWriter(RtOutputContext context)
        {
            return context.HasFlag(RtOutputContext.Diagnostic) ? Console.Error : Console.Out;
        }
    }
}
