using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

public sealed class ServerCancellationContractTests
{
    [Theory]
    [InlineData("2024-11-05")]
    [InlineData("2025-03-26")]
    [InlineData("2025-06-18")]
    [InlineData("2025-11-25")]
    [InlineData("2026-07-28")]
    public async Task cancelled_tool_call_stops_its_port_without_a_response_and_preserves_the_connection(
        string protocolVersion)
    {
        var inputPipe = new Pipe();
        var outputPipe = new Pipe();
        await using var serverInput = inputPipe.Reader.AsStream();
        await using var serverOutput = outputPipe.Writer.AsStream();
        await using var clientInput = inputPipe.Writer.AsStream(leaveOpen: true);
        await using var clientOutput = outputPipe.Reader.AsStream(leaveOpen: true);
        using var writer = new StreamWriter(clientInput, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
        using var reader = new StreamReader(clientOutput, Encoding.UTF8, leaveOpen: true);
        using var serverStop = new CancellationTokenSource();
        var blockingPort = new BlockingPrimePort();
        var services = new ServiceCollection();
        services.AddMcpHostServices(serverInput, serverOutput);
        services.RemoveAll<ICommandApplicationPort>();
        services.AddSingleton<ICommandApplicationPort>(blockingPort);
        using var provider = services.BuildServiceProvider();
        var serverTask = provider.GetRequiredService<Server>().RunAsync(serverStop.Token);
        var metadata = new Dictionary<string, object>
        {
            ["io.modelcontextprotocol/protocolVersion"] = protocolVersion,
            ["io.modelcontextprotocol/clientInfo"] = new { name = "rt-cancellation-test", version = "1.0" },
            ["io.modelcontextprotocol/clientCapabilities"] = new { }
        };

        try
        {
            if (protocolVersion == "2026-07-28")
            {
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "server/discover",
                    @params = new { _meta = metadata }
                });
            }
            else
            {
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        protocolVersion,
                        capabilities = new { },
                        clientInfo = new { name = "rt-cancellation-test", version = "1.0" }
                    }
                });
            }

            using (var handshake = await ReadAsync(reader))
                handshake.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
            if (protocolVersion != "2026-07-28")
                await SendAsync(writer, new { jsonrpc = "2.0", method = "notifications/initialized" });

            if (protocolVersion == "2026-07-28")
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "tools/call",
                    @params = new { _meta = metadata, name = "prime", arguments = new { } }
                });
            else
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    id = 3,
                    method = "tools/call",
                    @params = new { name = "prime", arguments = new { } }
                });
            await blockingPort.Started.WaitAsync(TimeSpan.FromSeconds(10));

            if (protocolVersion == "2026-07-28")
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    method = "notifications/cancelled",
                    @params = new { _meta = metadata, requestId = 3, reason = "contract test" }
                });
            else
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    method = "notifications/cancelled",
                    @params = new { requestId = 3, reason = "contract test" }
                });
            await blockingPort.Cancelled.WaitAsync(TimeSpan.FromSeconds(10));

            if (protocolVersion == "2026-07-28")
                await SendAsync(writer, new
                {
                    jsonrpc = "2.0",
                    id = 4,
                    method = "tools/list",
                    @params = new { _meta = metadata }
                });
            else
                await SendAsync(writer, new { jsonrpc = "2.0", id = 4, method = "tools/list" });
            using (var list = await ReadAsync(reader))
            {
                list.RootElement.GetProperty("id").GetInt32().ShouldBe(4);
                list.RootElement.GetProperty("result").GetProperty("tools")[0]
                    .GetProperty("name").GetString().ShouldBe("prime");
            }

            await inputPipe.Writer.CompleteAsync();
            await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
            await serverOutput.DisposeAsync();
            (await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10))).ShouldBeEmpty();
        }
        finally
        {
            blockingPort.Release();
            serverStop.Cancel();
        }
    }

    private static async Task SendAsync(StreamWriter writer, object message) =>
        await writer.WriteLineAsync(JsonSerializer.Serialize(message));

    private static async Task<JsonDocument> ReadAsync(StreamReader reader)
    {
        var line = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
        line.ShouldNotBeNullOrWhiteSpace();
        return JsonDocument.Parse(line);
    }

    private sealed class BlockingPrimePort : ICommandApplicationPort
    {
        private readonly TaskCompletionSource<bool> _started =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _cancelled =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ApplicationCommandDefinition Definition => PrimeCommandDefinition.Definition;
        public Type RequestType => typeof(PrimeRequest);
        public Type ResponseType => typeof(PrimeResult);
        public Task Started => _started.Task;
        public Task Cancelled => _cancelled.Task;

        public async Task<CommandPortResult> InvokeAsync(
            object request,
            CancellationToken cancellationToken = default)
        {
            request.ShouldBeOfType<PrimeRequest>();
            _started.TrySetResult(true);
            try
            {
                await _release.Task.WaitAsync(cancellationToken);
                return CommandPortResult.Failure(new RtError(
                    RtErrorKind.InvalidInput, "The test released a stalled request."));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                _cancelled.TrySetResult(true);
                throw;
            }
        }

        public void Release() => _release.TrySetResult(true);
    }
}
