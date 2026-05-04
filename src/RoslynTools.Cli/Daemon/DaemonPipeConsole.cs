using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Daemon;

internal sealed class DaemonPipeConsole(StreamWriter writer, string requestId) : IRtConsole
{
    public bool IsOutputRedirected => true;

    public void Log(RtLogLevel level, string template, params object?[] args)
        => Send("stderr", RtConsole.RenderTemplate(template, args) + "\n");

    public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        => Send(context == RtOutputContext.Diagnostic ? "stderr" : "stdout", message + "\n");

    public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        => Send(context == RtOutputContext.Diagnostic ? "stderr" : "stdout", message);

    public string? ReadLine() => null;

    private void Send(string type, string data)
        => writer.WriteLine(JsonSerializer.Serialize(
            new DaemonResponse(requestId, type, Data: data),
            DaemonProtocol.JsonOpts));
}
