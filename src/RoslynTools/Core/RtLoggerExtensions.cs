using Microsoft.Extensions.Logging;

namespace RoslynTools.Core;

internal static class RtLoggerExtensions
{
    private static readonly EventId DiagnosticEvent = new(0, "RtDiagnostic");

    public static void RtInfo(this ILogger logger, string template, params object?[] args)
    {
        Log(logger, LogLevel.Information, template, args);
    }

    public static void RtWarning(this ILogger logger, string template, params object?[] args)
    {
        Log(logger, LogLevel.Warning, template, args);
    }

    public static void RtError(this ILogger logger, string template, params object?[] args)
    {
        Log(logger, LogLevel.Error, template, args);
    }

    private static void Log(ILogger logger, LogLevel level, string template, object?[] args)
    {
        if (!logger.IsEnabled(level))
            return;

        logger.Log(
            level,
            DiagnosticEvent,
            new RtLogState(template, args),
            null,
            static (state, _) => RtConsole.RenderTemplate(state.Template, state.Args));
    }

    private readonly record struct RtLogState(string Template, object?[] Args);
}
