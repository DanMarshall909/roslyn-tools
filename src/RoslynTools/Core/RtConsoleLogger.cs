using Microsoft.Extensions.Logging;

namespace RoslynTools.Core;

internal sealed class RtConsoleLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull =>
        NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (exception is not null)
            message += Environment.NewLine + exception;

        RtConsole.Log(ToRtLogLevel(logLevel), "{Message}", message);
    }

    private static RtLogLevel ToRtLogLevel(LogLevel level) =>
        level switch
        {
            LogLevel.Warning => RtLogLevel.Warning,
            LogLevel.Error or LogLevel.Critical => RtLogLevel.Error,
            _ => RtLogLevel.Info,
        };

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
    }
}
