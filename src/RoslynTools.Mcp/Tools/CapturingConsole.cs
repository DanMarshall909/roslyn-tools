using System.Text;
using RoslynTools.Core;

namespace RoslynTools.Mcp.Tools;

internal sealed class CapturingConsole : IRtConsole, IDisposable
{
    private readonly StringWriter _out;

    public CapturingConsole(StringBuilder capture)
    {
        _out = new StringWriter(capture);
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
        if (!context.HasFlag(RtOutputContext.Diagnostic))
            _out.WriteLine(message);
    }

    public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
    {
        if (!context.HasFlag(RtOutputContext.Diagnostic))
            _out.Write(message);
    }

    public string? ReadLine()
    {
        return null;
    }
}
