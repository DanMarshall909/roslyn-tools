using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Mcp;

namespace RoslynTools.Tests.Mcp;

internal static class McpProtocolHarness
{
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan RequestResponseTimeout = TimeSpan.FromSeconds(30);

    private const string Initialize =
        """{"jsonrpc":"2.0","id":-1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"rt-tests","version":"1.0"}}}""";
    private const string Initialized =
        """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

    internal static async Task<string> RunAsync(
        string transcript,
        McpHostOptions? hostOptions = null)
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

        var services = new ServiceCollection();
        services.AddMcpHostServices(serverInput, serverOutput, hostOptions);
        using var provider = services.BuildServiceProvider();
        var serverTask = provider.GetRequiredService<Server>().RunAsync();
        var responses = new List<string>();

        var lines = transcript.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
        if (!lines.Any(line => IsInitializeRequest(line)))
        {
            await writer.WriteLineAsync(Initialize);
            await ReadResponseAsync(reader, HandshakeTimeout);
            await writer.WriteLineAsync(Initialized);
        }

        foreach (var line in lines)
        {
            await writer.WriteLineAsync(line);
            if (!IsNotification(line))
                responses.Add(await ReadResponseAsync(reader, RequestResponseTimeout));
        }

        await inputPipe.Writer.CompleteAsync();
        await serverTask.WaitAsync(TimeSpan.FromSeconds(10));
        await serverOutput.DisposeAsync();
        var trailingOutput = await reader.ReadToEndAsync().WaitAsync(TimeSpan.FromSeconds(10));
        responses.AddRange(trailingOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r')));
        return string.Join(Environment.NewLine, responses);
    }

    private static async Task<string> ReadResponseAsync(StreamReader reader, TimeSpan timeout)
    {
        return await reader.ReadLineAsync().WaitAsync(timeout)
            ?? throw new EndOfStreamException("The MCP server ended without a response.");
    }

    private static bool IsInitializeRequest(string line)
    {
        using var document = TryParse(line);
        return document is not null
               && document.RootElement.ValueKind == JsonValueKind.Object
               && document.RootElement.TryGetProperty("id", out _)
               && document.RootElement.TryGetProperty("method", out var method)
               && method.ValueKind == JsonValueKind.String
               && method.GetString() == "initialize";
    }

    private static bool IsNotification(string line)
    {
        using var document = TryParse(line);
        return document is not null
               && document.RootElement.ValueKind == JsonValueKind.Object
               && !document.RootElement.TryGetProperty("id", out _)
               && document.RootElement.TryGetProperty("method", out _);
    }

    private static JsonDocument? TryParse(string line)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
