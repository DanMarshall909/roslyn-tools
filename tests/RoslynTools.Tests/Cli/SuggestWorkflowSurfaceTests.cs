using System.Text.Json;
using RoslynTools.Services.SemanticDiff.Git;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Cli;

public sealed class SuggestWorkflowSurfaceTests
{
    [Fact]
    public async Task suggest_help_exposes_optional_solution_for_populated_routes()
    {
        var help = await RtCliHost.RunAsync("suggest --help");

        help.ExitCode.ShouldBe(0);
        help.Stdout.ShouldContain("--solution");
    }

    [Theory]
    [InlineData("Convert an instance method to static", "make-static")]
    [InlineData("Convert an instance method to static, please.", "make-static")]
    [InlineData("Convert an instance method to static, for performance and testability", "make-static")]
    [InlineData("Convert an instance method to static, and nothing else", "make-static")]
    [InlineData("Convert an instance method to static, and only that", "make-static")]
    [InlineData("Convert an instance method to static to improve performance and testability", "make-static")]
    [InlineData("Convert a static method to an instance method", "make-instance")]
    [InlineData("Convert OrderService.Validate from instance to static", "make-static")]
    [InlineData("Convert the static method OrderService.Validate to an instance method", "make-instance")]
    [InlineData("Make the instance method OrderService.Validate static", "make-static")]
    [InlineData("Remove static from the method OrderService.Validate", "make-instance")]
    [InlineData("Remove static from OrderService.Validate", "make-instance")]
    public async Task method_conversion_top_one_follows_the_requested_direction(string query, string intended)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Results")[0].GetProperty("Name").GetString().ShouldBe(intended);
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(1);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Convert an instance method to static and list callers")]
    [InlineData("Convert an instance method to static while listing callers")]
    [InlineData("Convert an instance method to static and do not change callers")]
    [InlineData("Convert an instance method to static and make a static class injectable")]
    [InlineData("Convert an instance method to static with no caller changes")]
    [InlineData("Make a static method non-static with no call-site updates")]
    [InlineData("Convert an instance method to static as well as list callers")]
    [InlineData("Convert an instance method to static. List callers.")]
    [InlineData("Convert an instance method to static with no call-site edits")]
    [InlineData("Convert an instance method to static along with listing callers")]
    [InlineData("List callers. Convert an instance method to static.")]
    [InlineData("List static classes and convert the instance method Foo.Bar unusual overload to static")]
    [InlineData("Convert the instance method Foo.Bar unusual overload to static and convert to static class")]
    [InlineData("Convert the instance method Foo.Bar unusual overload to static and convert to static imports")]
    [InlineData("List static classes and convert Foo.Bar unusual overload from instance to static")]
    [InlineData("Add static imports and convert Foo.Bar unusual overload from instance to static")]
    public async Task compound_method_conversion_does_not_recommend_the_opposite_mutation(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("method-staticness-workflow");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task negated_method_conversion_with_generic_callers_reports_missing_target()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Do not convert an instance method to static; list its callers\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("method-relationship-target");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task property_target_does_not_recommend_method_staticness()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Convert an instance method to a static property\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("method-kind-conversion");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
    }

