using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class ArchitectureCheckHandlerTests
{
    [Fact]
    public void Descriptor_is_public_read_only_and_has_typed_cli_mcp_schema_parity()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        var descriptor = untrustedProvider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == ArchitectureCheckCommandDefinition.Name);
        var tool = untrustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == ArchitectureCheckCommandDefinition.Name);

        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(typeof(ArchitectureCheckRequest));
        descriptor.Port.ResponseType.ShouldBe(typeof(ArchitectureCheckResult));
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.ReadOnly);
        descriptor.Effects.ShouldBe(CommandEffects.RepositoryRead | CommandEffects.RepositoryCodeExecution);
        tool.annotations.readOnlyHint.ShouldBeTrue();
        tool.annotations.destructiveHint.ShouldBeFalse();
        var properties = tool.inputSchema["properties"]
            .ShouldBeOfType<Dictionary<string, Dictionary<string, object>>>();
        properties.Keys.ShouldBe(["solution", "config", "baseline"], ignoreOrder: true);
    }

    [Fact]
    public async Task Untrusted_host_discovers_read_only_tool_but_invocation_fails_at_repository_trust_boundary()
    {
        using var provider = McpTestHost.CreateProvider();
        provider.GetRequiredService<ToolRegistry>().GetTools()
            .ShouldContain(tool => tool.name == ArchitectureCheckCommandDefinition.Name);
        using var arguments = JsonDocument.Parse("{}");

        var result = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync(ArchitectureCheckCommandDefinition.Name, arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("TrustRequired");
        result.content.ShouldContain("trusted CLI invocation or MCP host startup");
    }

    [Fact]
    public async Task Trusted_host_invokes_the_same_typed_port_and_returns_structured_result()
    {
        using var builder = new AdhocProjectBuilder("Demo.Domain");
        builder.AddSource("Value.cs", "namespace Demo.Domain; public sealed class Value { }");
        var (workspace, solution) = builder.Build();
        File.WriteAllText(Path.Combine(builder.ProjectDir, "Demo.Domain.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        Directory.CreateDirectory(Path.Combine(builder.Directory, ".rt"));
        File.WriteAllText(Path.Combine(builder.Directory, ".rt", "architecture.json"), """
            {
              "version": 1,
              "layers": [
                { "name": "Domain", "projects": ["Demo.Domain"], "allowedProjectReferences": [] }
              ]
            }
            """);

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider(new McpHostOptions(builder.Directory)))
        using (var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            solution = builder.SolutionPath,
            config = ".rt/architecture.json"
        })))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync(ArchitectureCheckCommandDefinition.Name, arguments.RootElement);

            result.isError.ShouldBeFalse();
            result.structuredContent.ShouldNotBeNull();
            var payload = result.structuredContent!.Value;
            payload.GetProperty("isCompliant").GetBoolean().ShouldBeTrue();
            payload.GetProperty("violationCount").GetInt32().ShouldBe(0);
            payload.GetProperty("checkedProjectCount").GetInt32().ShouldBe(1);
        }
    }
}
