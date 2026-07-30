using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Mcp.Tools;
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

        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("daemon");
        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("prime");
        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("dump_help");
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
    public async Task outline_cli_and_generic_mcp_adapter_return_equivalent_structured_output()
    {
        var cliOutput = await InvokeOutlineCliAsync();
        var mcpOutput = await InvokeOutlineMcpAsync();

        JsonElement.DeepEquals(cliOutput, mcpOutput).ShouldBeTrue();
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

    private static async Task<JsonElement> InvokeOutlineCliAsync()
    {
        using var project = McpTestHost.CreateInvoiceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        {
            var output = new StringBuilder();
            using var capture = new CapturingConsole(output);
            using (RtConsole.UseConsole(capture))
            {
                var exitCode = await App.RunAsync(
                    ["outline", "TestApp", "--solution", "ignored.slnx", "--json"],
                    provider);
                exitCode.ShouldBe(0);
            }

            using var json = JsonDocument.Parse(output.ToString());
            return json.RootElement.Clone();
        }
    }

    private static async Task<JsonElement> InvokeOutlineMcpAsync()
    {
        using var project = McpTestHost.CreateInvoiceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (var provider = McpTestHost.CreateProvider())
        using (var arguments = JsonDocument.Parse(
                   """
                   {
                     "target": "TestApp",
                     "solution": "ignored.slnx"
                   }
                   """))
        {
            var result = await provider.GetRequiredService<ToolRegistry>()
                .InvokeAsync("outline", arguments.RootElement);
            result.isError.ShouldBeFalse();

            using var json = JsonDocument.Parse(result.content);
            return json.RootElement.Clone();
        }
    }
}
