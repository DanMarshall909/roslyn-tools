using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class DurablePrepareHandlerTests
{
    [Fact]
    public async Task durable_prepare_port_remains_typed_but_is_not_an_mcp_tool()
    {
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(Directory.GetCurrentDirectory()));
        var descriptor = provider.GetRequiredService<ApplicationCommandCatalog>().All
            .Single(candidate => candidate.ToolName == DurablePrepareCommandDefinition.Definition.Name);
        var tools = provider.GetRequiredService<ToolRegistry>();

        descriptor.Port.ShouldBeOfType<DurablePrepareOperation>();
        descriptor.Definition.ShouldBeSameAs(DurablePrepareCommandDefinition.Definition);
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.LocalWrite);
        descriptor.Effects.ShouldBe(CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
        descriptor.Audience.ShouldBe(CommandAudience.Maintainer);
        descriptor.McpVisible.ShouldBeFalse();
        tools.GetTools().ShouldNotContain(tool => tool.name == descriptor.ToolName);
        provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .ShouldNotContain(tool => tool.name == descriptor.ToolName);

        using var arguments = JsonDocument.Parse("{}");
        var result = await tools.InvokeAsync(descriptor.ToolName, arguments.RootElement);
        result.isError.ShouldBeTrue();
        result.content.ShouldBe($"Unknown tool: {descriptor.ToolName}");
    }
}
