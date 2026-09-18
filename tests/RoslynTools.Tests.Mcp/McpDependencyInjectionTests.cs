using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using Shouldly;

namespace RoslynTools.Tests.Mcp;

[Collection("mcp-bridge")]
public sealed class McpDependencyInjectionTests
{
    [Fact]
    public void Mcp_registers_cli_services_and_mcp_runtime_services()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<ToolSchemaBuilder>().ShouldNotBeNull();
        provider.GetRequiredService<CliCommandBridge>().ShouldNotBeNull();
        provider.GetRequiredService<McpCommandAdapter>().ShouldNotBeNull();
        provider.GetRequiredService<ToolRegistry>().ShouldNotBeNull();
        provider.GetRequiredService<Server>().ShouldNotBeNull();
    }

    [Fact]
    public async Task Mcp_test_host_never_attaches_to_a_resident_daemon()
    {
        using var provider = McpTestHost.CreateProvider();

        var attempt = await provider.GetRequiredService<IDaemonRpcClient>().ExecuteAsync(
            "describe",
            JsonSerializer.SerializeToElement(new { symbol = "TestApp.Widget" }),
            CommandSafety.ReadOnly);

        attempt.State.ShouldBe(DaemonRpcAttemptState.UnavailableBeforeDispatch);
        attempt.DispatchStarted.ShouldBeFalse();
    }

    [Fact]
    public void Untrusted_tool_schema_builder_produces_every_read_only_visible_descriptor()
    {
        using var provider = McpTestHost.CreateProvider();
        var builder = provider.GetRequiredService<ToolSchemaBuilder>();
        var tools = builder.Build();

        var visibleDescriptorCount = provider
            .GetRequiredService<RoslynTools.CommandDescriptors.CommandDescriptorRegistry>()
            .All.Count(descriptor => descriptor.McpVisible
                                     && !descriptor.Effects.HasFlag(CommandEffects.RepositoryWrite));
        tools.Count.ShouldBe(visibleDescriptorCount);
        Assert.All(tools, t =>
        {
            string.IsNullOrWhiteSpace(t.name).ShouldBeFalse();
            string.IsNullOrWhiteSpace(t.description).ShouldBeFalse();
            t.inputSchema.ShouldNotBeNull();
            t.inputSchema["type"].ShouldBe("object");
            t.annotations.readOnlyHint.ShouldBeTrue();
            t.annotations.destructiveHint.ShouldBeFalse();
            t.metadata.ShouldContainKey("rt/maturity");
            t.metadata.ShouldContainKey("rt/effects");
            t.metadata["rt/audience"].ShouldBe("Public");
        });
    }

    [Fact]
    public void Trusted_tool_schema_builder_produces_the_complete_public_inventory()
    {
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(FindRepoRoot()));
        var descriptors = provider
            .GetRequiredService<RoslynTools.CommandDescriptors.CommandDescriptorRegistry>()
            .All.Where(descriptor => descriptor.McpVisible
                                     && descriptor.Audience == CommandAudience.Public)
            .ToList();
        var tools = provider.GetRequiredService<ToolSchemaBuilder>().Build();

        tools.Count.ShouldBe(descriptors.Count);
        tools.Select(tool => tool.name).ShouldBe(
            descriptors.Select(descriptor => descriptor.ToolName),
            ignoreOrder: true);
        tools.Select(tool => tool.name).ShouldContain("prime");
        tools.Select(tool => tool.name).ShouldContain("move");
        tools.Select(tool => tool.name).ShouldContain("rename");
        tools.Select(tool => tool.name).ShouldContain("delete_unreferenced");
    }

    [Fact]
    public async Task Mcp_tool_policy_filters_discovery_and_invocation_through_the_same_boundary()
    {
        var policy = new McpToolPolicy(
            new HashSet<string>(["prime", "outline"], StringComparer.Ordinal),
            new HashSet<string>(["outline"], StringComparer.Ordinal),
            ["test-config.json"]);
        using var provider = McpTestHost.CreateProvider(
            new McpHostOptions(toolPolicy: policy));
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var arguments = JsonDocument.Parse("{}");

        registry.GetTools().Select(tool => tool.name).ShouldBe(["prime"]);
        var denied = await registry.InvokeAsync("outline", arguments.RootElement);

        denied.isError.ShouldBeTrue();
        denied.content.ShouldContain("Unknown tool");
    }

    [Fact]
    public void Unknown_exact_tool_name_reports_the_configuration_source()
    {
        var policy = new McpToolPolicy(
            new HashSet<string>(["not_a_real_tool"], StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal),
            ["C:/repo/.rt/config.json"]);
        using var provider = McpTestHost.CreateProvider();

        var error = McpToolAvailability.ValidatePolicy(
            provider.GetRequiredService<RoslynTools.CommandDescriptors.CommandDescriptorRegistry>().All,
            policy);

        error.ShouldNotBeNull();
        error.ShouldContain("not_a_real_tool", Case.Sensitive);
        error.ShouldContain("C:/repo/.rt/config.json", Case.Sensitive);
    }

    [Fact]
    public void Tool_schema_builder_excludes_removed_debug_surface()
    {
        using var provider = McpTestHost.CreateProvider();
        var builder = provider.GetRequiredService<ToolSchemaBuilder>();

        builder.Build().ShouldNotContain(tool => tool.name == "debug");
    }

    [Fact]
    public async Task Tool_registry_rejects_removed_debug_surface()
    {
        var trace = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.jsonl");
        await File.WriteAllTextAsync(
            trace,
            """
            {"event":"tool_call","tool":"apply_patch","files":["src/Foo.cs"],"summary":"edit file"}
            {"event":"final_response","content":"done"}
            """);

        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse(
            $$"""
              {
                "trace": "{{JsonEncodedText.Encode(trace)}}",
                "break": ["tool:apply_patch", "file:src/Foo.cs"],
                "assert": ["requires-event:final_response", "no-tool:web"],
                "break-only": true
              }
              """);

        var result = await registry.InvokeAsync("debug", args.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("Unknown tool");
    }

    [Fact]
    public async Task Tool_registry_tracks_persistent_trust_changes_during_one_host_lifetime()
    {
        var registryPath = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-live-trust-{Guid.NewGuid():N}",
            "repository-trust.json");
        try
        {
            var trustRegistry = new RepositoryTrustRegistry(registryPath);
            using var provider = McpTestHost.CreateProvider(
                new McpHostOptions(trustRegistry: trustRegistry));
            var tools = provider.GetRequiredService<ToolRegistry>();
            using var arguments = JsonDocument.Parse("{}");

            tools.GetTools().ShouldNotContain(tool => tool.name == "move");
            trustRegistry.Add(FindRepoRoot()).IsOk.ShouldBeTrue();
            tools.GetTools().ShouldContain(tool => tool.name == "move");
            var admitted = await tools.InvokeAsync("move", arguments.RootElement);
            admitted.content.ShouldNotContain("Unknown tool");

            trustRegistry.Remove(FindRepoRoot()).IsOk.ShouldBeTrue();
            tools.GetTools().ShouldNotContain(tool => tool.name == "move");
            var rejected = await tools.InvokeAsync("move", arguments.RootElement);
            rejected.isError.ShouldBeTrue();
            rejected.content.ShouldContain("Unknown tool");
        }
        finally
        {
            var directory = Path.GetDirectoryName(registryPath)!;
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Mcp_runtime_does_not_use_static_service_locator_or_static_workspace_loading()
    {
        var repoRoot = FindRepoRoot();
        var sourceFiles = Directory.EnumerateFiles(
            Path.Combine(repoRoot, "src", "RoslynTools.Mcp"),
            "*.cs",
            SearchOption.AllDirectories);

        var directUsages = sourceFiles
            .Where(path =>
            {
                var text = File.ReadAllText(path);
                return text.Contains("RtServiceProvider.Get", StringComparison.Ordinal)
                       || text.Contains("WorkspaceLoader.LoadAsync", StringComparison.Ordinal)
                       || text.Contains("SolutionWriter.ApplyChangesAsync", StringComparison.Ordinal);
            })
            .Select(path => Path.GetRelativePath(repoRoot, path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        directUsages.ShouldBeEmpty();
    }

    [Fact]
    public async Task Tool_registry_invokes_relationships_via_typed_port()
    {
        using var project = McpTestHost.CreateInvoiceProject();

        var (workspace, sln) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        using (var provider = McpTestHost.CreateProvider())
        {
            var registry = provider.GetRequiredService<ToolRegistry>();
            using var args = JsonDocument.Parse("""
                                                {
                                                  "symbol": "Invoice",
                                                  "mode": "References",
                                                  "kind": "type",
                                                  "solution": "ignored.slnx"
                                                }
                                                """);

            var result = await registry.InvokeAsync("relationships", args.RootElement);

            result.isError.ShouldBeFalse();
            Assert.Contains("Invoice", result.content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Tool_registry_invokes_source_with_related_symbol_content_via_typed_port()
    {
        using var project = new AdhocProjectBuilder("SourceApp")
            .AddSource("Source.cs", """
                namespace App;
                public static class Formatter
                {
                    public static string Format(int value) => value.ToString();
                }

                public sealed class Calculator
                {
                    public string Add(int left, int right) => Formatter.Format(left + right);
                }
                """);
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        {
            using var args = JsonDocument.Parse("""
                                                {
                                                  "symbol": "App.Calculator.Add",
                                                  "kind": "method",
                                                  "solution": "ignored.slnx",
                                                  "includeRelated": true,
                                                  "relatedLimit": 1
                                                }
                                                """);

            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync("source", args.RootElement);

            result.isError.ShouldBeFalse();
            using var payload = JsonDocument.Parse(result.content);
            var root = payload.RootElement;
            root.GetProperty("subject").GetString().ShouldBe("App.Calculator.Add(int, int)");
            root.GetProperty("declarations").GetArrayLength().ShouldBe(1);
            var related = root.GetProperty("relatedSymbols")[0];
            related.GetProperty("fqn").GetString().ShouldBe("App.Formatter.Format(int)");
            related.GetProperty("declarations")[0].GetProperty("source").GetString()
                .ShouldBe("public static string Format(int value) => value.ToString();");
        }
    }

    [Fact]
    public async Task Tool_registry_pages_source_with_the_shared_continuation_contract()
    {
        using var project = new AdhocProjectBuilder("SourceApp")
            .AddSource("Source.cs", """
                namespace App;
                public static class Texts
                {
                    public static string Read() => "source text that spans several result pages";
                }
                """);
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        {
            using var args = JsonDocument.Parse("""
                                                {
                                                  "symbol": "App.Texts.Read",
                                                  "kind": "method",
                                                  "maxSourceBytes": 20
                                                }
                                                """);
            var registry = provider.GetRequiredService<ToolRegistry>();

            var first = await registry.InvokeAsync("source", args.RootElement);

            first.isError.ShouldBeFalse();
            using var firstPayload = JsonDocument.Parse(first.content);
            var firstRoot = firstPayload.RootElement;
            firstRoot.GetProperty("sourceBytesReturned").GetInt32().ShouldBeLessThanOrEqualTo(20);
            firstRoot.GetProperty("truncated").GetBoolean().ShouldBeTrue();
            var continuation = firstRoot.GetProperty("continuationToken").GetString();
            continuation.ShouldNotBeNullOrWhiteSpace();
            var firstEnd = firstRoot.GetProperty("declarations")[0]
                .GetProperty("returnedToByteExclusive").GetInt32();

            using var continuedArgs = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                symbol = "App.Texts.Read",
                kind = "method",
                maxSourceBytes = 20,
                continuationToken = continuation
            }));
            var second = await registry.InvokeAsync("source", continuedArgs.RootElement);

            second.isError.ShouldBeFalse();
            using var secondPayload = JsonDocument.Parse(second.content);
            secondPayload.RootElement.GetProperty("declarations")[0]
                .GetProperty("returnedFromByte").GetInt32().ShouldBe(firstEnd);
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }

}
