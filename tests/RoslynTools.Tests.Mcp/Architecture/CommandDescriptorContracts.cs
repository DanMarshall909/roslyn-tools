using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
    public void every_migrated_descriptor_has_one_port_and_request_schema_parity()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>().All
            .Where(descriptor => descriptor.Definition is not null)
            .ToList();

        descriptors.Select(descriptor => descriptor.ToolName).ShouldBeUnique();
        descriptors.ShouldNotBeEmpty();

        foreach (var descriptor in descriptors)
        {
            descriptor.Port.ShouldNotBeNull();
            descriptor.Port.ShouldNotBeAssignableTo<ILegacyCommandApplicationPort>();
            descriptor.Port.Definition.ShouldBeSameAs(descriptor.Definition);

            var tool = ToolSchemaBuilder.Build(descriptor);
            var properties = tool.inputSchema.TryGetValue("properties", out var rawProperties)
                ? (Dictionary<string, Dictionary<string, object>>)rawProperties
                : new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            var required = tool.inputSchema.TryGetValue("required", out var rawRequired)
                ? ((string[])rawRequired).OrderBy(name => name, StringComparer.Ordinal).ToArray()
                : [];
            var requestProperties = descriptor.Definition!.RequestType
                .GetProperties()
                .Where(property => property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
                    != JsonIgnoreCondition.Always)
                .ToList();
            var expectedNames = requestProperties
                .Select(JsonPropertyName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            var expectedRequired = requestProperties
                .Where(property => property.GetCustomAttributes(typeof(JsonRequiredAttribute), true).Length != 0)
                .Select(JsonPropertyName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            properties.Keys.OrderBy(name => name, StringComparer.Ordinal).ShouldBe(expectedNames);
            required.ShouldBe(expectedRequired);
        }
    }

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
        descriptors.Single(descriptor => descriptor.ToolName == "feedback_report")
            .Path.ShouldBe(["feedback", "report"]);

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
        descriptor.Definition.ShouldBe(OutlineCommandDefinition.Definition);
        descriptor.Port.RequestType.ShouldBe(definition.RequestType);
        descriptor.Port.ResponseType.ShouldBe(definition.ResponseType);
        definition.Safety.ShouldBe(CommandSafety.ReadOnly);
        descriptor.Safety.ShouldBe(CommandSafetyPolicy.ReadOnly);
        DaemonOwnedCommands.Contains(descriptor.ToolName).ShouldBeTrue();
    }

    [Fact]
    public void migrated_port_schemas_use_request_metadata_without_instantiating_a_cli_command()
    {
        var descriptor = new ApplicationCommandDescriptor(
            ["refactor", "add-dependency"],
            AddDependencyCommandDefinition.Name,
            "Inject a dependency.",
            AddDependencyCommandDefinition.Info.WhenToUse,
            () => throw new InvalidOperationException("The CLI builder must not run."),
            McpVisible: true,
            CommandSafetyPolicy.DryRunByDefault,
            Structured: true,
            Port: null,
            AddDependencyCommandDefinition.Definition);

        var tool = ToolSchemaBuilder.Build(descriptor);

        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        var required = (string[])tool.inputSchema["required"];
        required.ShouldBe(["type", "dependency"], ignoreOrder: true);
        properties.Keys.ShouldContain("dry-run");
        properties["type"]["description"].ShouldBe("Target class name");
        properties["dependency"]["description"].ShouldBe(
            "Dependency type to inject (e.g. IEmailService)");
    }

    [Fact]
    public void migrated_port_schemas_exclude_transport_ignored_request_properties()
    {
        var info = new RtCommandInfo(
            "schema-probe",
            Mutates: false,
            JsonOutput: true,
            "inspect",
            "Probe request schema generation.",
            []);
        var definition = new ApplicationCommandDefinition(
            info,
            typeof(SchemaProbeRequest),
            typeof(string),
            mcpVisible: true,
            CommandSafety.ReadOnly,
            CommandExecutionPlacement.InProcess,
            []);
        var descriptor = new ApplicationCommandDescriptor(
            ["inspect", "schema-probe"],
            definition.Name,
            "Probe request schema generation.",
            info.WhenToUse,
            () => throw new InvalidOperationException("The CLI builder must not run."),
            McpVisible: true,
            CommandSafetyPolicy.ReadOnly,
            Structured: true,
            Port: null,
            definition);

        var tool = ToolSchemaBuilder.Build(descriptor);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];

        properties.Keys.ShouldContain("visible");
        properties.Keys.ShouldNotContain("internalState");
    }

    [Fact]
    public void outline_schema_uses_host_neutral_property_descriptions()
    {
        using var provider = McpTestHost.CreateProvider();
        var tool = provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .Single(candidate => candidate.name == OutlineCommandDefinition.Name);

        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        properties["target"]["description"].ShouldBe(
            "File path, directory, glob pattern, or namespace to outline");
        properties["withLocations"]["description"].ShouldBe(
            "Include grouped members with per-member line ranges");
    }

    [Fact]
    public void safe_delete_schema_requires_the_symbol_and_projects_the_typed_port()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == SafeDeleteCommandDefinition.Definition.Name);
        var tool = ToolSchemaBuilder.Build(descriptor);

        descriptor.Port.ShouldBeOfType<SafeDeleteOperation>();
        descriptor.Definition.ShouldBeSameAs(SafeDeleteCommandDefinition.Definition);
        descriptor.WhenToUse.ShouldContain("Use this", Case.Sensitive);
        descriptor.WhenToUse.ShouldContain("Edit", Case.Sensitive);
        ((string[])tool.inputSchema["required"]).ShouldBe(["symbol"]);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        properties.Keys.ShouldBe(["symbol", "kind", "solution", "dry-run"], ignoreOrder: true);
        properties["symbol"]["description"].ShouldBe(
            "Type or member FQN or simple name to delete");
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

    private static string JsonPropertyName(System.Reflection.PropertyInfo property) =>
        property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), true)
            .Cast<JsonPropertyNameAttribute>()
            .SingleOrDefault()?.Name
        ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

    private sealed record SchemaProbeRequest(
        string Visible,
        [property: JsonIgnore] string InternalState);
}
