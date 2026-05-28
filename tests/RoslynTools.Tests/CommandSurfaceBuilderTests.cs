using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.CommandSurface;
using RoslynTools.Core.Output;
using Shouldly;

namespace RoslynTools.Tests;

/// <summary>
///     Unit tests for <see cref="CommandSurfaceBuilder.Build" /> — the pure projection of the
///     discovered command registry into a serializable <see cref="CommandSurfaceModel" />.
///     Exercised with synthetic registries so no MSBuild workspace is needed.
/// </summary>
public sealed class CommandSurfaceBuilderTests
{
    private static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>
    {
        ["inspect"] = "Read-only, single symbol or file",
        ["analyze"] = "Read-only, broader scans",
        ["refactor"] = "Mutations"
    };

    private static CommandSurfaceModel BuildOne(Command cmd, RtCommandInfo info, params Option[] globals) =>
        CommandSurfaceBuilder.Build(new[] { (cmd, info) }, Groups, globals);

    private static RtCommandInfo Info(
        string name,
        string category = "inspect",
        bool mutates = false,
        bool jsonOutput = false,
        string[]? examples = null,
        string[]? knownIssues = null,
        string[]? aliases = null,
        string[]? seeAlso = null,
        OutputFormat[]? supportedFormats = null,
        string? group = null) =>
        new(name, mutates, jsonOutput, category, "Use when needed.",
            examples ?? new[] { $"rt {category} {name}" },
            knownIssues, aliases, seeAlso, supportedFormats, group);

    [Fact]
    public void build_command_count_matches_registry_size()
    {
        var registry = new[]
        {
            (new Command("foo"), Info("foo")),
            (new Command("bar"), Info("bar", "analyze"))
        };

        var model = CommandSurfaceBuilder.Build(registry, Groups, Array.Empty<Option>());

        model.CommandCount.ShouldBe(2);
        model.Commands.Count.ShouldBe(2);
    }

    [Fact]
    public void build_maps_core_metadata_fields()
    {
        var model = BuildOne(
            new Command("rename", "Rename a symbol solution-wide"),
            Info("rename", "refactor", mutates: true, jsonOutput: false,
                examples: ["rt refactor rename A B"]));

        var cmd = model.Commands.Single();
        cmd.Name.ShouldBe("rename");
        cmd.Category.ShouldBe("refactor");
        cmd.Mutates.ShouldBeTrue();
        cmd.JsonOutput.ShouldBeFalse();
        cmd.WhenToUse.ShouldBe("Use when needed.");
        cmd.Description.ShouldBe("Rename a symbol solution-wide");
        cmd.Examples.ShouldBe(["rt refactor rename A B"]);
    }

    [Fact]
    public void build_maps_optional_metadata_when_present()
    {
        var model = BuildOne(
            new Command("describe"),
            Info("describe",
                aliases: ["desc"],
                seeAlso: ["outline"],
                knownIssues: ["slow on big solutions"],
                supportedFormats: [OutputFormat.Json, OutputFormat.Yaml],
                group: "inspect"));

        var cmd = model.Commands.Single();
        cmd.Aliases.ShouldBe(["desc"]);
        cmd.SeeAlso.ShouldBe(["outline"]);
        cmd.KnownIssues.ShouldBe(["slow on big solutions"]);
        cmd.SupportedFormats.ShouldBe(["Json", "Yaml"]);
        cmd.Group.ShouldBe("inspect");
    }

    [Fact]
    public void build_leaves_optional_metadata_null_when_absent()
    {
        var cmd = BuildOne(new Command("foo"), Info("foo")).Commands.Single();

        cmd.Aliases.ShouldBeNull();
        cmd.SeeAlso.ShouldBeNull();
        cmd.KnownIssues.ShouldBeNull();
        cmd.SupportedFormats.ShouldBeNull();
        cmd.Group.ShouldBeNull();
        // A command with no System.CommandLine description maps to an empty string, not null.
        cmd.Description.ShouldBe("");
    }

    [Fact]
    public void build_sorts_commands_by_name()
    {
        var registry = new[]
        {
            (new Command("zeta"), Info("zeta")),
            (new Command("alpha"), Info("alpha")),
            (new Command("mid"), Info("mid"))
        };

        var names = CommandSurfaceBuilder.Build(registry, Groups, Array.Empty<Option>())
            .Commands.Select(c => c.Name);

        names.ShouldBe(["alpha", "mid", "zeta"]);
    }

