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

    [Theory]
    [InlineData("Find violations of configured project layering rules")]
    [InlineData("Check configured project dependency boundaries")]
    [InlineData("Check configured layer dependency violations")]
    [InlineData("Check configured project/layer boundary violations")]
    [InlineData("List violations of configured project layering rules")]
    [InlineData("Show configured project layering violations")]
    [InlineData("Check configured project layering violations in `.rt/architecture.json`")]
    [InlineData("Check configured project layering violations in (.rt/architecture.json)")]
    [InlineData("Check configured project layering violations in .rt\\architecture.json")]
    [InlineData("Check configured project layering violations in ./.rt/architecture.json")]
    [InlineData("Validate architecture boundaries in .rt/architecture.json")]
    public void configured_architecture_checks_use_a_single_read_workflow(string query)
    {
        var catalog = new FakeCatalog(
            Info("check-architecture", category: "analyze", whenToUse: "Validate project boundaries"),
            Info("solid", category: "analyze", whenToUse: "Find principle violations"));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        var step = response.Workflow.ShouldNotBeNull().Steps.Single();
        step.CliInvocation.ShouldBe("rt analyze check-architecture --config .rt/architecture.json --json");
        step.McpTool.ShouldBeNull();
        step.McpUnavailableReason.ShouldNotBeNull().ShouldContain("absolute solution path");
        step.StopCondition.ShouldContain("trust");
        step.StopCondition.ShouldContain("completeness");
    }

    [Fact]
    public void configured_architecture_check_populates_mcp_only_for_an_absolute_solution()
    {
        var solution = Path.GetFullPath("RoslynTools.slnx");
        var catalog = new FakeCatalog(Info("check-architecture", category: "analyze"));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Find violations of configured project layering rules", Solution: solution));

        var step = response.Workflow.ShouldNotBeNull().Steps.Single();
        step.CliInvocation.ShouldBe(
            $"rt analyze check-architecture --config .rt/architecture.json --solution {solution} --json");
        step.McpTool.ShouldBe("check_architecture");
        step.McpArguments.ShouldNotBeNull()["config"].ShouldBe(".rt/architecture.json");
        step.McpArguments["solution"].ShouldBe(solution);
        step.McpUnavailableReason.ShouldBeNull();
    }

    [Fact]
    public void configured_architecture_check_reports_a_missing_catalogue_capability()
    {
        var response = new SuggestService(new FakeCatalog(Info("solid"))).Suggest(new SuggestRequest(
            "Find violations of configured project layering rules"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("check-architecture");
        response.Results.ShouldBeEmpty();
    }

    [Fact]
    public void compound_extraction_request_keeps_the_existing_unsupported_result()
    {
        var catalog = new FakeCatalog(Info("check-architecture", category: "analyze"));
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            "Check configured project dependency boundaries and extract all non-Microsoft dependencies from core layers"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("third-party-dependency-extraction");
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Explain the architecture of this project")]
    [InlineData("Find project layering violations")]
    [InlineData("List configured project dependencies")]
    [InlineData("Show configured architecture rules")]
    [InlineData("Find violations of SOLID principles in configured projects")]
    [InlineData("Find baseline deltas for configured project layering rules")]
    [InlineData("Refactor configured project layering rules")]
    [InlineData("Check configured project naming rules")]
    [InlineData("Check configured project layering rules in custom/architecture.json")]
    [InlineData("Check configured project layering violations with --config config/architecture.yaml")]
    [InlineData("Check configured project layering violations with --config config/architecture.rules")]
    [InlineData("Check configured project layering violations in config/architecture.rules")]
    [InlineData("Check configured project layering violations in config/architecture")]
    [InlineData("Check configured project layering violations in custom/architecture")]
    [InlineData("Check configured project layering violations with ./custom/architecture")]
    public void other_architecture_requests_keep_their_existing_discovery_path(string query)
    {
        var response = new SuggestService(new FakeCatalog(Info("doctor", whenToUse: query)))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe("doctor");
        response.Workflow.ShouldBeNull();
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
        response.Workflow.ShouldNotBeNull().Steps.Single().CliInvocation
            .ShouldBe("rt inspect relationships references Plan --kind method --json");
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
    [InlineData("Assess impact of moving CycleEngine to another project")]
    [InlineData("What breaks if I move CycleEngine to another project?")]
    [InlineData("Estimate the impact of renaming IRtCommand")]
    [InlineData("What is the impact of moving CycleEngine to another project?")]
    [InlineData("What will be the impact of moving CycleEngine to another project?")]
    [InlineData("What could be the impact of moving CycleEngine to another project?")]
    [InlineData("What might be the impact of moving CycleEngine to another project?")]
    [InlineData("What's the impact of moving CycleEngine to another project?")]
    [InlineData("What impact would moving CycleEngine to another project have?")]
    [InlineData("Impact of moving CycleEngine to another project")]
    [InlineData("Assess the project impact of moving CycleEngine")]
    [InlineData("Impact assessment for renaming IRtCommand")]
    [InlineData("How would moving CycleEngine impact the project?")]
    [InlineData("How would moving CycleEngine affect callers?")]
    [InlineData("Will moving CycleEngine to another project break callers?")]
    [InlineData("Does moving CycleEngine to another project break anything?")]
    [InlineData("Which callers would break if I rename IRtCommand?")]
    [InlineData("What happens if I move CycleEngine to another project?")]
    [InlineData("What would break if we moved CycleEngine?")]
    [InlineData("What would break when renaming IRtCommand?")]
    [InlineData("Assess impact of deleting CycleEngine")]
    [InlineData("What would break if I delete IRtCommand?")]
    [InlineData("Assess impact of replacing IRtCommand")]
    [InlineData("What would break if I extract CycleEngine into a new project?")]
    public void proposed_change_impact_requests_report_the_missing_capability(string query)
    {
        var catalog = new FakeCatalog(
            Info("move", category: "refactor", whenToUse: "Move a type between projects"),
            Info("check-architecture", category: "analyze", whenToUse: "Check boundaries"));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("change-impact");
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        var reason = response.UnsupportedReason.ShouldNotBeNull();
        reason.ShouldContain("Inspect references");
        reason.ShouldContain("project dependencies");
        reason.ShouldContain("review the proposed edits");
        reason.ShouldContain("build and test");
        reason.ShouldContain("cannot prove safety");
        reason.ShouldContain("reflection");
        reason.ShouldContain("generated code");
        reason.ShouldContain("external consumers");
    }

    [Theory]
    [InlineData("Move CycleEngine to another project", "move")]
    [InlineData("Delete IRtCommand", "delete-unreferenced")]
    [InlineData("Show dependencies of CycleEngine", "dependency-graph")]
    [InlineData("Find violations of configured project layering rules", "check-architecture")]
    [InlineData("Analyze project impact", "dependency-graph")]
    public void requests_without_proposed_change_impact_keep_their_existing_route(
        string query,
        string expectedCommand)
    {
        var response = new SuggestService(new FakeCatalog(Info(expectedCommand, whenToUse: query)))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        if (query.Contains("configured project layering", StringComparison.Ordinal))
            response.Workflow.ShouldNotBeNull().Steps.Single().CliInvocation.ShouldContain("check-architecture");
        else
            response.Results.Single().Name.ShouldBe(expectedCommand);
    }

    [Theory]
    [InlineData("Assess impact and prepare a safe rename plan", "prepare-plan")]
    [InlineData("Assess impact of moving CycleEngine and compare compiler diagnostics before and after", "compiler-diagnostic-delta")]
    [InlineData("Assess impact of removing all non-Microsoft dependencies from core layers", "third-party-dependency-extraction")]
    public void earlier_missing_capability_guards_keep_precedence_over_change_impact(
        string query,
        string expectedCapability)
    {
        var response = new SuggestService(new FakeCatalog(Info("move", category: "refactor")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe(expectedCapability);
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Find NuGet package dependencies in the core layer", "package-dependency-inventory")]
    [InlineData("NuGet package dependencies in the core layer", "package-dependency-inventory")]
    [InlineData("Find NuGet dependencies in the core layer", "package-dependency-inventory")]
    [InlineData("Which NuGet dependencies does the core layer use?", "package-dependency-inventory")]
    [InlineData("List all third-party package references across core projects", "package-dependency-inventory")]
    [InlineData("Extract all non-Microsoft dependencies from the core layers", "third-party-dependency-extraction")]
    [InlineData("Extract all dependencies from the core layers other than Microsoft ones", "third-party-dependency-extraction")]
    [InlineData("All non-Microsoft dependencies from core layers should be extracted", "third-party-dependency-extraction")]
    [InlineData("Removing all non-Microsoft dependencies from core layers", "third-party-dependency-extraction")]
    public void unavailable_package_and_core_extraction_jobs_do_not_suggest_unrelated_commands(
        string query,
        string missingCapability)
    {
        var catalog = new FakeCatalog(
            Info("dependency-graph", whenToUse: "Show C# symbol dependencies"),
            Info("extract-interface", category: "refactor", whenToUse: "Extract an interface"),
            Info("find-pattern", whenToUse: "Find source patterns"));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe(missingCapability);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNullOrWhiteSpace();
        response.UnsupportedReason.ShouldContain("rt");
    }

    [Theory]
    [InlineData("Show symbol dependencies of WorkspaceLoader", "dependency-graph")]
    [InlineData("Add a project dependency from Core to Hosting", "add-dependency")]
    [InlineData("Extract an interface for a constructor-injected dependency", "extract-interface")]
    [InlineData("Show dependencies of Package", "dependency-graph")]
    public void supported_dependency_queries_keep_their_existing_catalogue_route(
        string query,
        string expectedCommand)
    {
        var catalog = new FakeCatalog(Info(expectedCommand, whenToUse: query));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe(expectedCommand);
    }

    [Theory]
    [InlineData("Compare compiler diagnostics before and after this change")]
    [InlineData("Compare compiler errors against the previous build")]
    [InlineData("Which analyzer warnings were introduced since HEAD?")]
    [InlineData("Find the delta in CS8618 diagnostics since main")]
    [InlineData("Which compiler warnings were introduced by this change?")]
    [InlineData("What analyzer diagnostics appeared in the current branch relative to main?")]
    [InlineData("Compare compiler diagnostics between two Git branches")]
    [InlineData("Compare analyzer warnings between release/1.0 and release/1.1")]
    [InlineData("Compare compiler diagnostics against HEAD")]
    [InlineData("Compare compiler diagnostics between HEAD and main")]
    [InlineData("Compare compiler diagnostics from main to HEAD")]
    [InlineData("What compiler warnings did this change introduce?")]
    [InlineData("Compare compiler diagnostics before and after the fix")]
    [InlineData("Compare compiler diagnostics before and after C# declaration changes")]
    [InlineData("What did the fix introduce in compiler diagnostics since main?")]
    [InlineData("Compare compiler diagnostics before and after we fix compiler warnings")]
    [InlineData("After changing C# declarations, compare compiler diagnostics before and after")]
    [InlineData("After we fix compiler warnings, compare compiler diagnostics before and after")]
    [InlineData("Compare compiler diagnostics between feature/foo and main")]
    public void unavailable_diagnostic_delta_does_not_recommend_a_mutator(string query)
    {
        var catalog = new FakeCatalog(
            Info("change-signature", category: "refactor", whenToUse: "Change a method signature"),
            Info("rename", category: "refactor", whenToUse: "Rename a symbol"));

        var response = new SuggestService(catalog).Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("compiler-diagnostic-delta");
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNullOrWhiteSpace();
        response.UnsupportedReason.ShouldContain("revisions");
    }

    [Theory]
    [InlineData("Compare C# declarations before and after this change", "semantic-diff")]
    [InlineData("Compare C# declarations before and after fixing compiler warnings", "semantic-diff")]
    [InlineData("Find baseline deltas for architecture violations", "check-architecture")]
    [InlineData("Change a method signature to fix CS8618", "change-signature")]
    [InlineData("Fix compiler warning regressions since main", "doctor")]
    [InlineData("How do I fix compiler warnings introduced by this change?", "doctor")]
    [InlineData("Can you fix compiler warnings introduced by this change?", "doctor")]
    [InlineData("Please help me fix compiler warnings introduced by this change", "doctor")]
    [InlineData("How can I fix the current compiler diagnostics since main?", "doctor")]
    [InlineData("Fix all compiler warnings introduced since main", "doctor")]
    [InlineData("Compare compiler warnings between ProjectA and ProjectB", "doctor")]
    [InlineData("Compare compiler warnings in main and test projects", "doctor")]
    [InlineData("Compare compiler warnings against main project", "doctor")]
    [InlineData("Compare compiler diagnostics between .NET versions 9 and 10", "doctor")]
    [InlineData("Review C# declarations before and after fixing compiler warnings", "semantic-diff")]
    [InlineData("How did C# declarations change before and after fixing compiler warnings?", "semantic-diff")]
    [InlineData("How do I fix an introduced CS8618 warning?", "doctor")]
    [InlineData("Show current compiler warnings", "doctor")]
    [InlineData("Show compiler errors after this build", "doctor")]
    public void other_diagnostic_and_comparison_queries_are_not_reclassified_as_deltas(
        string query,
        string expectedCommand)
    {
        var response = new SuggestService(new FakeCatalog(Info(expectedCommand, whenToUse: query)))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe(expectedCommand);
    }

    [Fact]
    public void plan_verification_takes_precedence_over_diagnostic_delta_detection()
    {
        var response = new SuggestService(new FakeCatalog()).Suggest(new SuggestRequest(
            "Verify a prepared plan's compiler diagnostic delta before applying"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("verify-plan");
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

    [Theory]
    [InlineData("Find all references to WorkspaceLoader", "WorkspaceLoader", null)]
    [InlineData("Find references to CycleEngine", "CycleEngine", null)]
    [InlineData("Find references to Package", "Package", null)]
    [InlineData("Find references to Diagnostic", "Diagnostic", null)]
    [InlineData("Show references to the method named Plan before I review it", "Plan", "method")]
    [InlineData("List every reference to RoslynTools.Core.IRtCommand", "RoslynTools.Core.IRtCommand", null)]
    [InlineData("Find references to PackageReference", "PackageReference", null)]
    [InlineData("Find references to WorkspaceLoader.", "WorkspaceLoader", null)]
    [InlineData("Find references to WorkspaceLoader?", "WorkspaceLoader", null)]
    [InlineData("Find references to WorkspaceLoader, please", "WorkspaceLoader", null)]
    [InlineData("Please find references to WorkspaceLoader", "WorkspaceLoader", null)]
    [InlineData("I need to find references to WorkspaceLoader", "WorkspaceLoader", null)]
    [InlineData("Find references to RoslynTools.Services.Suggest.SuggestService._catalog", "RoslynTools.Services.Suggest.SuggestService._catalog", null)]
    [InlineData("Find references to the symbol named _catalog", "_catalog", null)]
    [InlineData("Find references to the type named WorkspaceLoader", "WorkspaceLoader", "type")]
    [InlineData("Find references to the field named _catalog", "_catalog", "field")]
    [InlineData("Find references to a field named _catalog", "_catalog", "field")]
    [InlineData("Find references to the property named Current", "Current", "property")]
    [InlineData("Find references to the event named Changed", "Changed", "event")]
    [InlineData("Find references to _catalog", "_catalog", null)]
    [InlineData("Find references to the method named executeAsync", "executeAsync", "method")]
    [InlineData("Find references to executeAsync", "executeAsync", null)]
    public void named_reference_search_returns_one_invokable_read_workflow(string query, string symbol, string? kind)
    {
        var catalog = new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships"),
            Info("rename", category: "refactor", whenToUse: "Rename a symbol"));
        var solutionPath = Path.GetFullPath("RoslynTools.slnx");
        var response = new SuggestService(catalog).Suggest(new SuggestRequest(
            query, Top: 3, Solution: solutionPath));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        var step = response.Workflow.ShouldNotBeNull().Steps.Single();
        step.CliInvocation.ShouldBe(
            $"rt inspect relationships references {symbol}"
            + (kind is null ? "" : $" --kind {kind}")
            + $" --solution {solutionPath} --json");
        step.McpTool.ShouldBe("relationships");
        step.McpArguments.ShouldNotBeNull()["symbol"].ShouldBe(symbol);
        step.McpArguments["mode"].ShouldBe("References");
        step.McpArguments["solution"].ShouldBe(solutionPath);
        if (kind is null)
            step.McpArguments.ShouldNotContainKey("kind");
        else
            step.McpArguments["kind"].ShouldBe(kind);
        step.StopCondition.ShouldContain("Partial");
        step.StopCondition.ShouldContain("truncated");
        step.StopCondition.ShouldContain("ambiguous");
        step.StopCondition.ShouldContain("external consumers");
    }

    [Fact]
    public void reference_intent_is_unsupported_when_the_read_route_is_absent()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("rename", category: "refactor", whenToUse: "Rename a symbol")))
            .Suggest(new SuggestRequest("Find all references to WorkspaceLoader"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("relationships-references");
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("RoslynTools.slnx")]
    public void reference_search_without_absolute_solution_keeps_cli_route_only(string? solution)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships")))
            .Suggest(new SuggestRequest("Find references to WorkspaceLoader", Solution: solution));

        var step = response.Workflow.ShouldNotBeNull().Steps.Single();
        step.CliInvocation.ShouldBe("rt inspect relationships references WorkspaceLoader"
                                    + (solution is null ? "" : " --solution RoslynTools.slnx") + " --json");
        step.McpTool.ShouldBeNull();
        step.McpArguments.ShouldBeNull();
        step.McpUnavailableReason.ShouldNotBeNull().ShouldContain("absolute solution path");
    }

    [Theory]
    [InlineData("Find all references")]
    [InlineData("Find references to the interface")]
    [InlineData("Find references to the field")]
    [InlineData("Find references to the property")]
    [InlineData("Find references to the event")]
    [InlineData("Find references to the record")]
    [InlineData("Find references to the parameter")]
    [InlineData("Find references to the constructor named WorkspaceLoader")]
    [InlineData("Find references to the namespace named RoslynTools")]
    [InlineData("Find references to the parameter named cancellationToken")]
    [InlineData("Find references to the class named WorkspaceLoader")]
    [InlineData("Find references to the interface named IRtCommand")]
    [InlineData("Find references to the record named Request")]
    [InlineData("Find references to the struct named Span")]
    [InlineData("Find references to the enum named Mode")]
    [InlineData("Find references to the delegate named Handler")]
    [InlineData("Find references to the member named WorkspaceLoader")]
    [InlineData("Find references to package")]
    [InlineData("Find references to project")]
    [InlineData("Find references to WorkspaceLoader<T>")]
    [InlineData("Find references to a named C# symbol before changing it")]
    [InlineData("Find references to C++ code")]
    [InlineData("Find references to Run(int)")]
    [InlineData("Find references to Run (int)")]
    [InlineData("Find references to global::WorkspaceLoader")]
    [InlineData("Find references to WorkspaceLoader[]")]
    [InlineData("Find references to WorkspaceLoader and CycleEngine")]
    [InlineData("Find references to WorkspaceLoader, CycleEngine")]
    [InlineData("Find project references to Core")]
    [InlineData("Find references to Core project")]
    [InlineData("Find references to the Core project")]
    [InlineData("Find references to Foo package")]
    [InlineData("Find references to WorkspaceLoader in project Core")]
    [InlineData("Find references to WorkspaceLoader in Core project")]
    [InlineData("Find references to WorkspaceLoader only in tests")]
    [InlineData("Find references to WorkspaceLoader excluding tests")]
    [InlineData("Find references to WorkspaceLoader including tests")]
    [InlineData("Find references to WorkspaceLoader with kind type")]
    [InlineData("Find references to WorkspaceLoader with callers")]
    [InlineData("Find references to WorkspaceLoader --kind type")]
    [InlineData("Find references to WorkspaceLoader --limit 5")]
    [InlineData("Find references to WorkspaceLoader --solution Other.slnx")]
    [InlineData("Find references to WorkspaceLoader in Other.slnx")]
    [InlineData("Find references to WorkspaceLoader from main")]
    [InlineData("In project Core, find references to WorkspaceLoader")]
    [InlineData("Only in tests, find references to WorkspaceLoader")]
    [InlineData("With kind type, find references to WorkspaceLoader")]
    [InlineData("--limit 5 find references to WorkspaceLoader")]
    [InlineData("For tests, find references to WorkspaceLoader")]
    [InlineData("Find references to WorkspaceLoader before I review it limited to 5")]
    [InlineData("Find references to WorkspaceLoader before I review it for tests")]
    public void reference_queries_outside_the_one_symbol_workflow_keep_catalogue_discovery(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships")))
            .Suggest(new SuggestRequest(query));

        response.Workflow.ShouldBeNull();
    }

    [Fact]
    public void change_impact_guard_takes_precedence_over_reference_workflow()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("relationships", whenToUse: "Inspect semantic relationships")))
            .Suggest(new SuggestRequest(
                "What breaks if I move WorkspaceLoader? Find references to WorkspaceLoader"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("change-impact");
        response.Workflow.ShouldBeNull();
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
    [InlineData("compare my uncommitted C# changes against main", "main", "main")]
    [InlineData("review semantic C# working tree changes since v1.0", "v1.0", "v1.0")]
    [InlineData("review my working tree C# changes since main.", "main", "main")]
    [InlineData("review my working tree C# changes from HEAD~1", "HEAD~1", "'HEAD~1'")]
    [InlineData("review my working tree C# changes compared with main", "main", "main")]
    [InlineData("review my working tree C# changes versus main", "main", "main")]
    [InlineData("review my working tree C# changes relative to main", "main", "main")]
    [InlineData("review my working tree C# changes vs. main", "main", "main")]
    [InlineData("review my working tree C# changes against head", "head", "head")]
    [InlineData("review my working tree C# changes against Head", "Head", "Head")]
    public void explicit_simple_working_tree_baseline_routes_to_cli_and_mcp(
        string query, string reference, string cliReference)
    {
        var solution = Path.GetFullPath("RoslynTools.slnx");
        var directory = Path.GetDirectoryName(solution)!;
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest(query, Solution: solution));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        var step = response.Workflow!.Steps.Single();
        step.CliInvocation.ShouldBe(
            $"rt analyze semantic-diff --directory {directory} --from {cliReference} --json");
        step.McpTool.ShouldBe("semantic_diff");
        step.McpArguments!["directory"].ShouldBe(directory);
        step.McpArguments["from"].ShouldBe(reference);
        step.Reason.ShouldContain(reference);
        step.StopCondition.ShouldContain("error");
    }

    [Fact]
    public void explicit_baseline_without_solution_has_cli_route_and_honest_mcp_limit()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest("review my working tree C# changes against main"));

        var step = response.Workflow!.Steps.Single();
        step.CliInvocation.ShouldBe("rt analyze semantic-diff --from main --json");
        step.McpTool.ShouldBeNull();
        step.McpArguments.ShouldBeNull();
        step.McpUnavailableReason!.ShouldContain("absolute solution path");
    }

    [Theory]
    [InlineData("review my working tree C# changes from HEAD versus main")]
    [InlineData("review my working tree C# changes against my branch")]
    [InlineData("review my working tree C# changes from my branch")]
    [InlineData("review C# changes from my working tree against 'main'")]
    [InlineData("review C# changes from my working tree against \"main\"")]
    [InlineData("review my working tree C# changes against main branch")]
    [InlineData("review my working tree C# changes against MY branch")]
    [InlineData("review my working tree C# changes against HEAD~1..HEAD")]
    [InlineData("review my working tree C# changes against HEAD@{1}")]
    [InlineData("review my working tree C# changes against main:Widget.cs")]
    [InlineData("review my working tree C# changes against main versus main")]
    [InlineData("review my working tree C# changes since --cached")]
    [InlineData("review my working tree C# changes since")]
    public void ambiguous_working_tree_baseline_is_unsupported(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("semantic-diff-working-tree-baseline");
        response.UnsupportedReason!.ShouldContain("--from <ref>");
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("review unstaged C# changes")]
    [InlineData("review unstaged C# changes since main")]
    public void unstaged_only_review_is_unsupported_because_default_includes_staged_changes(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("semantic-diff", category: "analyze")), workingTreeReadsAvailable: true)
            .Suggest(new SuggestRequest(query));

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
