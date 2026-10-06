using System.Runtime.Loader;
using RoslynTools.Cli.Suggest;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Architecture;

/// <summary>
/// Independent discovery and policy contracts for the shared host metadata.
/// These tests need no workspace, child CLI process, or cached production answers.
/// </summary>
public sealed class HostMetadataContractTests
{
    private static readonly (string Name, string Value)[] OutputDefaults =
    [
        ("Format", "Text"), ("Level", "Default"), ("RelaxJson", "False"),
        ("ShowWorkspaceWarnings", "False"), ("FullWorkspaceEvidence", "False"), ("NoSuggest", "False")
    ];

    [Theory]
    [InlineData("config", "inspect", false, "CategoryAndRoot")]
    [InlineData("trust", "control", false, "RootOnly")]
    [InlineData("semantic-diff", "analyze", false, "CategoryAndRoot")]
    [InlineData("feedback", "meta", true, "CategoryAndRoot")]
    public void discovery_preserves_command_identity_and_placement(
        string name, string category, bool mutates, string placement)
    {
        var info = Command(name);

        info.Name.ShouldBe(name);
        info.Category.ShouldBe(category);
        info.Mutates.ShouldBe(mutates);
        info.JsonOutput.ShouldBeTrue();
        info.Placement.ToString().ShouldBe(placement);
        info.Group.ShouldBeNull();
    }

    [Theory]
    [InlineData("config")]
    [InlineData("trust")]
    [InlineData("semantic-diff")]
    public void local_inspection_advertises_only_text_and_json(string name)
    {
        Command(name).SupportedFormats.ShouldBe([OutputFormat.Text, OutputFormat.Json]);
    }

    [Fact]
    public void aliases_and_related_commands_remain_actionable()
    {
        HostCommandMetadata.SemanticDiff.Aliases.ShouldBe(["sdiff"]);
        HostCommandMetadata.Config.SeeAlso.ShouldBe(["doctor", "trust"]);
        HostCommandMetadata.Trust.SeeAlso.ShouldBe(["doctor"]);
    }

    [Theory]
    [InlineData("config", false, "Idempotent", 100)]
    [InlineData("trust", true, "Conditional", 1000)]
    public void local_policy_preserves_effects_authority_and_resource_limits(
        string name, bool conditionallyMutable, string idempotency, int maximumItems)
    {
        var product = name == "config" ? HostCommandMetadata.ConfigProduct : HostCommandMetadata.TrustProduct;
        var effects = CommandEffects.RepositoryRead | CommandEffects.LocalStateRead;
        if (name == "trust")
            effects |= CommandEffects.LocalStateWrite;

        product.Effects.ShouldBe(effects);
        product.ConditionallyMutable.ShouldBe(conditionallyMutable);
        product.Idempotency.ToString().ShouldBe(idempotency);
        product.Maturity.ShouldBe(CapabilityMaturity.Preview);
        product.Audience.ShouldBe(CommandAudience.Public);
        product.TrustRequirement.ShouldBe(TrustRequirement.None);
        product.WorkspaceRequirement.ShouldBe(WorkspaceRequirement.None);
        product.Prerequisites.ShouldBeEmpty();
        product.Cost.ShouldBe(CommandCost.Low);
        product.Cancellation.ShouldBe(CancellationSupport.NotSupported);
        product.Limits.DefaultTimeoutMilliseconds.ShouldBe(30_000);
        product.Limits.MaximumItems.ShouldBe(maximumItems);
        product.Limits.MaximumOutputBytes.ShouldBe(1_048_576);
    }

    [Fact]
    public void preview_policies_disclose_unenforced_limits_and_trust_constraints()
    {
        HostCommandMetadata.ConfigProduct.Limitations.Single()
            .ShouldContain("does not yet enforce the advertised timeout, item count, or output byte limit");
        var limitations = HostCommandMetadata.TrustProduct.Limitations;
        limitations.Count.ShouldBe(4);
        limitations[0].ShouldContain("Add and status require a Git worktree");
        limitations[0].ShouldContain("list and removal by stored repository id do not");
        limitations[1].ShouldContain("synchronous registry and Git operations cannot be cancelled");
        limitations[2].ShouldContain("do not yet enforce the advertised timeout, item count, or output byte limit");
        limitations[3].ShouldContain("does not yet implement ADR-0011 leases, audit history, quotas, or evaluator isolation");
    }

    [Fact]
    public void advertised_examples_identify_supported_modes_and_verbs()
    {
        string[] config = ["rt config", "rt config --json"];
        string[] trust = ["rt trust add .", "rt trust list", "rt trust status .", "rt trust remove ."];
        HostCommandMetadata.Config.Examples.ShouldBe(config);
        HostCommandMetadata.ConfigProduct.Examples.ShouldBe(config);
        HostCommandMetadata.Trust.Examples.ShouldBe(trust);
        HostCommandMetadata.TrustProduct.Examples.ShouldBe(trust);
        HostCommandMetadata.SemanticDiff.Examples.ShouldBe([
            "rt analyze semantic-diff --from HEAD~1 --to HEAD",
            "rt analyze semantic-diff",
            "rt analyze sdiff --from main --to HEAD",
            "rt analyze semantic-diff --from v1.0 --to v1.1 --ignore-accessibility",
            "rt analyze semantic-diff --no-detect-renames",
            "rt analyze semantic-diff --from HEAD~1 --to HEAD --json",
            "rt analyze semantic-diff old/Widget.cs new/Widget.cs",
            "rt analyze sdiff before.cs after.cs --json"]);
        HostCommandMetadata.Feedback.Examples.ShouldBe([
            "rt maintainer feedback add --description \"rename should also preview related symbols\"",
            "rt maintainer feedback list --status pending"]);
    }