    [Fact]
    public async Task property_staticness_reports_its_own_missing_capability()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Convert a property from instance to static\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("member-staticness-conversion");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("Make an instance field static")]
    [InlineData("Remove static from the property Value")]
    [InlineData("Remove static modifier on a field")]
    [InlineData("Add the static modifier on a field")]
    [InlineData("Make the property named Value static")]
    [InlineData("Convert a field named _count from static to instance")]
    [InlineData("Mark a field with the static modifier")]
    [InlineData("Remove the static keyword from property Value")]
    [InlineData("Set property Value to static")]
    [InlineData("Make property `OrderService.Status` static")]
    [InlineData("Make the fields X and Y static")]
    [InlineData("Make a static property thread-safe and make a field static")]
    [InlineData("Change the name of a static property and set a field static")]
    [InlineData("Make the property Value and the field Count static")]
    [InlineData("Do not remove static from property Value and add static to field Count")]
    [InlineData("Make a property static and list static classes")]
    [InlineData("Convert a field from instance to static and add static imports")]
    [InlineData("Add static imports and convert a field from instance to static")]
    [InlineData("List static classes and make a property static")]
    [InlineData("Remove a static field and make a property static")]
    public async Task property_or_field_staticness_never_recommends_a_method_recipe(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("member-staticness-conversion");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
    }

    [Theory]
    [InlineData("Find static imports used across the solution", "list-imports")]
    [InlineData("Add static using imports", "add-static-import")]
    public async Task non_method_static_queries_keep_their_first_catalogue_result(string query, string expected)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Results")[0].GetProperty("Name").GetString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("Make a static class injectable")]
    [InlineData("CONVERT A STATIC CLASS TO AN INSTANCE CLASS")]
    [InlineData("MAKE A STATIC CLASS INJECTABLE FOR TESTING")]
    [InlineData("Make static class \"TO\" injectable")]
    [InlineData("Make static class named RETURN injectable")]
    [InlineData("Convert a static class from static to instance")]
    [InlineData("Make a static class an instance class")]
    [InlineData("Make static class OrderService usable with dependency injection")]
    [InlineData("Migrate a static class to an instance class")]
    [InlineData("Replace a static class with an instance class")]
    [InlineData("Refactor a static class for dependency injection")]
    [InlineData("Convert OrderService from a static class to an instance class")]
    [InlineData("Make the static class OrderService into an instance")]
    [InlineData("Convert a static class into a service that can be injected")]
    [InlineData("Make this static class injectable")]
    [InlineData("Make a static class instance")]
    [InlineData("Make static class OrderService injectable")]
    [InlineData("Make a static class with fields, properties and methods injectable")]
    public async Task static_class_migration_reports_missing_workflow(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("static-class-migration");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Do not ever make a static class injectable")]
    [InlineData("Make a static class with an instance class")]
    [InlineData("Make a static class work with an instance class")]
    [InlineData("Make a static class compatible with an instance class")]
    [InlineData("Make a static class return an instance class")]
    [InlineData("MAKE A STATIC CLASS RETURN AN INSTANCE CLASS")]
    [InlineData("MAKE A STATIC CLASS ACCEPT AN INJECTABLE SERVICE")]
    [InlineData("Make a static class accept an injectable service")]
    [InlineData("Convert a static class to an instance method")]
    [InlineData("Convert a static class to an instance property")]
    public async Task non_migration_class_context_keeps_general_discovery(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(1);
        payload.RootElement.TryGetProperty("MissingCapability", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Add a static property", "static-member-creation")]
    [InlineData("Create a static field", "static-member-creation")]
    [InlineData("Add a private static field", "static-member-creation")]
    [InlineData("Add a static int field", "static-member-creation")]
    [InlineData("Create a static int? property", "static-member-creation")]
    [InlineData("Remove a static property", "static-member-deletion")]
    [InlineData("Delete a static field", "static-member-deletion")]
    [InlineData("Delete a public static property", "static-member-deletion")]
    [InlineData("Delete a static int field", "static-member-deletion")]
    public async Task static_member_creation_or_deletion_reports_missing_capability(string query, string capability)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe(capability);
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Delete a static property's reference")]
    [InlineData("Update references to a static property")]
    [InlineData("Edit references to a static property")]
    [InlineData("Edit references for a static field")]
    [InlineData("Edit references to an existing static property")]
    [InlineData("Remove static from the references to a static property Count")]
    [InlineData("Remove the static keyword from a static property reference")]
    [InlineData("Remove the static keyword from references to a static property")]
    public async Task static_member_reference_edit_reports_missing_capability(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 1 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
        payload.RootElement.GetProperty("MissingCapability").GetString().ShouldBe("member-reference-edit");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Theory]
    [InlineData("Do not add a static field")]
    [InlineData("I do not want to add a static field")]
    [InlineData("Do not add a static int field")]
    [InlineData("Avoid adding a static field")]
    [InlineData("Do not create a static int? property")]
    [InlineData("Never delete a static property")]
    public async Task negated_static_member_creation_or_deletion_does_not_recommend_mutation(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --top 3 --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.GetProperty("Outcome").GetString().ShouldBe("NoMatch");
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBe(0);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task implementation_search_json_contains_an_invokable_route_with_solution()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Find every implementation of IRtCommand\" --solution RoslynTools.slnx --top 3 --json");

        result.ExitCode.ShouldBe(0);
        using var payload = JsonDocument.Parse(result.Stdout);
        var response = payload.RootElement;
        response.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt inspect relationships implementations IRtCommand --solution {Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx")} --json");
        step.GetProperty("McpArguments").GetProperty("symbol").GetString().ShouldBe("IRtCommand");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(
            Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx"));
    }

    [Fact]
    public async Task implementation_search_text_shows_route_and_completeness_stop_condition()
    {
        var result = await RtCliHost.RunAsync(
            "suggest \"Find every implementation of IRtCommand\" --solution RoslynTools.slnx");

        result.ExitCode.ShouldBe(0);
        result.Stdout.ShouldContain(
            $"rt inspect relationships implementations IRtCommand --solution {Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx")} --json");
        result.Stdout.ShouldContain("Partial");
        result.Stdout.ShouldNotContain("extract-interface");
    }

    [Theory]
    [InlineData("Find all references to WorkspaceLoader", "WorkspaceLoader", null)]
    [InlineData("Find references to RoslynTools.Services.Suggest.SuggestService._catalog", "RoslynTools.Services.Suggest.SuggestService._catalog", null)]
    [InlineData("Find references to the field named _catalog", "_catalog", "field")]
    [InlineData("Find references to the method named Plan", "Plan", "method")]
    public async Task named_reference_search_has_one_cli_and_mcp_read_step(string query, string symbol, string? kind)
    {
        var json = await RtCliHost.RunAsync(
            $"suggest \"{query}\" --solution RoslynTools.slnx --top 3 --json");
        var text = await RtCliHost.RunAsync(
            $"suggest \"{query}\" --solution RoslynTools.slnx --top 3 --llm");

        json.ExitCode.ShouldBe(0, json.Stderr);
        text.ExitCode.ShouldBe(0, text.Stderr);
        using var payload = JsonDocument.Parse(json.Stdout);
        var response = payload.RootElement;
        response.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        response.GetProperty("Related").GetArrayLength().ShouldBe(0);
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        var solution = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");
        var invocation = $"rt inspect relationships references {symbol}"
                         + (kind is null ? "" : $" --kind {kind}")
                         + $" --solution {solution} --json";
        step.GetProperty("CliInvocation").GetString().ShouldBe(invocation);
        step.GetProperty("McpTool").GetString().ShouldBe("relationships");
        step.GetProperty("McpArguments").GetProperty("symbol").GetString().ShouldBe(symbol);
        step.GetProperty("McpArguments").GetProperty("mode").GetString().ShouldBe("References");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(solution);
        if (kind is null)
            step.GetProperty("McpArguments").TryGetProperty("kind", out _).ShouldBeFalse();
        else
            step.GetProperty("McpArguments").GetProperty("kind").GetString().ShouldBe(kind);
        step.GetProperty("StopCondition").GetString()!.ShouldContain("truncated");
        text.Stdout.ShouldContain(invocation);
        text.Stdout.ShouldContain("Partial");
        text.Stdout.ShouldNotContain("find-pattern");
    }

    [Theory]
    [InlineData("Find references to the constructor named WorkspaceLoader")]
    [InlineData("Find references to the namespace named RoslynTools")]
    public async Task unsupported_reference_kind_keeps_catalogue_discovery(string query)
    {
        var result = await RtCliHost.RunAsync($"suggest \"{query}\" --solution RoslynTools.slnx --json");

        result.ExitCode.ShouldBe(0, result.Stderr);
        using var payload = JsonDocument.Parse(result.Stdout);
        payload.RootElement.TryGetProperty("Workflow", out _).ShouldBeFalse();
        payload.RootElement.GetProperty("Results").GetArrayLength().ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task configured_architecture_check_has_a_populated_cli_and_mcp_route()
    {
        var json = await RtCliHost.RunAsync(
            "suggest \"Find violations of configured project layering rules\" --solution RoslynTools.slnx --json");
        var text = await RtCliHost.RunAsync(
            "suggest \"Find violations of configured project layering rules\" --solution RoslynTools.slnx --llm");

        json.ExitCode.ShouldBe(0, json.Stderr);
        text.ExitCode.ShouldBe(0, text.Stderr);
        using var payload = JsonDocument.Parse(json.Stdout);
        var response = payload.RootElement;
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        var solution = Path.Combine(RtCliHost.RepoRoot, "RoslynTools.slnx");
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt analyze check-architecture --config .rt/architecture.json --solution {solution} --json");
        step.GetProperty("McpTool").GetString().ShouldBe("check_architecture");
        step.GetProperty("McpArguments").GetProperty("config").GetString()
            .ShouldBe(".rt/architecture.json");
        step.GetProperty("McpArguments").GetProperty("solution").GetString().ShouldBe(solution);
        text.Stdout.ShouldContain("check-architecture --config .rt/architecture.json");
        text.Stdout.ShouldContain("Stop:");
    }

    [Fact]
    public async Task working_tree_review_has_an_invokable_or_explicitly_unsupported_cli_route()
    {
        var json = await RtCliHost.RunAsync(
            "suggest \"review my working tree C# changes\" --solution RoslynTools.slnx --json");
        var text = await RtCliHost.RunAsync(
            "suggest \"review my working tree C# changes\" --solution RoslynTools.slnx --llm");

        json.ExitCode.ShouldBe(0);
        text.ExitCode.ShouldBe(0);
        using var document = JsonDocument.Parse(json.Stdout);
        var response = document.RootElement;
        response.GetProperty("Results").GetArrayLength().ShouldBe(0);
        if (!WorkingTreeReadCapability.IsAvailable)
        {
            response.GetProperty("Outcome").GetString().ShouldBe("Unsupported");
            response.GetProperty("MissingCapability").GetString().ShouldBe("semantic-diff-working-tree");
            text.Stdout.ShouldContain("Git refs or two .cs files");
            return;
        }

        response.GetProperty("Outcome").GetString().ShouldBe("Recommendations");
        var step = response.GetProperty("Workflow").GetProperty("Steps")[0];
        step.GetProperty("CliInvocation").GetString().ShouldBe(
            $"rt analyze semantic-diff --directory {RtCliHost.RepoRoot} --json");
        step.GetProperty("McpTool").GetString().ShouldBe("semantic_diff");
        step.GetProperty("McpArguments").GetProperty("directory").GetString()
            .ShouldBe(RtCliHost.RepoRoot);
        text.Stdout.ShouldContain("semantic-diff --directory");
        text.Stdout.ShouldContain("Stop:");
    }
}