    [Fact]
    public void build_maps_option_value_types_to_friendly_names()
    {
        var command = new Command("x")
        {
            new Option<int>("--count"),
            new Option<int?>("--maybe"),
            new Option<long>("--big"),
            new Option<FileInfo>("--file"),
            new Option<DirectoryInfo>("--dir"),
            new Option<string[]>("--names"),
            new Option<Guid>("--id")
        };

        var options = BuildOne(command, Info("x")).Commands.Single().Options;

        options.Single(o => o.Aliases.Contains("--count")).ValueType.ShouldBe("int");
        options.Single(o => o.Aliases.Contains("--maybe")).ValueType.ShouldBe("int");
        options.Single(o => o.Aliases.Contains("--big")).ValueType.ShouldBe("long");
        options.Single(o => o.Aliases.Contains("--file")).ValueType.ShouldBe("file");
        options.Single(o => o.Aliases.Contains("--dir")).ValueType.ShouldBe("directory");
        options.Single(o => o.Aliases.Contains("--names")).ValueType.ShouldBe("string[]");
        options.Single(o => o.Aliases.Contains("--id")).ValueType.ShouldBe("Guid");
    }

    [Fact]
    public void build_normalises_empty_descriptions_to_null()
    {
        var command = new Command("x") { new Option<bool>("--flag", "") };
        command.AddArgument(new Argument<string>("a", string.Empty));

        var cmd = BuildOne(command, Info("x")).Commands.Single();

        cmd.Options.Single().Description.ShouldBeNull();
        cmd.Arguments.Single().Description.ShouldBeNull();
    }

    [Fact]
    public void build_extracts_options_name_aliases_required_and_valuetype()
    {
        var solutionOpt = new Option<string>("--solution", "Path to the .sln/.slnx") { IsRequired = true };
        solutionOpt.AddAlias("-s");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview only");

        var command = new Command("rename") { solutionOpt, dryRunOpt };
        var model = BuildOne(command, Info("rename", "refactor"));

        var options = model.Commands.Single().Options;
        options.Count.ShouldBe(2);

        var solution = options.Single(o => o.Aliases.Contains("--solution"));
        solution.Required.ShouldBeTrue();
        solution.ValueType.ShouldBe("string");
        solution.Description.ShouldBe("Path to the .sln/.slnx");
        // Aliases are ordinal-sorted ascending so JSON output is stable across runs.
        solution.Aliases.ShouldBe(["--solution", "-s"]);

        var dryRun = options.Single(o => o.Aliases.Contains("--dry-run"));
        dryRun.Required.ShouldBeFalse();
        dryRun.ValueType.ShouldBe("bool");
    }

    [Fact]
    public void build_extracts_arguments_with_required_derived_from_arity()
    {
        var requiredArg = new Argument<string>("target", "The thing to act on");
        var optionalArg = new Argument<string?>("extra", () => null, "Optional extra");

        var command = new Command("outline") { requiredArg, optionalArg };
        var args = BuildOne(command, Info("outline")).Commands.Single().Arguments;

        args.Count.ShouldBe(2);
        var target = args.Single(a => a.Name == "target");
        target.Required.ShouldBeTrue();
        target.ValueType.ShouldBe("string");
        target.Description.ShouldBe("The thing to act on");
        args.Single(a => a.Name == "extra").Required.ShouldBeFalse();
    }

    [Fact]
    public void build_maps_global_options()
    {
        var jsonOpt = new Option<bool>("--json", "Emit results as JSON");
        var decisionsOpt = new Option<FileInfo?>("--decisions", "Decisions file");

        var model = BuildOne(new Command("foo"), Info("foo"), jsonOpt, decisionsOpt);

        model.GlobalOptions.Count.ShouldBe(2);
        model.GlobalOptions.ShouldContain(o => o.Aliases.Contains("--json") && o.ValueType == "bool");
        model.GlobalOptions.ShouldContain(o => o.Aliases.Contains("--decisions") && o.ValueType == "file");
    }

    [Fact]
    public void build_passes_through_group_descriptions()
    {
        var model = BuildOne(new Command("foo"), Info("foo"));

        model.Groups.ShouldBe(Groups);
    }
}