    [Theory]
    [InlineData("config", "invalid rt configuration file")]
    [InlineData("config", "without a solution or repository trust")]
    [InlineData("trust", "user-scoped trust for a reviewed Git repository")]
    [InlineData("trust", "list/status inspect trust")]
    [InlineData("trust", "explicit add/remove verb is the local decision")]
    [InlineData("feedback", "dogfood bugs, missing features, and unexpected command behavior")]
    [InlineData("feedback", "rt maintainer feedback add")]
    public void discovery_explains_the_trigger_and_route(string name, string contract)
    {
        Command(name).WhenToUse.ShouldContain(contract);
    }

    [Theory]
    [InlineData("Compare C# declarations between Git refs")]
    [InlineData("or between source files")]
    [InlineData("Working-tree comparison requires Linux x64")]
    [InlineData("on other platforms use two Git refs (--from and --to) or two .cs files")]
    [InlineData("No --solution")]
    [InlineData("partially-broken tree")]
    [InlineData("ignores comments and whitespace")]
    [InlineData("moved to another file")]
    [InlineData("Renamed entry")]
    [InlineData("default: working tree vs HEAD")]
    [InlineData("non-HEAD working-tree baseline")]
    [InlineData("exactly two .cs file paths")]
    [InlineData("needs no git repository")]
    [InlineData("git-diff-style text report")]
    [InlineData("--json emits a stable array")]
    [InlineData("Added|Removed|Changed|Renamed|Unchanged")]
    [InlineData("oldPath, newPath, oldFqn, newFqn")]
    [InlineData("MCP semantic_diff handler")]
    public void semantic_diff_discloses_its_modes_and_output_contract(string contract)
    {
        HostCommandMetadata.SemanticDiff.WhenToUse.ShouldContain(contract);
    }

    [Fact]
    public void semantic_diff_discloses_rename_and_working_tree_limitations()
    {
        var issues = HostCommandMetadata.SemanticDiff.KnownIssues.ShouldNotBeNull();
        issues.Length.ShouldBe(2);
        issues[0].ShouldContain("Rename pairing is greedy within a container");
        issues[0].ShouldContain("can pair arbitrarily (deterministic by FQN order)");
        issues[0].ShouldContain("body change stays Added + Removed");
        issues[1].ShouldContain("staged changes, unstaged changes, and untracked files");
        issues[1].ShouldContain("not an unstaged-only comparison");
    }

    [Theory]
    [InlineData("show effective configuration", "config")]
    [InlineData("trust a repository", "trust")]
    [InlineData("record command feedback", "feedback")]
    public void real_catalogue_routes_local_intents_to_the_expected_command(string query, string expected)
    {
        var response = new SuggestService(new AppCommandCatalog()).Suggest(new SuggestRequest(query, Top: 3));

        response.Results.ShouldContain(result => result.Name == expected);
    }

    [Fact]
    public void a_fresh_output_context_starts_with_safe_defaults()
    {
        // Existing output fixtures assign defaults themselves. A separate load context
        // observes real initialization without changing other tests' process-wide state.
        WithFreshOutputContext(type =>
        {
            foreach (var (name, expected) in OutputDefaults)
                type.GetProperty(name)!.GetValue(null)!.ToString().ShouldBe(expected, name);
        });
    }

    [Theory]
    [InlineData("Format", "Json")]
    [InlineData("Level", "Detailed")]
    [InlineData("RelaxJson", "True")]
    [InlineData("ShowWorkspaceWarnings", "True")]
    [InlineData("FullWorkspaceEvidence", "True")]
    [InlineData("NoSuggest", "True")]
    public void output_context_properties_change_independently(string name, string value)
    {
        WithFreshOutputContext(type =>
        {
            var property = type.GetProperty(name)!;
            var original = property.GetValue(null);
            property.SetValue(null, property.PropertyType.IsEnum
                ? Enum.Parse(property.PropertyType, value)
                : bool.Parse(value));
            property.GetValue(null)!.ToString().ShouldBe(value);
            foreach (var (otherName, expected) in OutputDefaults)
            {
                if (otherName != name)
                    type.GetProperty(otherName)!.GetValue(null)!.ToString().ShouldBe(expected, otherName);
            }
            property.SetValue(null, original);
            property.GetValue(null).ShouldBe(original);
        });
    }

    private static void WithFreshOutputContext(Action<Type> assert)
    {
        var context = new AssemblyLoadContext("host-output-contract", isCollectible: true);
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(RtContext).Assembly.Location);
            var type = assembly.GetType("RoslynTools.Core.Output.RtContext", throwOnError: true)!;
            assert(type);
        }
        finally
        {
            context.Unload();
        }
    }

    private static RtCommandInfo Command(string name) => name switch
    {
        "config" => HostCommandMetadata.Config,
        "trust" => HostCommandMetadata.Trust,
        "semantic-diff" => HostCommandMetadata.SemanticDiff,
        "feedback" => HostCommandMetadata.Feedback,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown shared host command")
    };
}
