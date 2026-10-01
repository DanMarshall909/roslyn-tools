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

    [Theory]
    [InlineData("Find every implementation of IRtCommand before changing the interface")]
    [InlineData("Show all implementers of IRtCommand")]
    public void implementation_search_returns_one_invokable_read_workflow(string query)
    {
        var catalog = new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships"),
            Info("extract-interface", category: "refactor", whenToUse: "Extract an interface"),
            Info("implement-interface", category: "refactor", whenToUse: "Implement an interface"));
        var solutionPath = Path.GetFullPath("RoslynTools.slnx");
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            query, Top: 3, Solution: solutionPath));
        var payload = JsonSerializer.SerializeToElement(response);

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.ShouldBeEmpty();
        var step = payload.GetProperty("Workflow").GetProperty("Steps")[0];
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt inspect relationships implementations IRtCommand --solution {solutionPath} --json");
        step.GetProperty("McpTool").GetString().ShouldBe("relationships");
        step.GetProperty("McpArguments").GetProperty("symbol").GetString().ShouldBe("IRtCommand");
        step.GetProperty("McpArguments").GetProperty("mode").GetString().ShouldBe("Implementations");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(solutionPath);
        var stopCondition = step.GetProperty("StopCondition").GetString();
        stopCondition.ShouldNotBeNull();
        stopCondition.ShouldContain("Partial");
        stopCondition.ShouldContain("truncated");
    }

    [Fact]
    public void implementation_intent_is_unsupported_when_the_read_route_is_absent()
    {
        var catalog = new FakeCatalog(Info("extract-interface", category: "refactor",
            whenToUse: "Extract an interface"));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Find every implementation of IRtCommand"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.Results.ShouldBeEmpty();
        response.MissingCapability.ShouldBe("relationships-implementations");
    }

    [Fact]
    public void implementation_search_without_solution_keeps_cli_route_and_marks_mcp_unavailable()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships"))).Suggest(
            new SuggestRequest("Find all implementations of IRtCommand"));

        var step = response.Workflow!.Steps.Single();
        step.CliInvocation.ShouldBe("rt inspect relationships implementations IRtCommand --json");
        step.McpTool.ShouldBeNull();
        step.McpArguments.ShouldBeNull();
        step.McpUnavailableReason.ShouldBe(
            "MCP requires an absolute solution path; call suggest_command again with one.");
    }

    [Theory]
    [InlineData("Find every implementation of IHandler<T>")]
    [InlineData("Find every implementation of global::IRtCommand")]
    [InlineData("Find every implementation of the interface")]
    [InlineData("Find implementations of an interface")]
    [InlineData("Find implementations of that interface")]
    [InlineData("Find implementations of some interface")]
    [InlineData("Find implementations of IFoo and IBar")]
    [InlineData("Find implementations of IFoo, IBar")]
    public void incomplete_symbol_query_does_not_suggest_a_route_for_its_prefix(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships"))).Suggest(
            new SuggestRequest(query));

        response.Workflow.ShouldBeNull();
    }

    [Fact]
    public void relative_solution_from_shared_request_does_not_create_an_uninvokable_mcp_step()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships"))).Suggest(
            new SuggestRequest("Find every implementation of IRtCommand", Solution: "RoslynTools.slnx"));

        var step = response.Workflow!.Steps.Single();
        step.McpTool.ShouldBeNull();
        step.McpArguments.ShouldBeNull();
        step.McpUnavailableReason.ShouldBe(
            "MCP requires an absolute solution path; call suggest_command again with one.");
    }

    [Fact]
    public void interface_creation_does_not_trigger_implementation_search()
    {
        var catalog = new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships"),
            Info("extract-interface", category: "refactor", whenToUse: "Extract an interface"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Extract an interface from BookingService"));
        var payload = JsonSerializer.SerializeToElement(response);

        response.Results[0].Name.ShouldBe("extract-interface");
        payload.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("review my working tree C# changes")]
    [InlineData("semantic diff my uncommitted C# changes")]
    [InlineData("review my working tree C# changes before commit")]
    [InlineData("review my working tree C# changes before I stage them")]
    [InlineData("review C# changes from my working tree")]
    [InlineData("compare my uncommitted C# changes against HEAD")]
    [InlineData("review my working tree C# changes since HEAD")]
    [InlineData("review my working tree C# changes vs. HEAD")]
    public void working_tree_review_returns_one_invokable_semantic_diff_step(string query)
    {
        var solution = Path.GetFullPath("RoslynTools.slnx");
        var directory = Path.GetDirectoryName(solution)!;
        var service = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true);

        var response = service.Suggest(new SuggestRequest(query, Solution: solution));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.ShouldBeEmpty();
        var step = response.Workflow!.Steps.Single();
        step.CliInvocation.ShouldBe($"rt analyze semantic-diff --directory {directory} --json");
        step.McpTool.ShouldBe("semantic_diff");
        step.McpArguments!["directory"].ShouldBe(directory);
        step.StopCondition.ShouldContain("error");
    }

    [Fact]
    public void working_tree_review_without_solution_keeps_cli_step_and_marks_mcp_unavailable()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest("review my working tree C# changes"));

        var step = response.Workflow!.Steps.Single();
        step.CliInvocation.ShouldBe("rt analyze semantic-diff --json");
        step.McpTool.ShouldBeNull();
        step.McpArguments.ShouldBeNull();
        step.McpUnavailableReason!.ShouldContain("absolute solution path");
    }

    [Fact]
    public void unsupported_working_tree_review_does_not_emit_a_call()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: false)
            .Suggest(new SuggestRequest("review my working tree C# changes"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("semantic-diff-working-tree");
        response.UnsupportedReason!.ShouldContain("Linux x64");
        response.UnsupportedReason!.ShouldContain("Git refs");
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("compare my uncommitted C# changes against main")]
    [InlineData("review semantic C# working tree changes since v1.0")]
    [InlineData("review my working tree C# changes from HEAD~1")]
    [InlineData("review my working tree C# changes compared with main")]
    [InlineData("review my working tree C# changes versus main")]
    [InlineData("review my working tree C# changes relative to main")]
    [InlineData("review my working tree C# changes vs. main")]
    [InlineData("review my working tree C# changes from HEAD versus main")]
    [InlineData("review my working tree C# changes against my branch")]
    [InlineData("review my working tree C# changes from my branch")]
    [InlineData("review C# changes from my working tree against 'main'")]
    [InlineData("review C# changes from my working tree against \"main\"")]
    public void explicit_working_tree_baseline_is_unsupported_until_changed_paths_are_baseline_aware(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("semantic-diff-working-tree-baseline");
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Fact]
    public void unstaged_only_review_is_unsupported_because_default_includes_staged_changes()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest("review unstaged C# changes"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("semantic-diff-unstaged");
        response.Workflow.ShouldBeNull();
    }

    [Fact]
    public void absent_semantic_diff_command_cannot_be_suggested_as_a_workflow()
    {
        var response = new SuggestService(new FakeCatalog(), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest("review my working tree C# changes"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("semantic-diff-working-tree");
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("compare C# changes between two Git refs")]
    [InlineData("semantic diff old.cs and new.cs")]
    [InlineData("apply my uncommitted C# changes")]
    [InlineData("review uncommitted semantic changes in Java")]
    public void other_semantic_or_mutation_intents_do_not_trigger_working_tree_route(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: false)
            .Suggest(new SuggestRequest(query));

        response.Workflow.ShouldBeNull();
        response.Outcome.ShouldNotBe(SuggestOutcome.Unsupported);
    }
}
