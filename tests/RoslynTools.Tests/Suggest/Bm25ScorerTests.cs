using RoslynTools.Core;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Suggest;

/// <summary>
///     Pure-function tests for the BM25 ranker that powers <c>rt suggest</c> and
///     the unknown-command fallback. Catalogs are synthetic so the scorer is
///     exercised in isolation from the real command registry.
/// </summary>
public sealed class Bm25ScorerTests
{
    private static RtCommandInfo Info(string name, string whenToUse = "", string[]? examples = null) =>
        new(
            Name: name,
            Mutates: false,
            JsonOutput: false,
            Category: "inspect",
            WhenToUse: whenToUse,
            Examples: examples ?? []);

    [Fact]
    public void query_matching_name_ranks_that_command_first()
    {
        // Both commands mention "symbol" in their whenToUse so both score >0, but
        // only `rename` has "rename" in its name field — the higher Name weight
        // should put it on top.
        var catalog = new[]
        {
            Info("rename",      whenToUse: "Rename a symbol across the solution"),
            Info("safe-delete", whenToUse: "Delete a symbol if it has no references"),
        };

        var results = Bm25Scorer.Score(catalog, query: "rename symbol", top: 2);

        results.Count.ShouldBe(2);
        results[0].Command.Name.ShouldBe("rename");
        results[0].Score.ShouldBeGreaterThan(results[1].Score);
    }

    [Fact]
    public void query_with_no_matches_returns_empty()
    {
        var catalog = new[] { Info("rename", whenToUse: "Rename a symbol") };

        var results = Bm25Scorer.Score(catalog, query: "xyzzy fnord", top: 5);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void stopwords_in_query_do_not_bias_ranking()
    {
        var catalog = new[]
        {
            Info("rename",      whenToUse: "rename a symbol"),
            Info("safe-delete", whenToUse: "delete a symbol"),
        };

        var results = Bm25Scorer.Score(catalog, query: "the the the rename", top: 2);

        results[0].Command.Name.ShouldBe("rename");
    }

    [Fact]
    public void synonyms_expand_name_field()
    {
        var catalog = new[]
        {
            Info("safe-delete", whenToUse: "Delete a symbol only if it has no references"),
            Info("rename",      whenToUse: "Rename a symbol across the solution"),
        };
        var synonyms = new Dictionary<string, string[]>
        {
            ["safe-delete"] = ["safe-delete", "remove"],
        };

        var results = Bm25Scorer.Score(catalog, query: "remove", top: 2, nameSynonyms: synonyms);

        results[0].Command.Name.ShouldBe("safe-delete");
    }

    [Fact]
    public void name_field_outweighs_whentouse_field()
    {
        var catalog = new[]
        {
            Info("rename",        whenToUse: "Move a symbol to a new namespace"),
            Info("other-command", whenToUse: "Rename and refactor stuff inline"),
        };

        var results = Bm25Scorer.Score(catalog, query: "rename", top: 2);

        results[0].Command.Name.ShouldBe("rename");
    }

    [Fact]
    public void top_caps_result_count()
    {
        var catalog = Enumerable.Range(1, 10)
            .Select(i => Info($"cmd{i}", whenToUse: "rename refactor extract"))
            .ToArray();

        var results = Bm25Scorer.Score(catalog, query: "rename", top: 3);

        results.Count.ShouldBe(3);
    }

    [Fact]
    public void empty_query_returns_empty()
    {
        var catalog = new[] { Info("rename", whenToUse: "rename a symbol") };

        var results = Bm25Scorer.Score(catalog, query: "", top: 5);

        results.ShouldBeEmpty();
    }

    [Fact]
    public void query_is_case_insensitive()
    {
        var catalog = new[]
        {
            Info("rename",      whenToUse: "Rename a symbol"),
            Info("safe-delete", whenToUse: "Delete a symbol"),
        };

        var results = Bm25Scorer.Score(catalog, query: "RENAME", top: 2);

        results[0].Command.Name.ShouldBe("rename");
    }

    [Fact]
    public void example_field_contributes_below_whentouse()
    {
        // Only examples contain the query token. Verify the command is still returned
        // (Examples field has weight > 0) but ranks below an equivalent whenToUse hit.
        var catalog = new[]
        {
            Info("only-in-examples",  whenToUse: "Unrelated",       examples: ["rt only-in-examples utcnow"]),
            Info("only-in-whentouse", whenToUse: "uses utcnow here", examples: ["rt only-in-whentouse"]),
        };

        var results = Bm25Scorer.Score(catalog, query: "utcnow", top: 2);

        results.Count.ShouldBe(2);
        results[0].Command.Name.ShouldBe("only-in-whentouse");
        results[1].Command.Name.ShouldBe("only-in-examples");
    }
}
