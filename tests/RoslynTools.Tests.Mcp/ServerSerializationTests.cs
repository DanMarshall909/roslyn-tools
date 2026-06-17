using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Mcp;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

/// <summary>
/// Regression tests for US-130 — the rt MCP server must not crash when the
/// JSON-RPC request id is a default-initialized JsonElement (malformed request,
/// no parent document) or when a JsonElement's backing document has been
/// disposed before serialization runs. Both manifest as
/// <see cref="InvalidOperationException"/> from
/// <c>System.Text.Json.Serialization.Converters.JsonElementConverter.Write</c>.
/// </summary>
public class ServerSerializationTests
{
    private static async Task<string> RunSingleRequestAsync(string requestLine)
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(requestLine + Environment.NewLine), new StringWriter());
        using var provider = services.BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        await server.RunAsync();

        var output = provider.GetRequiredService<TextWriter>();
        return output.ToString() ?? string.Empty;
    }

    [Fact]
    public async Task malformed_request_without_id_does_not_crash_and_returns_null_id_error()
    {
        // Missing `id` field — current code hits SendErrorAsync(default(JsonElement), ...),
        // and JsonSerializer crashes trying to write a JsonElement with no parent.
        var output = await RunSingleRequestAsync("""{"jsonrpc":"2.0","method":"initialize"}""");

        output.ShouldNotBeNullOrWhiteSpace();
        using var doc = JsonDocument.Parse(output);
        var root = doc.RootElement;
        root.GetProperty("jsonrpc").GetString().ShouldBe("2.0");
        root.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
        root.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32700);
    }

    [Fact]
    public async Task serialization_fault_emits_internal_error_response_instead_of_throwing()
    {
        // Defence-in-depth: even if something inside the response object throws
        // during serialization, the server must not propagate the exception.
        // We simulate the disposed-JsonElement case by building a response that
        // embeds a JsonElement whose document has already been disposed.
        JsonElement disposedElement;
        using (var doc = JsonDocument.Parse("""{"x": 1}"""))
        {
            disposedElement = doc.RootElement;
        } // doc disposed — `disposedElement` is now stale.

        var poisoned = new { jsonrpc = "2.0", id = (object?)1, payload = disposedElement };
        var output = new StringWriter();

        // Server.SendResponseAsync is private; call via the public RunAsync flow
        // would require routing a path that produces this exact object. Instead
        // we verify the defence-in-depth behaviour at the level it must hold:
        // SendResponseAsync must catch and emit a -32603 envelope. Use the
        // internal IVT seam.
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), output);
        using var provider = services.BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();

        // ReSharper disable once AccessToDisposedClosure
        var act = async () => await server.SendResponseForTestAsync(poisoned);
        await act.ShouldNotThrowAsync();

        var written = output.ToString();
        written.ShouldNotBeNullOrWhiteSpace();
        using var resp = JsonDocument.Parse(written);
        resp.RootElement.GetProperty("error").GetProperty("code").GetInt32().ShouldBe(-32603);
        resp.RootElement.GetProperty("id").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task initialize_request_with_numeric_id_round_trips_as_number()
    {
        var output = await RunSingleRequestAsync("""{"jsonrpc":"2.0","method":"initialize","id":42}""");

        using var doc = JsonDocument.Parse(output);
        var idEl = doc.RootElement.GetProperty("id");
        idEl.ValueKind.ShouldBe(JsonValueKind.Number);
        idEl.GetInt64().ShouldBe(42);
    }

    [Fact]
    public async Task initialize_request_with_string_id_round_trips_as_string()
    {
        var output = await RunSingleRequestAsync("""{"jsonrpc":"2.0","method":"initialize","id":"abc-123"}""");

        using var doc = JsonDocument.Parse(output);
        var idEl = doc.RootElement.GetProperty("id");
        idEl.ValueKind.ShouldBe(JsonValueKind.String);
        idEl.GetString().ShouldBe("abc-123");
    }
}
