using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class PrimeHandlerTests
{
    [Fact]
    public void prime_is_discoverable_with_an_empty_read_only_schema()
    {
        using var provider = McpTestHost.CreateProvider();

        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>().All
            .Single(candidate => candidate.ToolName == "prime");
        var tool = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == "prime");

        descriptor.Port.ShouldBeOfType<PrimeOperation>();
        descriptor.Definition.ShouldBeSameAs(PrimeCommandDefinition.Definition);
        tool.inputSchema["type"].ShouldBe("object");
        tool.inputSchema.ShouldNotContainKey("properties");
        tool.inputSchema.ShouldNotContainKey("required");
        tool.annotations.readOnlyHint.ShouldBeTrue();
        tool.annotations.destructiveHint.ShouldBeFalse();
        tool.metadata["rt/effects"].ShouldBe(Array.Empty<string>());
        tool.metadata["rt/workspaceRequirement"].ShouldBe("None");
    }

    [Fact]
    public async Task prime_returns_the_shared_session_primer()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse("{}");

        var result = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync("prime", arguments.RootElement);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        var gate = payload.RootElement.GetProperty("gate").GetString();
        gate.ShouldNotBeNull();
        gate.ShouldContain("rt inspect outline/describe");
        var entry = payload.RootElement.GetProperty("entry").GetString();
        entry.ShouldNotBeNull();
        entry.ShouldContain("rt suggest");
        payload.RootElement.GetProperty("hotCommands").GetArrayLength().ShouldBe(6);
    }
}
