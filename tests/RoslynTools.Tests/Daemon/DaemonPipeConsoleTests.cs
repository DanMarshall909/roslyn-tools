using System.Text;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonPipeConsoleTests
{
    [Fact]
    public void out_writer_forwards_payload_as_stdout_response()
    {
        using var ms = new MemoryStream();
        using var sink = new StreamWriter(ms, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        IRtConsole console = new DaemonPipeConsole(sink, "req1");

        // find-refs --json renders via RtConsole.Out (RtSerializer), not Write/WriteLine.
        console.Out.Write("{\"fullyQualifiedName\":\"TestApp.Foo\"}");

        var sent = Encoding.UTF8.GetString(ms.ToArray());
        var response = JsonSerializer.Deserialize<DaemonResponse>(sent.Trim(), DaemonProtocol.JsonOpts);
        response.ShouldNotBeNull();
        response.Type.ShouldBe("stdout");
        response.Data.ShouldBe("{\"fullyQualifiedName\":\"TestApp.Foo\"}");
    }

    [Fact]
    public void exposes_forwarded_stdin_as_input_reader()
    {
        using var writer = new StreamWriter(Stream.Null);
        IRtConsole console = new DaemonPipeConsole(writer, "req1", "piped payload");

        console.IsInputRedirected.ShouldBeTrue();
        console.In.ReadToEnd().ShouldBe("piped payload");
    }

    [Fact]
    public void reports_no_redirected_input_when_stdin_absent()
    {
        using var writer = new StreamWriter(Stream.Null);
        IRtConsole console = new DaemonPipeConsole(writer, "req1");

        console.IsInputRedirected.ShouldBeFalse();
        console.In.ReadToEnd().ShouldBe("");
    }
}
