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
    }

    [Fact]
    public void synonyms_route_remove_to_safe_delete()
    {
        // High-traffic LLM mismatch: a user/agent says "remove" but the command
        // is "safe-delete". The seeded synonym map must close this gap.
        var catalog = new FakeCatalog(
            Info("safe-delete", whenToUse: "Delete a symbol only if it has no references"),
            Info("rename", whenToUse: "Rename a symbol across the solution"));
        var service = new SuggestService(catalog);

        var response = service.Suggest(new SuggestRequest(Query: "remove", Top: 1));

        response.Results.Count.ShouldBe(1);
        response.Results[0].Name.ShouldBe("safe-delete");
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

}
