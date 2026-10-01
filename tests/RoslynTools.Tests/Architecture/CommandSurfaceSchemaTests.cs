using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using RoslynTools.Core;
using RoslynTools.Core.CommandSurface;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

/// <summary>
///     Keeps <c>docs/command-surface.schema.json</c> honest. The schema is the published contract
///     for <c>rt dump-help --json</c>; these tests fail the build if the schema drifts from the
///     <see cref="CommandSurfaceModel" /> record shape, or if the serializer stops producing the
///     camelCase, null-omitting JSON the schema describes.
/// </summary>
public sealed class CommandSurfaceSchemaTests
{
    private static readonly JsonObject Schema = LoadSchema();

    private static JsonObject LoadSchema()
    {
        var path = Path.Combine(FindRepoRoot(), "docs", "command-surface.schema.json");
        return JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    }

    // ── drift guard: schema $defs must match the record shapes exactly ──────────────

    [Fact]
    public void schema_root_matches_command_surface_model()
        => AssertObjectMatchesRecord(Schema, typeof(CommandSurfaceModel));

    [Fact]
    public void schema_command_def_matches_command_descriptor()
        => AssertObjectMatchesRecord(Schema["$defs"]!["command"]!.AsObject(), typeof(CommandDescriptor));

    [Fact]
    public void schema_option_def_matches_option_descriptor()
        => AssertObjectMatchesRecord(Schema["$defs"]!["option"]!.AsObject(), typeof(OptionDescriptor));

    [Fact]
    public void schema_product_def_matches_product_descriptor()
        => AssertObjectMatchesRecord(
            Schema["$defs"]!["product"]!.AsObject(),
            typeof(CommandProductDescriptor));

    [Fact]
    public void schema_mode_definitions_match_mode_descriptors()
    {
        AssertObjectMatchesRecord(Schema["$defs"]!["mode"]!.AsObject(), typeof(CommandModeDescriptor));
        AssertObjectMatchesRecord(Schema["$defs"]!["modeSelector"]!.AsObject(), typeof(CommandModeSelector));
    }

    [Fact]
    public void schema_argument_def_matches_argument_descriptor()
        => AssertObjectMatchesRecord(Schema["$defs"]!["argument"]!.AsObject(), typeof(ArgumentDescriptor));

    [Fact]
    public void schema_category_enum_matches_known_categories()
    {
        var categoryEnum = Schema["$defs"]!["command"]!["properties"]!["category"]!["enum"]!
            .AsArray().Select(n => n!.GetValue<string>());

        categoryEnum.ShouldBe(App.CategoryDescriptions.Keys, ignoreOrder: true);
    }

