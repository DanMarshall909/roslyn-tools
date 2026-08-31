using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
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

    [Fact]
    public async Task structured_tools_advertise_output_schema()
    {
        var output = await RunSingleRequestAsync(
            """{"jsonrpc":"2.0","method":"tools/list","id":1}""");

        using var doc = JsonDocument.Parse(output);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var suggest = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("name").GetString() == "suggest_command");

        suggest.TryGetProperty("outputSchema", out var schema).ShouldBeTrue();
        schema.GetProperty("oneOf").GetArrayLength().ShouldBe(2);
    }

    [Fact]
    public async Task search_symbols_advertises_bounded_typed_discovery_contract()
    {
        var output = await RunSingleRequestAsync(
            """{"jsonrpc":"2.0","method":"tools/list","id":1}""");

        using var doc = JsonDocument.Parse(output);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var search = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("name").GetString() == "search_symbols");
        var schema = search.GetProperty("inputSchema");
        schema.GetProperty("required").EnumerateArray()
            .Select(property => property.GetString())
            .ShouldContain("query");
        var properties = schema.GetProperty("properties");
        properties.TryGetProperty("kind", out _).ShouldBeTrue();
        properties.TryGetProperty("genericArity", out _).ShouldBeTrue();
        properties.TryGetProperty("fuzzy", out _).ShouldBeTrue();
        properties.TryGetProperty("continuationToken", out _).ShouldBeTrue();
        search.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean().ShouldBeTrue();
        search.GetProperty("_meta").GetProperty("rt/maturity").GetString().ShouldBe("Preview");
        search.TryGetProperty("outputSchema", out _).ShouldBeTrue();
    }

    [Fact]
    public async Task relationship_wrappers_are_consolidated_out_of_normal_discovery()
    {
        var output = await RunSingleRequestAsync(
            """{"jsonrpc":"2.0","method":"tools/list","id":1}""");

        using var doc = JsonDocument.Parse(output);
        var tools = doc.RootElement.GetProperty("result").GetProperty("tools");
        var names = tools.EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();
        names.ShouldContain("relationships");
        names.ShouldNotContain("find_refs");
        names.ShouldNotContain("find_callers");
        names.ShouldNotContain("find_impl");

        var relationships = tools.EnumerateArray().Single(tool =>
            tool.GetProperty("name").GetString() == "relationships");
        relationships.GetProperty("inputSchema").GetProperty("properties")
            .GetProperty("mode").GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(["References", "Callers", "Implementations"]);
    }

    [Fact]
    public async Task descriptor_tool_call_returns_equivalent_text_and_structured_content()
    {
        var output = await RunSingleRequestAsync(
            """
            {"jsonrpc":"2.0","method":"tools/call","id":2,"params":{"name":"suggest_command","arguments":{"query":"rename","top":1}}}
            """);

        using var doc = JsonDocument.Parse(output);
        var result = doc.RootElement.GetProperty("result");
        result.TryGetProperty("structuredContent", out var structured).ShouldBeTrue();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.ShouldNotBeNull();
        using var payload = JsonDocument.Parse(text);
        payload.RootElement.GetProperty("results").GetArrayLength().ShouldBe(1);
        structured.GetProperty("results").GetArrayLength().ShouldBe(1);
        JsonElement.DeepEquals(structured, payload.RootElement).ShouldBeTrue();
    }

    [Fact]
    public async Task doctor_returns_the_versioned_envelope_as_structured_content_without_evaluating_untrusted_code()
    {
        var arguments = JsonSerializer.Serialize(new
        {
            directory = FindRepoRoot(),
            solution = "RoslynTools.slnx"
        });
        using var argumentsDocument = JsonDocument.Parse(arguments);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            id = 3,
            @params = new
            {
                name = "doctor",
                arguments = argumentsDocument.RootElement.Clone()
            }
        });
        var output = await RunSingleRequestAsync(request);

        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeFalse();
        var structured = result.GetProperty("structuredContent");
        structured.GetProperty("schemaVersion").GetString().ShouldBe("rt.result.v1");
        structured.GetProperty("operation").GetString().ShouldBe("doctor");
        structured.GetProperty("outcome").GetString().ShouldBe("TrustRequired");
        structured.GetProperty("data").GetProperty("installation")
            .GetProperty("daemonRequired").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task semantic_tool_call_cannot_grant_itself_repository_trust()
    {
        var arguments = JsonSerializer.Serialize(new
        {
            query = "SymbolCandidateService",
            solution = Path.Combine(FindRepoRoot(), "RoslynTools.slnx")
        });
        using var argumentsDocument = JsonDocument.Parse(arguments);
        var request = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            id = 4,
            @params = new
            {
                name = "search_symbols",
                arguments = argumentsDocument.RootElement.Clone()
            }
        });

        var output = await RunSingleRequestAsync(request);

        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.ShouldNotBeNull();
        using var error = JsonDocument.Parse(text);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.TrustRequired));
    }

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}
