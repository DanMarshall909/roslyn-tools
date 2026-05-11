namespace RoslynTools.Core;

/// <summary>
///     Convenience adapter for diagnostic logging. Delegates to <see cref="RtConsole.Log" />.
/// </summary>
internal static class RtLog
{
    /// <summary>
    /// Logs an informational message with the provided parameters.
    /// </summary>
    internal static void Info(string template, params object?[] args)
    {
        RtConsole.Log(RtLogLevel.Info, template, args);
    }

    /// <summary>
    /// Logs a warning message with the provided parameters.
    /// </summary>
    internal static void Warning(string template, params object?[] args)
    {
        RtConsole.Log(RtLogLevel.Warning, template, args);
    }

    /// <summary>
    /// Logs an error message with the provided parameters.
    /// </summary>
    internal static void Error(string template, params object?[] args)
    {
        RtConsole.Log(RtLogLevel.Error, template, args);
    }

    /// <summary>
    /// Configures the logger to use the specified logger provider.
    /// </summary>
    internal static IDisposable UseLogger(IRtLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return RtConsole.UseConsole(new LegacyLoggerAdapter(logger));
    }

    private sealed class LegacyLoggerAdapter(IRtLogger inner) : IRtConsole
    {
        public void Log(RtLogLevel level, string template, object?[] args)
        {
            var message = RtConsole.RenderTemplate(template, args);
            inner.Log(level, message);
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        {
            RtConsole.DefaultConsole.WriteLine(context, message, color);
        }

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        {
            RtConsole.DefaultConsole.Write(context, message, color);
        }

        public string? ReadLine()
        {
            return RtConsole.DefaultConsole.ReadLine();
        }

        public bool IsOutputRedirected => RtConsole.DefaultConsole.IsOutputRedirected;
    }
}
