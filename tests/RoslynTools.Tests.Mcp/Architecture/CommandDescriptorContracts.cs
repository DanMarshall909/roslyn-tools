using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class CommandDescriptorContracts
{
    [Fact]
    public void every_mcp_visible_descriptor_has_a_unique_tool_and_generated_schema()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Where(descriptor => descriptor.McpVisible)
            .ToList();
        var tools = provider.GetRequiredService<ToolSchemaBuilder>().Build();

        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldBeUnique();
        tools.Select(tool => tool.name).ShouldBeUnique();

        foreach (var descriptor in descriptors)
        {
            var tool = tools.Single(candidate => candidate.name == descriptor.ToolName);
            tool.inputSchema["type"].ShouldBe("object");

            if (descriptor.Structured)
                tool.outputSchema.ShouldNotBeNull();
        }
    }

    [Fact]
    public void nested_feedback_operations_are_leaf_descriptors_and_operational_commands_are_hidden()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>().All;

        descriptors.Single(descriptor => descriptor.ToolName == "feedback_add")
            .Path.ShouldBe(["feedback", "add"]);
        descriptors.Single(descriptor => descriptor.ToolName == "feedback_list")
            .Path.ShouldBe(["feedback", "list"]);
        descriptors.Single(descriptor => descriptor.ToolName == "feedback_process")
            .Path.ShouldBe(["feedback", "process"]);

        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("daemon");
        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("prime");
        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("dump_help");
    }

    [Fact]
    public void outline_descriptor_projects_the_core_owned_command_definition_and_request_contract()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == OutlineCommandDefinition.Name);
        var definition = DaemonOwnedCommands.All
            .Single(candidate => candidate.Name == OutlineCommandDefinition.Name);

        descriptor.WhenToUse.ShouldBe(definition.Info.WhenToUse);
        descriptor.Port.ShouldNotBeNull();
        descriptor.Port.RequestType.ShouldBe(definition.RequestType);
        definition.Safety.ShouldBe(CommandSafety.ReadOnly);
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.ReadOnly);
        DaemonOwnedCommands.Contains(descriptor.ToolName).ShouldBeTrue();
    }

    [Fact]
    public void mutation_safety_adds_dry_run_unless_the_caller_made_an_explicit_choice()
    {
        var descriptor = new ApplicationCommandDescriptor(
            ["refactor", "sample"],
            "sample",
            "sample",
            "sample",
            () => new System.CommandLine.Command("sample"),
            McpVisible: true,
            CommandSafetyPolicy.DryRunByDefault,
            Structured: true,
            Port: null);

        using var omitted = JsonDocument.Parse("""{"symbol":"Foo"}""");
        var safe = McpCommandAdapter.ApplySafetyPolicy(descriptor, omitted.RootElement);
        safe.GetProperty("dry-run").GetBoolean().ShouldBeTrue();

        using var explicitApply = JsonDocument.Parse("""{"dry-run":false}""");
        var apply = McpCommandAdapter.ApplySafetyPolicy(
            descriptor,
            explicitApply.RootElement);
        apply.GetProperty("dry-run").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task feedback_list_uses_the_generic_port_instead_of_a_native_handler()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse("""{"status":"processed"}""");
        var result = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync("feedback_list", arguments.RootElement);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
    }
}