    [Fact]
    public void generated_public_catalog_uses_schema_categories_and_omits_maintainer_prepare()
    {
        var categoryEnum = Schema["$defs"]!["command"]!["properties"]!["category"]!["enum"]!
            .AsArray().Select(node => node!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var registry = App.CatalogCommands
            .Select(entry => (Cmd: entry.Build(), entry.Info))
            .ToArray();
        var json = CommandSurfaceBuilder.ToJson(CommandSurfaceBuilder.Build(
            registry,
            App.GroupDescriptions,
            []));
        var root = JsonNode.Parse(json)!.AsObject();
        var commands = root["commands"]!.AsArray();

        commands.Select(node => node!["category"]!.GetValue<string>())
            .ShouldAllBe(category => categoryEnum.Contains(category));
        commands.ShouldNotContain(node =>
            node!["name"]!.GetValue<string>() == "prepare-operation");
        root["groups"]!.AsObject().Select(property => property.Key)
            .ShouldBe(App.GroupDescriptions.Keys, ignoreOrder: true);
    }

    [Fact]
    public void schema_supported_formats_enum_matches_output_format_enum()
    {
        var formatEnum = Schema["$defs"]!["command"]!["properties"]!["supportedFormats"]!["items"]!["enum"]!
            .AsArray().Select(n => n!.GetValue<string>());

        formatEnum.ShouldBe(Enum.GetNames<OutputFormat>(), ignoreOrder: true);
    }

    private static void AssertObjectMatchesRecord(JsonObject schemaObject, Type recordType)
    {
        schemaObject["additionalProperties"]!.GetValue<bool>()
            .ShouldBeFalse($"{recordType.Name}'s schema must be closed (additionalProperties: false)");

        var props = recordType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.Name != "EqualityContract")
            .ToList();

        var nullability = new NullabilityInfoContext();
        var expectedProperties = props.Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name)).ToList();
        var expectedRequired = props
            .Where(p => nullability.Create(p).ReadState != NullabilityState.Nullable)
            .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToList();

        var schemaProperties = schemaObject["properties"]!.AsObject().Select(kvp => kvp.Key);
        var schemaRequired = schemaObject["required"]!.AsArray().Select(n => n!.GetValue<string>());

        schemaProperties.ShouldBe(expectedProperties, ignoreOrder: true,
            $"{recordType.Name}: schema properties must match record properties");
        schemaRequired.ShouldBe(expectedRequired, ignoreOrder: true,
            $"{recordType.Name}: schema 'required' must be exactly the non-nullable record properties");
    }

    // ── JSON shape: serializer output must match what the schema describes ───────────

    [Fact]
    public void to_json_emits_camelcase_keys_and_string_formats()
    {
        var model = new CommandSurfaceModel(
            1,
            [
                new CommandDescriptor(
                    "rename", "refactor", true, false, "when to use it", "Rename a symbol",
                    ["rt refactor rename A B"],
                    [new OptionDescriptor("solution", ["--solution", "-s"], true, "string", "Path")],
                    [new ArgumentDescriptor("target", true, "string", "the target")],
                    Product(),
                    Group: "refactor",
                    Aliases: ["rn"],
                    SupportedFormats: ["Text", "Json"])
            ],
            [new OptionDescriptor("json", ["--json"], false, "bool", "Emit JSON")],
            new Dictionary<string, string> { ["refactor"] = "Mutations" });

        var root = JsonNode.Parse(CommandSurfaceBuilder.ToJson(model))!.AsObject();

        root["commandCount"]!.GetValue<int>().ShouldBe(1);
        root.ContainsKey("globalOptions").ShouldBeTrue();
        root.ContainsKey("groups").ShouldBeTrue();

        var cmd = root["commands"]!.AsArray()[0]!.AsObject();
        cmd.ContainsKey("jsonOutput").ShouldBeTrue();
        cmd.ContainsKey("whenToUse").ShouldBeTrue();
        cmd["supportedFormats"]!.AsArray().Select(n => n!.GetValue<string>()).ShouldBe(["Text", "Json"]);

        var option = cmd["options"]!.AsArray()[0]!.AsObject();
        option["valueType"]!.GetValue<string>().ShouldBe("string");
        option["required"]!.GetValue<bool>().ShouldBeTrue();

        cmd["arguments"]!.AsArray()[0]!["valueType"]!.GetValue<string>().ShouldBe("string");
    }

    [Fact]
    public void to_json_is_pretty_printed_with_indentation()
    {
        var model = new CommandSurfaceModel(0, [], [], new Dictionary<string, string>());

        CommandSurfaceBuilder.ToJson(model).ShouldContain("\n");
    }

    [Fact]
    public void to_json_is_strict_by_default_and_relaxes_only_when_requested()
    {
        var model = new CommandSurfaceModel(
            0, [], [], new Dictionary<string, string> { ["g"] = "→" });

        var previous = RtContext.RelaxJson;
        try
        {
            RtContext.RelaxJson = false;
            var strict = CommandSurfaceBuilder.ToJson(model);
            strict.ShouldNotContain("→");
            strict.ShouldContain("\\u2192");

            RtContext.RelaxJson = true;
            CommandSurfaceBuilder.ToJson(model).ShouldContain("→");
        }
        finally
        {
            RtContext.RelaxJson = previous;
        }
    }

    [Fact]
    public void to_json_omits_absent_optional_fields_but_keeps_required_arrays()
    {
        var model = new CommandSurfaceModel(
            1,
            [new CommandDescriptor("foo", "inspect", false, false, "w", "d", ["ex"], [], [], Product())],
            [],
            new Dictionary<string, string>());

        var cmd = JsonNode.Parse(CommandSurfaceBuilder.ToJson(model))!["commands"]!.AsArray()[0]!.AsObject();

        cmd.ContainsKey("aliases").ShouldBeFalse();
        cmd.ContainsKey("group").ShouldBeFalse();
        cmd.ContainsKey("seeAlso").ShouldBeFalse();
        cmd.ContainsKey("knownIssues").ShouldBeFalse();
        cmd.ContainsKey("supportedFormats").ShouldBeFalse();

        cmd["options"]!.AsArray().Count.ShouldBe(0);
        cmd["arguments"]!.AsArray().Count.ShouldBe(0);
    }

    private static CommandProductDescriptor Product() => new(
        "Preview",
        ["RepositoryRead"],
        false,
        "Idempotent",
        "Public",
        "ExplicitTrustedRepository",
        "Semantic",
        [".NET SDK"],
        "Medium",
        "Cooperative",
        30_000,
        100,
        1_048_576,
        []);

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RoslynTools.slnx")))
                return dir.FullName;

        throw new InvalidOperationException("Could not locate RoslynTools.slnx walking up from " + AppContext.BaseDirectory);
    }
}
