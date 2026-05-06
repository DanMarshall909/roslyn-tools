namespace RoslynTools.Core;

internal interface IRtConsole
{
    void Log(RtLogLevel level, string template, params object?[] args);

    void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null);

    void Write(RtOutputContext context, string message, ConsoleColor? color = null);

    string? ReadLine();

    bool IsOutputRedirected { get; }

    TextWriter Out => Console.Out;
}
