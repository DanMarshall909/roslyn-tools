using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Suggest;

/// <summary>
///     Tests for <see cref="SuggestService" /> — the host-agnostic surface that
///     CLI and MCP both call into. Uses an in-memory <see cref="ICommandCatalog" />
///     so tests don't depend on the real <c>App.Commands</c> registry.
/// </summary>
public sealed class SuggestServiceTests
{
    private static RtCommandInfo Info(
        string name,
        string category = "inspect",
        string whenToUse = "",
        string[]? examples = null) =>
        new(
            Name: name,
            Mutates: false,
            JsonOutput: false,
            Category: category,
            WhenToUse: whenToUse,
            Examples: examples ?? []);

    private sealed class FakeCatalog(params RtCommandInfo[] commands) : ICommandCatalog
    {
        public IReadOnlyList<RtCommandInfo> All { get; } = commands;
    }

    [Fact]
    public void returns_top_n_matches_in_descending_score()
    {
        var catalog = new FakeCatalog(
            Info("rename", whenToUse: "Rename a symbol across the solution"),
            Info("safe-delete", whenToUse: "Delete an unused symbol safely"),
            Info("outline", whenToUse: "Print a JSON outline of a type or namespace"));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "rename symbol", Top: 2));

        response.Results.Count.ShouldBe(2);
        response.Results[0].Name.ShouldBe("rename");
        response.Results[0].Score.ShouldBeGreaterThan(response.Results[1].Score);
    }

    [Fact]
    public void result_carries_metadata_for_rendering()
    {
        var catalog = new FakeCatalog(
            Info("rename",
                category: "refactor",
                whenToUse: "Rename a symbol across the solution",
                examples: ["rt refactor rename Foo Bar", "rt refactor rename ProjectX.Foo Baz"]));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "rename", Top: 1));

        var hit = response.Results[0];
        hit.Name.ShouldBe("rename");
        hit.Category.ShouldBe("refactor");
        hit.WhenToUse.ShouldBe("Rename a symbol across the solution");
        hit.PrimaryExample.ShouldBe("rt refactor rename Foo Bar");
        hit.AllExamples.Count.ShouldBe(2);
    }

    [Fact]
    public void primary_example_is_empty_when_command_has_no_examples()
    {
        var catalog = new FakeCatalog(
            Info("rename", whenToUse: "Rename a symbol"));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "rename", Top: 1));

        response.Results[0].PrimaryExample.ShouldBe("");
        response.Results[0].AllExamples.ShouldBeEmpty();
    }

    [Fact]
    public void empty_query_returns_empty_results()
    {
        var catalog = new FakeCatalog(
            Info("rename", whenToUse: "Rename a symbol"));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "", Top: 3));

        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Outcome.ShouldBe(SuggestOutcome.NoMatch);
    }

    [Fact]
    public void synonyms_route_remove_to_delete_unreferenced()
    {
        // High-traffic LLM mismatch: a user/agent says "remove" but the command
        // is "delete-unreferenced". The seeded synonym map must close this gap.
        var catalog = new FakeCatalog(
            Info("delete-unreferenced", whenToUse: "Prepare a deletion candidate with no loaded references"),
            Info("rename", whenToUse: "Rename a symbol across the solution"));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "remove", Top: 1));

        response.Results.Count.ShouldBe(1);
        response.Results[0].Name.ShouldBe("delete-unreferenced");
    }

    [Fact]
    public void related_is_empty_when_top_result_has_no_see_also()
    {
        var catalog = new FakeCatalog(
            Info("rename", whenToUse: "Rename a symbol"));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "rename", Top: 1));

        response.Related.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Verify a prepared plan without applying")]
    [InlineData("Validate an existing change plan before it is applied")]
    [InlineData("Verify a refactoring plan without applying it")]
    [InlineData("verify-plan")]
    public void unavailable_plan_verification_returns_unsupported_instead_of_a_command(string query)
    {
        var catalog = new FakeCatalog(
            Info("doctor", whenToUse: "Verify repository workspace readiness"),
            Info("add-dependency", category: "refactor", whenToUse: "Apply a dependency change"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(query));
        var payload = JsonSerializer.SerializeToElement(response);

        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        payload.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.GetProperty("MissingCapability").GetString().ShouldBe("verify-plan");
    }

    [Fact]
    public void unavailable_revision_bound_plan_preparation_does_not_recommend_a_mutator()
    {
        var catalog = new FakeCatalog(Info("rename", category: "refactor",
            whenToUse: "Rename a C# symbol across the solution"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Prepare a revision-bound rename plan before applying"));
        var payload = JsonSerializer.SerializeToElement(response);

        response.Results.ShouldBeEmpty();
        payload.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.GetProperty("MissingCapability").GetString().ShouldBe("prepare-plan");
    }

    [Fact]
    public void named_prepare_plan_capability_is_reported_as_unavailable()
    {
        var response = new SuggestService(new FakeCatalog()).Suggest(
            new SuggestRequest("prepare-plan"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("prepare-plan");
    }

    [Fact]
    public void immutable_plan_request_without_a_prepare_verb_is_still_unsupported()
    {
        var catalog = new FakeCatalog(Info("rename", category: "refactor",
            whenToUse: "Rename a C# symbol across the solution"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "I need an immutable revision-bound rename plan"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("prepare-plan");
        response.Results.ShouldBeEmpty();
    }

    [Fact]
    public void a_symbol_named_plan_does_not_trigger_the_plan_capability_guard()
    {
        var catalog = new FakeCatalog(Info("relationships",
            whenToUse: "Show references to a method"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Show references to the method named Plan before I review it"));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe("relationships");
    }

    [Fact]
    public void a_plan_class_is_not_mistaken_for_a_change_plan_artifact()
    {
        var catalog = new FakeCatalog(Info("extract-interface",
            whenToUse: "Create an interface for a class"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Create an interface for the Plan class"));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe("extract-interface");
    }

    [Fact]
    public void supported_rename_query_still_returns_ranked_results()
    {
        var catalog = new FakeCatalog(Info("rename", whenToUse: "Rename a symbol"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest("rename symbol"));
        var payload = JsonSerializer.SerializeToElement(response);

        response.Results.Single().Name.ShouldBe("rename");
        payload.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
    }

}
