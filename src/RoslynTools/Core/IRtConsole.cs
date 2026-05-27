namespace RoslynTools.Core;

/// <summary>
/// An interface for a console output system.
/// </summary>
internal interface IRtConsole
{
    bool IsOutputRedirected { get; }

    bool IsInputRedirected => Console.IsInputRedirected;

    TextReader In => Console.In;

    TextWriter Out => Console.Out;
    void Log(RtLogLevel level, string template, params object?[] args);

    void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null);

    void Write(RtOutputContext context, string message, ConsoleColor? color = null);

    string? ReadLine();
}
