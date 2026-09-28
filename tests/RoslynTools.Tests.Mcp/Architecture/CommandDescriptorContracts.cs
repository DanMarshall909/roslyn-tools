using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using RoslynTools.Services.SemanticReview;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class CommandDescriptorContracts
{
    [Fact]
    public void discoverable_mcp_commands_require_application_ports()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>().All;
        var hostOptions = provider.GetRequiredService<McpHostOptions>();
        var outline = descriptors.Single(descriptor => descriptor.ToolName == "outline");

        descriptors.Where(descriptor => descriptor.McpVisible)
            .ShouldAllBe(descriptor => descriptor.Port != null);
        descriptors.Where(descriptor => descriptor.McpVisible
                                        && descriptor.Port is ILegacyCommandApplicationPort)
            .Select(descriptor => descriptor.ToolName)
            .ShouldBeEmpty();
        McpToolAvailability.IsAvailable(outline, hostOptions).ShouldBeTrue();
        McpToolAvailability.IsAvailable(outline with { Port = null }, hostOptions)
            .ShouldBeFalse();
    }

    [Fact]
    public void mcp_host_has_no_cli_command_bridge_or_console_capture()
    {
        typeof(McpCommandAdapter).Assembly
            .GetType("RoslynTools.Mcp.Tools.CliCommandBridge")
            .ShouldBeNull();
        typeof(McpCommandAdapter).Assembly
            .GetType("RoslynTools.Mcp.Tools.CapturingConsole")
            .ShouldBeNull();
    }

    [Fact]
    public void mcp_registry_has_only_the_generic_dispatch_adapter()
    {
        typeof(ToolRegistry).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Select(field => field.FieldType)
            .ShouldBe([typeof(ToolSchemaBuilder), typeof(McpCommandAdapter)], ignoreOrder: true);
        typeof(ToolRegistry).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(ToolRegistry).Namespace)
            .ShouldNotContain(type => type.Name.EndsWith("Handler", StringComparison.Ordinal));
    }

    [Fact]
    public void local_trust_control_plane_is_not_an_mcp_descriptor_or_tool()
    {
        using var provider = McpTestHost.CreateProvider(CreateTrustedHostOptions());

        provider.GetRequiredService<CommandDescriptorRegistry>().All
            .ShouldNotContain(descriptor => descriptor.ToolName == "trust");
        provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .ShouldNotContain(tool => tool.name == "trust");
    }

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
    public void application_descriptor_contract_contains_no_command_line_types()
    {
        var descriptorTypes = new[]
        {
            typeof(ApplicationCommandDescriptor),
            typeof(ApplicationCommandInput),
            typeof(ApplicationCommandParameter)
        };

        var members = descriptorTypes.SelectMany(type => type
            .GetMembers(BindingFlags.Instance | BindingFlags.Public)
            .OfType<PropertyInfo>()
            .Select(property => property.PropertyType));

        members.ShouldNotContain(type =>
            type.Namespace != null
            && type.Namespace.StartsWith("System.CommandLine", StringComparison.Ordinal));
    }

    [Fact]
    public void every_mcp_visible_descriptor_has_a_unique_tool_and_generated_schema()
    {
        using var provider = McpTestHost.CreateProvider(CreateTrustedHostOptions());
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
    public void mutation_descriptors_require_startup_repository_trust_for_mcp_discovery()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(CreateTrustedHostOptions());
        var descriptors = trustedProvider.GetRequiredService<CommandDescriptorRegistry>().All;
        var mutations = descriptors
            .Where(descriptor => descriptor.Definition?.Safety.Effect == CommandEffect.Mutation
                                 && descriptor.Audience == CommandAudience.Public)
            .ToList();
        var untrustedTools = untrustedProvider.GetRequiredService<ToolSchemaBuilder>().Build();
        var trustedTools = trustedProvider.GetRequiredService<ToolSchemaBuilder>().Build();
        var mutationNames = mutations.Select(descriptor => descriptor.ToolName).ToHashSet(StringComparer.Ordinal);

        mutations.ShouldNotBeEmpty();
        mutations.ShouldAllBe(descriptor => descriptor.McpVisible);
        untrustedTools.ShouldNotContain(tool => mutationNames.Contains(tool.name));
        var trustedToolNames = trustedTools.Select(tool => tool.name).ToHashSet(StringComparer.Ordinal);
        foreach (var mutationName in mutationNames)
            trustedToolNames.ShouldContain(mutationName);
        foreach (var keyMutation in new[] { "move", "rename", "delete_unreferenced" })
            trustedToolNames.ShouldContain(keyMutation);
    }

    [Fact]
    public async Task mutation_dispatch_requires_startup_repository_trust()
    {
        using var arguments = JsonDocument.Parse("{}");
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(CreateTrustedHostOptions());

        var untrustedResult = await untrustedProvider.GetRequiredService<ToolRegistry>()
            .InvokeAsync("move", arguments.RootElement);
        var trustedResult = await trustedProvider.GetRequiredService<ToolRegistry>()
            .InvokeAsync("move", arguments.RootElement);

        untrustedResult.isError.ShouldBeTrue();
        untrustedResult.content.ShouldContain("Unknown tool");
        trustedResult.isError.ShouldBeTrue();
        trustedResult.content.ShouldNotContain("Unknown tool");
        trustedResult.content.ShouldContain("MoveRequest");
        trustedResult.content.ShouldContain("required properties");
    }

    [Fact]
    public void nested_feedback_operations_are_leaf_descriptors_and_operational_commands_are_hidden()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>().All;

        var add = descriptors.Single(descriptor => descriptor.ToolName == "feedback_add");
        var list = descriptors.Single(descriptor => descriptor.ToolName == "feedback_list");
        var process = descriptors.Single(descriptor => descriptor.ToolName == "feedback_process");
        var report = descriptors.Single(descriptor => descriptor.ToolName == "feedback_report");

        add.Path.ShouldBe(["maintainer", "feedback", "add"]);
        list.Path.ShouldBe(["maintainer", "feedback", "list"]);
        process.Path.ShouldBe(["maintainer", "feedback", "process"]);
        report.Path.ShouldBe(["maintainer", "feedback", "report"]);

        new[] { add, list, process, report }
            .ShouldAllBe(descriptor => !descriptor.McpVisible
                                       && descriptor.Audience == CommandAudience.Maintainer);
        add.Safety.ShouldBe(CommandSafetyPolicy.LocalWrite);
        add.Effects.ShouldBe(CommandEffects.LocalStateWrite);
        list.Safety.ShouldBe(CommandSafetyPolicy.ReadOnly);
        list.Effects.ShouldBe(CommandEffects.LocalStateRead);
        process.Safety.ShouldBe(CommandSafetyPolicy.LocalWrite);
        process.Effects.ShouldBe(CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
        report.Safety.ShouldBe(CommandSafetyPolicy.ExternalWriteReviewByDefault);
        report.Effects.ShouldBe(CommandEffects.LocalStateRead
                                | CommandEffects.LocalStateWrite
                                | CommandEffects.ExternalRead
                                | CommandEffects.ExternalWrite);

        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("daemon");
        descriptors.Select(descriptor => descriptor.ToolName)
            .ShouldNotContain("dump_help");
    }

    [Fact]
    public void probe_di_is_trusted_experimental_repository_code_execution_and_hidden_from_mcp()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == "probe_di");

        descriptor.McpVisible.ShouldBeFalse();
        descriptor.Path.ShouldBe(["trusted", "probe-di"]);
        descriptor.Audience.ShouldBe(CommandAudience.TrustedExperimental);
        descriptor.Effects.ShouldBe(
            CommandEffects.RepositoryRead | CommandEffects.RepositoryCodeExecution);
        provider.GetRequiredService<ToolSchemaBuilder>().Build()
            .ShouldNotContain(tool => tool.name == descriptor.ToolName);
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
            new ApplicationCommandInput([], []),
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
            new ApplicationCommandInput([], []),
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
    public void source_schema_exposes_related_symbol_inclusion_controls()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == SourceApplicationDefinition.Definition.Name);
        var tool = ToolSchemaBuilder.Build(descriptor);

        descriptor.Port.ShouldBeOfType<SourceOperation>();
        var definition = descriptor.Definition.ShouldNotBeNull();
        definition.ShouldBeSameAs(SourceApplicationDefinition.Definition);
        definition.ResponseType.ShouldBe(typeof(SourceResult));
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];
        properties.Keys.ShouldContain("includeRelated");
        properties.Keys.ShouldContain("relatedLimit");
        properties.Keys.ShouldContain("maxSourceBytes");
        properties.Keys.ShouldContain("continuationToken");
        properties["includeRelated"]["description"].ShouldBe(
            "Include one-hop source declarations for each listed related symbol");
        properties["relatedLimit"]["description"].ShouldBe(
            "Maximum related symbols to list (1-100)");
        properties["maxSourceBytes"]["description"].ShouldBe(
            "Maximum UTF-8 bytes across returned source text (4-1048576)");
        properties["continuationToken"]["description"].ShouldBe(
            "Opaque continuation from a prior truncated source response");
    }

    [Fact]
    public void delete_unreferenced_schema_requires_the_symbol_and_projects_the_typed_port()
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
            new ApplicationCommandInput([], []),
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
    public void move_schema_projects_the_explicit_mode_enum()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == MoveCommandDefinition.Definition.Name);
        var tool = ToolSchemaBuilder.Build(descriptor);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];

        descriptor.Port.ShouldBeOfType<MoveOperation>();
        descriptor.Definition.ShouldBeSameAs(MoveCommandDefinition.Definition);
        ((string[])properties["mode"]["enum"]).ShouldBe(
            ["TypeToNamespace", "NamespaceToNamespace", "TypeToProject"],
            ignoreOrder: true);
    }

    [Fact]
    public void rename_schema_projects_default_single_and_explicit_batch_request_shapes()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptor = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == RenameCommandDefinition.Definition.Name);
        var tool = ToolSchemaBuilder.Build(descriptor);
        using var schema = JsonDocument.Parse(JsonSerializer.Serialize(tool.inputSchema));

        var variants = schema.RootElement.GetProperty("oneOf").EnumerateArray().ToList();
        var single = variants.Single(variant =>
            variant.GetProperty("properties").GetProperty("mode")
                .GetProperty("enum")[0].GetString() == "Single");
        var batch = variants.Single(variant =>
            variant.GetProperty("properties").GetProperty("mode")
                .GetProperty("enum")[0].GetString() == "Batch");

        single.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(["symbol", "new-name"], ignoreOrder: true);
        single.GetProperty("not").GetProperty("anyOf").EnumerateArray()
            .Select(rule => rule.GetProperty("required")[0].GetString())
            .ShouldBe(["csv"]);

        batch.GetProperty("required").EnumerateArray()
            .Select(value => value.GetString())
            .ShouldBe(["mode", "csv"], ignoreOrder: true);
        batch.GetProperty("not").GetProperty("anyOf").EnumerateArray()
            .Select(rule => rule.GetProperty("required")[0].GetString())
            .ShouldBe(["symbol", "new-name"], ignoreOrder: true);
    }

    [Fact]
    public async Task feedback_list_is_not_invocable_through_the_normal_mcp_registry()
    {
        using var provider = McpTestHost.CreateProvider();
        using var arguments = JsonDocument.Parse("""{"status":"processed"}""");
        var result = await provider.GetRequiredService<ToolRegistry>()
            .InvokeAsync("feedback_list", arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("Unknown tool");
    }

    [Fact]
    public void solid_review_descriptor_projects_typed_opt_in_source_transmission_contract()
    {
        using var untrustedProvider = McpTestHost.CreateProvider();
        using var trustedProvider = McpTestHost.CreateProvider(CreateTrustedHostOptions());
        var descriptor = trustedProvider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Single(candidate => candidate.ToolName == SolidReviewCommandDefinition.Definition.Name);
        var definition = descriptor.Definition
            ?? throw new InvalidOperationException("solid_review must project a typed definition.");
        var tool = ToolSchemaBuilder.Build(descriptor);
        var properties = (Dictionary<string, Dictionary<string, object>>)tool.inputSchema["properties"];

        descriptor.Port.ShouldBeOfType<SolidReviewOperation>();
        definition.ShouldBeSameAs(SolidReviewCommandDefinition.Definition);
        descriptor.McpVisible.ShouldBeTrue();
        definition.ExecutionPlacement.ShouldBe(CommandExecutionPlacement.InProcess);
        descriptor.Effects.ShouldBe(CommandEffects.RepositoryRead | CommandEffects.ExternalWrite);
        properties.Keys.ShouldContain("dryRun");
        properties.Keys.ShouldContain("transmitSource");
        untrustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .ShouldNotContain(candidate => candidate.name == descriptor.ToolName);
        trustedProvider.GetRequiredService<ToolSchemaBuilder>().Build()
            .ShouldContain(candidate => candidate.name == descriptor.ToolName);
    }

    private static string JsonPropertyName(System.Reflection.PropertyInfo property) =>
        property.GetCustomAttributes(typeof(JsonPropertyNameAttribute), true)
            .Cast<JsonPropertyNameAttribute>()
            .SingleOrDefault()?.Name
        ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

    private sealed record SchemaProbeRequest(
        string Visible,
        [property: JsonIgnore] string InternalState);

    private static McpHostOptions CreateTrustedHostOptions() => new(FindRepoRoot());

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
