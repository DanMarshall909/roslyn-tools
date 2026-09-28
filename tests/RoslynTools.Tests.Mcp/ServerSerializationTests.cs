using System.Text.Json;
using Microsoft.CodeAnalysis;
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
    private static async Task<string> RunSingleRequestAsync(
        string requestLine,
        McpHostOptions? hostOptions = null)
    {
        var services = new ServiceCollection();
        services.AddMcpHostServices(
            new StringReader(requestLine + Environment.NewLine),
            new StringWriter(),
            hostOptions);
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
        services.AddMcpHostServices(new StringReader(string.Empty), output);
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
    public async Task initialized_notification_has_no_response_before_tools_list()
    {
        var output = await RunSingleRequestAsync(
            """
            {"jsonrpc":"2.0","method":"initialize","id":1}
            {"jsonrpc":"2.0","method":"notifications/initialized"}
            {"jsonrpc":"2.0","method":"tools/list","id":2}
            """);

        var responses = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        responses.Length.ShouldBe(2);
        using var initialize = JsonDocument.Parse(responses[0]);
        initialize.RootElement.GetProperty("id").GetInt32().ShouldBe(1);
        using var tools = JsonDocument.Parse(responses[1]);
        tools.RootElement.GetProperty("id").GetInt32().ShouldBe(2);
    }

    [Theory]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}", true)]
    [InlineData("{\"jsonrpc\":\"1.0\",\"method\":\"notifications/initialized\"}", false)]
    [InlineData("{\"method\":\"notifications/initialized\"}", false)]
    [InlineData("{\"jsonrpc\":\"2.0\"}", false)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/cancelled\"}", false)]
    [InlineData("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\",\"id\":1}", false)]
    public void initialized_notification_requires_the_exact_notification_shape(
        string json,
        bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Server.IsInitializedNotification(document.RootElement).ShouldBe(expected);
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
    public async Task prime_is_available_through_protocol_discovery_and_invocation()
    {
        var output = await RunSingleRequestAsync(
            """
            {"jsonrpc":"2.0","method":"tools/list","id":1}
            {"jsonrpc":"2.0","method":"tools/call","id":2,"params":{"name":"prime","arguments":{}}}
            """);

        var responses = output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        responses.Length.ShouldBe(2);

        using var discovery = JsonDocument.Parse(responses[0]);
        discovery.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .ShouldContain(tool => tool.GetProperty("name").GetString() == "prime");

        using var invocation = JsonDocument.Parse(responses[1]);
        var result = invocation.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeFalse();
        result.GetProperty("structuredContent").GetProperty("hotCommands")
            .GetArrayLength().ShouldBe(6);
    }

    [Fact]
    public async Task trusted_tools_list_includes_prime_and_key_mutation_operations()
    {
        var output = await RunSingleRequestAsync(
            """{"jsonrpc":"2.0","method":"tools/list","id":1}""",
            new McpHostOptions(FindRepoRoot()));

        using var document = JsonDocument.Parse(output);
        var names = document.RootElement.GetProperty("result").GetProperty("tools")
            .EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();

        names.Length.ShouldBe(60);
        names.ShouldContain("check");
        names.ShouldContain("prime");
        names.ShouldContain("prepare_operation");
        names.ShouldContain("move");
        names.ShouldContain("move_slice");
        names.ShouldContain("rename");
        names.ShouldContain("delete_unreferenced");
        names.ShouldContain("remove_unused_usings");
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
        properties.TryGetProperty("continuationToken", out _).ShouldBeFalse();
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
    public async Task suggest_requires_its_typed_query_through_the_protocol()
    {
        var output = await RunSingleRequestAsync(
            """
            {"jsonrpc":"2.0","method":"tools/call","id":3,"params":{"name":"suggest_command","arguments":{"top":1}}}
            """);

        using var document = JsonDocument.Parse(output);
        var result = document.RootElement.GetProperty("result");
        result.GetProperty("isError").GetBoolean().ShouldBeTrue();
        var text = result.GetProperty("content")[0].GetProperty("text").GetString();
        text.ShouldNotBeNull();
        using var error = JsonDocument.Parse(text);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.InvalidInput));
        var message = error.RootElement.GetProperty("message").GetString();
        message.ShouldNotBeNull();
        message.ShouldContain("Query");
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
            solution = Path.Combine(FindRepoRoot(), "RoslynTools.slnx"),
            trustRepository = true
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

    [Fact]
    public async Task startup_authorized_repository_can_run_a_semantic_tool()
    {
        // Keep this cache-fingerprint fixture outside the repository tree so the
        // core test project's concurrent TestOutput writes cannot invalidate it.
        using var project = McpTestHost.CreateInvoiceProject(baseOutputDirectory: Path.GetTempPath());
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var fixtureProject = solution.Projects.Single();
            var solutionPath = Path.Combine(project.Directory, "Invoice.slnx");
            File.WriteAllText(solutionPath,
                $"<Solution><Project Path=\"{fixtureProject.Name}/{fixtureProject.Name}.csproj\" /></Solution>");
            File.WriteAllText(fixtureProject.FilePath!, "<Project />");
            WorkspaceLoader.OpenSolutionOverride.Value = async (loadedWorkspace, requestedPath) =>
            {
                requestedPath.ShouldBe(solutionPath);
                // Preserve the real loader trust gate and the acquisition workspace identity.
                // This controlled load has usable source, but supplies no production closure receipt.
                var loaded = loadedWorkspace.CurrentSolution.AddProject(ProjectInfo.Create(
                    fixtureProject.Id, fixtureProject.Version, fixtureProject.Name,
                    fixtureProject.AssemblyName!, fixtureProject.Language, fixtureProject.FilePath,
                    compilationOptions: fixtureProject.CompilationOptions,
                    parseOptions: fixtureProject.ParseOptions,
                    metadataReferences: fixtureProject.MetadataReferences));
                foreach (var source in fixtureProject.Documents)
                    loaded = loaded.AddDocument(source.Id, source.Name, await source.GetTextAsync(),
                        source.Folders, source.FilePath);
                return loaded;
            };
            try
            {
                var request = CreateSearchSymbolsRequest(solutionPath);

                var output = await RunSingleRequestAsync(
                    request,
                    new McpHostOptions(project.Directory));

                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                result.GetProperty("isError").GetBoolean().ShouldBeFalse(
                    result.GetProperty("content").GetRawText());
                result.GetProperty("structuredContent").GetProperty("data").GetProperty("items")
                    .EnumerateArray()
                    .ShouldContain(item => item.GetProperty("name").GetString() == "Invoice");
            }
            finally
            {
                WorkspaceLoader.OpenSolutionOverride.Value = null;
            }
        }
    }

    [Fact]
    public async Task persistent_registry_authorizes_semantic_tools_without_a_startup_path()
    {
        using var project = McpTestHost.CreateInvoiceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var registryDirectory = Path.Combine(
                Path.GetTempPath(),
                $"rt-mcp-persistent-trust-{Guid.NewGuid():N}");
            var registry = new RepositoryTrustRegistry(
                Path.Combine(registryDirectory, "repository-trust.json"));
            WorkspaceLoader.OpenSolutionOverride.Value = (_, _) => Task.FromResult(solution);
            try
            {
                var repositoryRoot = FindRepoRoot();
                registry.Add(repositoryRoot).IsOk.ShouldBeTrue();
                var output = await RunSingleRequestAsync(
                    CreateSearchSymbolsRequest(Path.Combine(repositoryRoot, "RoslynTools.slnx")),
                    new McpHostOptions(trustRegistry: registry));

                using var document = JsonDocument.Parse(output);
                var result = document.RootElement.GetProperty("result");
                result.GetProperty("isError").GetBoolean().ShouldBeFalse(
                    result.GetProperty("content").GetRawText());
                result.GetProperty("structuredContent").GetProperty("data").GetProperty("items")
                    .EnumerateArray()
                    .ShouldContain(item => item.GetProperty("name").GetString() == "Invoice");
            }
            finally
            {
                WorkspaceLoader.OpenSolutionOverride.Value = null;
                if (Directory.Exists(registryDirectory))
                    Directory.Delete(registryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task persistent_trust_for_one_repository_does_not_authorize_another_target()
    {
        var registryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-persistent-trust-{Guid.NewGuid():N}");
        var otherDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-untrusted-target-{Guid.NewGuid():N}");
        Directory.CreateDirectory(otherDirectory);
        try
        {
            var registry = new RepositoryTrustRegistry(
                Path.Combine(registryDirectory, "repository-trust.json"));
            registry.Add(FindRepoRoot()).IsOk.ShouldBeTrue();
            var otherSolution = Path.Combine(otherDirectory, "Other.slnx");
            File.WriteAllText(otherSolution, "<Solution />");

            var output = await RunSingleRequestAsync(
                CreateSearchSymbolsRequest(otherSolution),
                new McpHostOptions(trustRegistry: registry));

            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            result.GetProperty("isError").GetBoolean().ShouldBeTrue();
            var text = result.GetProperty("content")[0].GetProperty("text").GetString();
            text.ShouldNotBeNull();
            using var error = JsonDocument.Parse(text);
            error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.TrustRequired));
        }
        finally
        {
            if (Directory.Exists(registryDirectory))
                Directory.Delete(registryDirectory, recursive: true);
            Directory.Delete(otherDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task trust_management_ports_are_never_discovered_over_mcp()
    {
        var registryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-persistent-trust-{Guid.NewGuid():N}");
        try
        {
            var registry = new RepositoryTrustRegistry(
                Path.Combine(registryDirectory, "repository-trust.json"));
            registry.Add(FindRepoRoot()).IsOk.ShouldBeTrue();

            var output = await RunSingleRequestAsync(
                """{"jsonrpc":"2.0","method":"tools/list","id":1}""",
                new McpHostOptions(trustRegistry: registry));

            using var document = JsonDocument.Parse(output);
            var names = document.RootElement.GetProperty("result").GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToArray();
            names.ShouldContain("move_slice");
            names.Any(name => name?.StartsWith("trust_", StringComparison.Ordinal) == true)
                .ShouldBeFalse();
        }
        finally
        {
            if (Directory.Exists(registryDirectory))
                Directory.Delete(registryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task startup_authorization_does_not_cover_a_solution_outside_its_repository_root()
    {
        var repositoryRoot = FindRepoRoot();
        var approvedRoot = Path.Combine(Path.GetTempPath(), $"rt-approved-{Guid.NewGuid():N}");
        Directory.CreateDirectory(approvedRoot);
        try
        {
            var request = CreateSearchSymbolsRequest(Path.Combine(repositoryRoot, "RoslynTools.slnx"));

            var output = await RunSingleRequestAsync(
                request,
                new McpHostOptions(approvedRoot));

            using var document = JsonDocument.Parse(output);
            var result = document.RootElement.GetProperty("result");
            result.GetProperty("isError").GetBoolean().ShouldBeTrue();
            var text = result.GetProperty("content")[0].GetProperty("text").GetString();
            text.ShouldNotBeNull();
            text.ShouldStartWith("{");
            using var error = JsonDocument.Parse(text);
            error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.TrustRequired));
        }
        finally
        {
            Directory.Delete(approvedRoot);
        }
    }

    private static string CreateSearchSymbolsRequest(string solutionPath)
    {
        var arguments = JsonSerializer.SerializeToElement(new
        {
            query = "Invoice",
            solution = solutionPath
        });
        return JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            method = "tools/call",
            id = 5,
            @params = new
            {
                name = "search_symbols",
                arguments
            }
        });
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
