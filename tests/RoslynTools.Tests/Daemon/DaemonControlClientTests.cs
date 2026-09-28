using System.IO.Pipes;
using System.Text.Json;
using RoslynTools.Daemon;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonControlClientTests
{
    [Fact]
    public async Task Control_client_sends_only_the_stop_request_and_reads_its_exit()
    {
        var pipeName = $"rt-control-test-{Guid.NewGuid():N}";
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var client = new DaemonClient();

        var resultTask = client.SendStopOverPipeAsync(pipeName, deadline.Token);
        await server.WaitForConnectionAsync(deadline.Token);
        using var reader = new StreamReader(server, DaemonEncoding.NoBom, leaveOpen: true);
        var line = await reader.ReadLineAsync(deadline.Token);
        line.ShouldNotBeNull();
        var request = JsonSerializer.Deserialize<DaemonRequest>(line, DaemonProtocol.JsonOpts);
        request.ShouldNotBeNull();
        request.Args.ShouldBe(["stop"]);

        await using var writer = new StreamWriter(server, DaemonEncoding.NoBom, leaveOpen: true)
        {
            AutoFlush = true
        };
        await writer.WriteLineAsync(JsonSerializer.Serialize(
            new DaemonResponse(request.RequestId, "exit", Code: 0),
            DaemonProtocol.JsonOpts));

        (await resultTask).ShouldBe(0);
    }
}
