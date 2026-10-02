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
    [InlineData("Convert an instance method to static", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static, please.", "make-static", "make-instance")]
    [InlineData("Please, convert an instance method to static.", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static for performance", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static for performance and testability", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static, for performance and testability", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static, because it improves performance", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static, to improve performance", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static to improve performance and testability", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static because it improves performance and testability", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static for better testability", "make-static", "make-instance")]
    [InlineData("Kindly convert an instance method to static", "make-static", "make-instance")]
    [InlineData("Could you kindly convert an instance method to static", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static and nothing else", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static, and nothing else", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static, and only that", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static and no other edits", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static safely", "make-static", "make-instance")]
    [InlineData("Convert an instance method to static in a test project", "make-static", "make-instance")]
    [InlineData("Make an instance method static", "make-static", "make-instance")]
    [InlineData("Make the instance method OrderService.Validate static", "make-static", "make-instance")]
    [InlineData("Make OrderService.Validate static", "make-static", "make-instance")]
    [InlineData("Make the named method OrderService.Validate static", "make-static", "make-instance")]
    [InlineData("Make an instance method named Validate static", "make-static", "make-instance")]
    [InlineData("Turn an instance method into a static method", "make-static", "make-instance")]
    [InlineData("Convert a method from instance to static", "make-static", "make-instance")]
    [InlineData("Convert a method from instance to a static method", "make-static", "make-instance")]
    [InlineData("Convert OrderService.Validate from instance to static", "make-static", "make-instance")]
    [InlineData("Convert OrderService.Validate(string) from instance to static", "make-static", "make-instance")]
    [InlineData("Convert the instance method Foo.Bar<Baz<Qux>> to static", "make-static", "make-instance")]
    [InlineData("Convert an instance method named Validate to static", "make-static", "make-instance")]
    [InlineData("Convert the instance method OrderService.Validate to static", "make-static", "make-instance")]
    [InlineData("Convert a method named Validate from instance to static", "make-static", "make-instance")]
    [InlineData("Convert the named method OrderService.Validate from instance to static", "make-static", "make-instance")]
    [InlineData("Convert a static method to an instance method", "make-instance", "make-static")]
    [InlineData("Convert a method from static to instance", "make-instance", "make-static")]
    [InlineData("Convert a method from static to an instance method", "make-instance", "make-static")]
    [InlineData("Convert OrderService.Validate from static to instance", "make-instance", "make-static")]
    [InlineData("Convert the named method OrderService.Validate from static to instance", "make-instance", "make-static")]
    [InlineData("Convert OrderService.Validate<T> from static to instance", "make-instance", "make-static")]
    [InlineData("Convert a static method named Validate to an instance method", "make-instance", "make-static")]
    [InlineData("Convert the static method OrderService.Validate to an instance method", "make-instance", "make-static")]
    [InlineData("Convert a method from static to non-static", "make-instance", "make-static")]
    [InlineData("Make a static method an instance method", "make-instance", "make-static")]
    [InlineData("Make the static method OrderService.Validate an instance method", "make-instance", "make-static")]
    [InlineData("Make OrderService.Validate an instance method", "make-instance", "make-static")]
    [InlineData("Make OrderService.Validate non-static", "make-instance", "make-static")]
    [InlineData("Make the method OrderService.Validate non-static", "make-instance", "make-static")]
    [InlineData("Make the named method OrderService.Validate non-static", "make-instance", "make-static")]
    [InlineData("Remove static from a method", "make-instance", "make-static")]
    [InlineData("Remove static from the method OrderService.Validate", "make-instance", "make-static")]
    [InlineData("Remove static from OrderService.Validate", "make-instance", "make-static")]
    [InlineData("Remove the static modifier from OrderService.Validate", "make-instance", "make-static")]
    [InlineData("Remove static from static method OrderService.Validate", "make-instance", "make-static")]
    [InlineData("Remove static from the named method OrderService.Validate", "make-instance", "make-static")]
    [InlineData("Remove static from a method named Validate", "make-instance", "make-static")]
    [InlineData("Remove the static modifier from a method", "make-instance", "make-static")]
    [InlineData("Make a static method non-static", "make-instance", "make-static")]
    [InlineData("Convert a static method to non-static", "make-instance", "make-static")]
    [InlineData("Convert a static method to an instance method; please", "make-instance", "make-static")]
    public void explicit_method_conversion_removes_the_opposite_direction_before_ranking(
        string query,
        string intended,
        string opposite)
    {
        var catalog = new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static"),
            Info("static-class-inventory", category: "analyze", whenToUse: "List static classes"));
        var service = new SuggestService(catalog);

        var topOne = service.Suggest(new SuggestRequest(query, Top: 1));
        var topThree = service.Suggest(new SuggestRequest(query, Top: 3));

        topOne.Results.ShouldHaveSingleItem().Name.ShouldBe(intended);
        topThree.Results.ShouldHaveSingleItem().Name.ShouldBe(intended);
        topThree.Results.Select(result => result.Name).ShouldNotContain(opposite);
        topThree.Related.ShouldBeEmpty();
        topThree.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Make an instance method static and convert a static method to instance")]
    [InlineData("Convert an instance method to static and list callers")]
    [InlineData("Convert an instance method to static and identify its callers")]
    [InlineData("Convert an instance method to static while listing callers")]
    [InlineData("Convert an instance method to static after reviewing callers")]
    [InlineData("Convert an instance method to static but keep callers unchanged")]
    [InlineData("Convert an instance method to static but not update callers")]
    [InlineData("Convert an instance method to static but do not update callers")]
    [InlineData("Convert an instance method to static and do not change callers")]
    [InlineData("Convert an instance method to static; do not change callers")]
    [InlineData("Convert an instance method to static and never change callers")]
    [InlineData("Convert an instance method to static and make a static class injectable")]
    [InlineData("Make a static class injectable and convert an instance method to static")]
    [InlineData("Convert an instance method to static but leave callers unchanged")]
    [InlineData("Convert an instance method to static without updating callers")]
    [InlineData("Convert an instance method to static without caller changes")]
    [InlineData("Convert an instance method to static with no caller changes")]
    [InlineData("Convert an instance method to static with no updates to callers")]
    [InlineData("Convert an instance method to static with callers unchanged")]
    [InlineData("Convert an instance method to static so callers remain unchanged")]
    [InlineData("Convert an instance method to static with no call-site edits")]
    [InlineData("Convert an instance method to static with no edits to callers")]
    [InlineData("Convert an instance method to static with no changes at call sites")]
    [InlineData("Convert an instance method to static with existing call sites unchanged")]
    [InlineData("Convert an instance method to static with callers kept intact")]
    [InlineData("Make a static method non-static with no call-site updates")]
    [InlineData("Convert an instance method to static as well as list callers")]
    [InlineData("Inspect callers and convert an instance method to static")]
    [InlineData("Inspect callers before we convert an instance method to static")]
    [InlineData("Convert an instance method to static, list callers")]
    [InlineData("Convert an instance method to static, update callers")]
    [InlineData("Convert an instance method to static. List callers.")]
    [InlineData("Convert an instance method to static or list callers")]
    [InlineData("Convert an instance method to static along with listing callers")]
    [InlineData("List callers. Convert an instance method to static.")]
    [InlineData("Convert an instance method to static; give me a list of callers")]
    [InlineData("Convert an instance method to static with an extra caller review requirement")]
    [InlineData("Convert OrderService.Validate from instance to static and list callers")]
    [InlineData("Convert a static method to an instance method; list callers")]
    public void compound_method_conversion_reports_missing_workflow(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("method-staticness-workflow");
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNull().ShouldContain("separate");
    }

    [Theory]
    [InlineData("Convert the instance method Foo.Bar unusual overload to static")]
    [InlineData("List static classes and convert the instance method Foo.Bar unusual overload to static")]
    [InlineData("Convert the instance method Foo.Bar unusual overload to static and convert to static class")]
    [InlineData("Convert the instance method Foo.Bar unusual overload to static and convert to static imports")]
    [InlineData("List static classes and convert Foo.Bar unusual overload from instance to static")]
    [InlineData("Add static imports and convert Foo.Bar unusual overload from instance to static")]
    public void unparsed_named_method_conversion_does_not_fall_through_to_opposite_mutation(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("method-staticness-workflow");
        response.Results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Convert an instance method to a static property")]
    [InlineData("Convert a static method to an instance property")]
    [InlineData("Convert an instance method to a static field")]
    [InlineData("Convert the instance method OrderService.Validate to a static property")]
    public void conversion_to_another_member_kind_does_not_recommend_method_staticness(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("method-kind-conversion");
        response.Results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Convert a property from instance to static")]
    [InlineData("Convert a field from static to instance")]
    [InlineData("Convert the instance property OrderService.Status to static")]
    [InlineData("Make an instance field static")]
    [InlineData("Make a static property an instance property")]
    [InlineData("Remove static from the property Value")]
    [InlineData("Add static to a field")]
    [InlineData("Mark property Value static")]
    [InlineData("Make a property static and list static classes")]
    [InlineData("Make a property static in a static class")]
    [InlineData("Convert a field from instance to static and add static imports")]
    [InlineData("Remove the static modifier on property Value")]
    [InlineData("Remove static modifier on a field")]
    [InlineData("Add the static modifier on a field")]
    [InlineData("Make the property named Value static")]
    [InlineData("Make a field called _count static")]
    [InlineData("Convert a property named OrderService.Status from instance to static")]
    [InlineData("Convert a field named _count from static to instance")]
    [InlineData("Mark the property named Value as static")]
    [InlineData("Mark a field with the static modifier")]
    [InlineData("Remove the static keyword from property Value")]
    [InlineData("Add the static keyword to field _count")]
    [InlineData("Make field called _count non-static")]
    [InlineData("Convert a property called Value from static to instance")]
    [InlineData("Make the property \"Value\" static")]
    [InlineData("Set property Value to static")]
    [InlineData("Make property `OrderService.Status` static")]
    [InlineData("Convert field `_count` from static to instance")]
    [InlineData("Make a property global::Foo.Bar static")]
    [InlineData("Make the fields X and Y static")]
    [InlineData("Set the static modifier on property Value")]
    [InlineData("Apply the static modifier to field _count")]
    [InlineData("Remove the static modifier off property Value")]
    [InlineData("Make a static property thread-safe and make a field static")]
    [InlineData("Change the name of a static property and set a field static")]
    [InlineData("Mark a static field obsolete and convert a property from instance to static")]
    [InlineData("Make the property Value and the field Count static")]
    [InlineData("Convert field X and property Y from instance to static")]
    [InlineData("Do not remove static from property Value and add static to field Count")]
    [InlineData("Do not add static to property Value and remove static from field Count")]
    [InlineData("Add static imports and convert a field from instance to static")]
    [InlineData("List static classes and make a property static")]
    [InlineData("Remove a static field and make a property static")]
    [InlineData("Convert a property from static to instance and edit references to a static field")]
    [InlineData("Remove static from property Count and edit references to a static field")]
    [InlineData("Convert a property from static to instance, then edit references to a static field")]
    [InlineData("Convert a property from static to instance and editing references to a static field")]
    [InlineData("Remove static from property Count then change references to a static field")]
    [InlineData("Make property Count non-static and convert references to a static field")]
    [InlineData("Make property Count nonstatic")]
    [InlineData("Make property Count nonstatic and convert references to a static field")]
    public void property_or_field_staticness_does_not_claim_a_method_workflow(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("member-staticness-conversion");
        response.Results.ShouldBeEmpty();
    }

    [Fact]
    public void missing_plan_capability_keeps_precedence_over_method_conversion()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest("Convert an instance method to static and prepare a plan", Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("prepare-plan");
        response.Results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Convert an instance method to static", "make-static", "make-instance")]
    [InlineData("Convert a static method to an instance method", "make-instance", "make-static")]
    [InlineData("Convert OrderService.Validate from instance to static", "make-static", "make-instance")]
    [InlineData("Convert OrderService.Validate from static to instance", "make-instance", "make-static")]
    public void explicit_method_conversion_reports_a_missing_intended_command(
        string query,
        string intended,
        string opposite)
    {
        var response = new SuggestService(new FakeCatalog(
            Info(opposite, category: "refactor", whenToUse: "Convert a method between static and instance")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe(intended);
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Find static imports used across the solution")]
    [InlineData("List static classes")]
    [InlineData("Make a static class with properties thread-safe")]
    [InlineData("Remove static imports from a method")]
    [InlineData("Find a field used by a static method")]
    [InlineData("Make a static method async")]
    [InlineData("Convert an instance method to a static class")]
    [InlineData("Make a static property thread-safe")]
    [InlineData("Mark a static field obsolete")]
    [InlineData("Change the name of a static property")]
    [InlineData("Convert a static field to a property")]
    [InlineData("Make a property thread-safe in a static method")]
    [InlineData("Mark a property obsolete for static analysis")]
    [InlineData("Make a property static-analysis friendly")]
    [InlineData("Mark a field static-analysis exempt")]
    [InlineData("Make a property static analysis friendly")]
    [InlineData("Mark a field static analysis exempt")]
    [InlineData("Make a property with the static modifier thread-safe")]
    [InlineData("Do not make a property static")]
    [InlineData("Do not convert a field from instance to static")]
    [InlineData("Do not remove static from property Value")]
    [InlineData("I do not want to make property Value static")]
    [InlineData("Do not make a static class injectable; list static classes")]
    [InlineData("Make a static class thread-safe and make another class injectable")]
    [InlineData("Make a static class thread-safe and also make another class injectable")]
    [InlineData("Make a static class thread-safe. Make another class injectable")]
    [InlineData("Make a static class thread-safe and create another injectable class")]
    [InlineData("Make a static class callable from an instance class")]
    [InlineData("Make a static class thread-safe but make another class injectable")]
    [InlineData("Make a static class not injectable")]
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
    [InlineData("Make a static class an instance property")]
    [InlineData("Convert a static class to an instance field")]
    public void static_queries_outside_method_conversion_keep_catalogue_discovery(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("list-imports", whenToUse: "Find static imports"),
            Info("static-class-inventory", category: "analyze", whenToUse: "List static classes"),
            Info("rename", category: "refactor", whenToUse: "Rename a method or class")))
            .Suggest(new SuggestRequest(query, Top: 3));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.MissingCapability.ShouldBeNull();
        response.Results.ShouldNotBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Add a static property", "static-member-creation", 1)]
    [InlineData("Create a static field", "static-member-creation", 3)]
    [InlineData("Introduce a static property named Status", "static-member-creation", 1)]
    [InlineData("Declare static field Foo.Bar", "static-member-creation", 3)]
    [InlineData("Add property Value with the static modifier", "static-member-creation", 1)]
    [InlineData("Add a property with the static modifier", "static-member-creation", 3)]
    [InlineData("Create field Count as static", "static-member-creation", 1)]
    [InlineData("Declare static fields X and Y", "static-member-creation", 3)]
    [InlineData("Add a private static field", "static-member-creation", 1)]
    [InlineData("Create a public static property", "static-member-creation", 3)]
    [InlineData("Declare an internal static readonly field", "static-member-creation", 1)]
    [InlineData("Add a static int field", "static-member-creation", 1)]
    [InlineData("Create a static string property", "static-member-creation", 3)]
    [InlineData("Create a private static System.String property", "static-member-creation", 1)]
    [InlineData("Create a static int? property", "static-member-creation", 1)]
    [InlineData("Add a static Dictionary<string, List<int>> field", "static-member-creation", 1)]
    [InlineData("Add a static (int x, int y) field", "static-member-creation", 1)]
    [InlineData("Remove a static property", "static-member-deletion", 3)]
    [InlineData("Delete a static field", "static-member-deletion", 1)]
    [InlineData("Delete static property Count", "static-member-deletion", 3)]
    [InlineData("Delete a public static property", "static-member-deletion", 1)]
    [InlineData("Delete a static int field", "static-member-deletion", 1)]
    [InlineData("Delete a static Dictionary<string, List<int>> field", "static-member-deletion", 1)]
    [InlineData("Do not add a static field; delete a static property", "static-member-deletion", 1)]
    [InlineData("List static classes; add a static field", "static-member-creation", 3)]
    public void static_member_creation_and_deletion_report_missing_capability(
        string query, string capability, int top)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("add-static-import", category: "refactor", whenToUse: "Add using static imports"),
            Info("make-static", category: "refactor", whenToUse: "Convert a method to static"),
            Info("delete-unreferenced", category: "refactor", whenToUse: "Delete an unreferenced symbol")))
            .Suggest(new SuggestRequest(query, Top: top));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe(capability);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("Do not add a static field")]
    [InlineData("Do not ever add a static field")]
    [InlineData("I do not want to add a static field")]
    [InlineData("Do not add a static int field")]
    [InlineData("Avoid adding a static field")]
    [InlineData("Without creating a static property")]
    [InlineData("Do not create a static int? property")]
    [InlineData("Without creating a static string? property")]
    [InlineData("Do not create a static (int? x, int y) field")]
    [InlineData("Do not create a static (int x, string? label) property")]
    [InlineData("Never delete a static property")]
    public void standalone_negated_static_member_change_does_not_recommend_a_mutation(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("add-static-import", category: "refactor", whenToUse: "Add using static imports"),
            Info("make-static", category: "refactor", whenToUse: "Convert a method to static")))
            .Suggest(new SuggestRequest(query, Top: 3));

        response.Outcome.ShouldBe(SuggestOutcome.NoMatch);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Do not add a static field; list static classes")]
    [InlineData("Do not add a static field and list static classes")]
    [InlineData("Do not add a static field? List static classes")]
    [InlineData("Do not add a static field? Also list static classes")]
    [InlineData("Do not add a static field? Instead, list static classes")]
    [InlineData("Do not add a static field? Could you list static classes")]
    [InlineData("Do not add a static int? field? Please list static classes")]
    [InlineData("Do not create a static (int? x, int y) field? Also list static classes")]
    public void inventory_after_negated_member_creation_keeps_inventory_discovery(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("add-static-import", category: "refactor", whenToUse: "Add using static imports"),
            Info("static-class-inventory", category: "analyze", whenToUse: "List static classes")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe("static-class-inventory");
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Add an attribute to a static property")]
    [InlineData("Remove an attribute from a static field")]
    [InlineData("Add static using imports")]
    [InlineData("Add a static property getter")]
    [InlineData("Remove a static field initializer")]
    [InlineData("Delete a static field Count initializer")]
    [InlineData("Remove a public static field named Count initializer")]
    [InlineData("Delete a static property Count access")]
    [InlineData("Remove a static field Count value")]
    [InlineData("Remove a static field's initializer")]
    [InlineData("Add a static property's getter")]
    [InlineData("Remove a static field/property Count initializer")]
    public void static_member_context_does_not_claim_creation_or_deletion(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("add-static-import", category: "refactor", whenToUse: "Add using static imports"),
            Info("static-class-inventory", category: "analyze", whenToUse: "List static classes")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.MissingCapability.ShouldBeNull();
        response.Results.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData("Delete a static property reference", 1)]
    [InlineData("Delete a static property Count reference", 3)]
    [InlineData("Delete a static property's reference", 1)]
    [InlineData("Delete a static property’s reference", 3)]
    [InlineData("Delete a static property or field reference", 1)]
    [InlineData("Delete a static property/field reference", 3)]
    [InlineData("Delete a static property or field's reference", 1)]
    [InlineData("Delete a static property/field's reference", 3)]
    [InlineData("Delete a static property or field named Count reference", 1)]
    [InlineData("Delete a static property/field Count reference", 3)]
    [InlineData("Delete a static property and field named Count's reference", 1)]
    [InlineData("Remove a static field usage", 3)]
    [InlineData("Update references to a static property", 1)]
    [InlineData("Change all usages of static field Count", 3)]
    [InlineData("Replace uses of the static property named Count", 1)]
    [InlineData("Add a reference to a static property", 3)]
    [InlineData("Edit references to a static property", 1)]
    [InlineData("Edit usages of a static field", 3)]
    [InlineData("Edit a static property reference", 1)]
    [InlineData("Modify references to a static property", 3)]
    [InlineData("Edit references for a static field", 1)]
    [InlineData("Edit usages for a static field", 3)]
    [InlineData("Modify references for a static property", 1)]
    [InlineData("Update all references for static field Count", 3)]
    [InlineData("Edit any references to a static field", 1)]
    [InlineData("Edit each reference to a static field", 3)]
    [InlineData("Update existing references to a static property", 1)]
    [InlineData("Edit references to an existing static property", 3)]
    [InlineData("Edit an existing static property reference", 1)]
    [InlineData("Edit references to my static field", 3)]
    [InlineData("Remove static from the references to a static property Count", 1)]
    [InlineData("Change a static property reference to instance access", 3)]
    [InlineData("Convert references to a static property to instance accesses", 1)]
    [InlineData("Convert a static field reference to instance access", 3)]
    [InlineData("Modify references for each static field", 1)]
    [InlineData("Modify references for any static field", 3)]
    [InlineData("Modify references for every static field", 1)]
    [InlineData("Remove static from a static property reference", 3)]
    [InlineData("Remove the static keyword from a static property reference", 1)]
    [InlineData("Remove the static keyword from references to a static property", 3)]
    [InlineData("Remove the static keyword from the references to a static property", 1)]
    [InlineData("Remove the static modifier from usages of a static field", 3)]
    public void static_member_reference_edits_report_missing_capability(string query, int top)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("delete-unreferenced", category: "refactor", whenToUse: "Delete an unreferenced symbol"),
            Info("make-static", category: "refactor", whenToUse: "Convert a method to static"),
            Info("relationships", category: "inspect", whenToUse: "Find symbol references")))
            .Suggest(new SuggestRequest(query, Top: top));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("member-reference-edit");
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNull().ShouldContain("references");
    }

    [Theory]
    [InlineData("Do not delete a static property's reference")]
    [InlineData("Never update references to a static field")]
    [InlineData("Do not edit references to a static property")]
    [InlineData("Avoid editing usages of a static field")]
    [InlineData("Avoid modifying references to a static property")]
    [InlineData("Without updating a static property's reference")]
    [InlineData("Do not edit references for a static field")]
    [InlineData("Avoid editing any references to a static field")]
    [InlineData("Never update existing references to a static property")]
    [InlineData("Do not edit references to my static field")]
    [InlineData("Do not remove static from the references to a static property Count")]
    [InlineData("Never convert references to a static property to instance accesses")]
    [InlineData("Do not remove static from a static property reference")]
    [InlineData("Do not remove the static keyword from a static property reference")]
    [InlineData("Do not remove the static keyword from references to a static property")]
    [InlineData("Never remove the static modifier from usages of a static field")]
    public void standalone_negated_static_member_reference_edit_has_no_mutation_route(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("delete-unreferenced", category: "refactor", whenToUse: "Delete an unreferenced symbol"),
            Info("make-static", category: "refactor", whenToUse: "Convert a method to static")))
            .Suggest(new SuggestRequest(query, Top: 3));

        response.Outcome.ShouldBe(SuggestOutcome.NoMatch);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Make a static class injectable")]
    [InlineData("Make a static class with properties injectable")]
    [InlineData("Convert a static class to an instance class")]
    [InlineData("CONVERT A STATIC CLASS TO AN INSTANCE CLASS")]
    [InlineData("MAKE A STATIC CLASS INJECTABLE FOR TESTING")]
    [InlineData("Make static class \"TO\" injectable")]
    [InlineData("Make static class named RETURN injectable")]
    [InlineData("Make static class IO injectable")]
    [InlineData("Convert a static class from static to instance")]
    [InlineData("Turn a static class into an instance class")]
    [InlineData("Make static class OrderService injectable")]
    [InlineData("Convert static class OrderService to an instance class")]
    [InlineData("Convert static class Foo.Bar to an instance class")]
    [InlineData("Make a static class thread-safe; convert a static class to an instance class")]
    [InlineData("Make a static class an instance class")]
    [InlineData("Make static class OrderService usable with dependency injection")]
    [InlineData("Migrate a static class to an instance class")]
    [InlineData("Refactor a static class for dependency injection")]
    [InlineData("Convert a static class to support dependency injection")]
    [InlineData("Turn static class Foo into a regular class")]
    [InlineData("Replace a static class with an instance class")]
    [InlineData("Make static class Foo instance-based")]
    [InlineData("Make a static class thread-safe and make another static class injectable")]
    [InlineData("Make a static class thread-safe. Make another static class injectable")]
    [InlineData("Convert static class Foo to be injectable")]
    [InlineData("Make static class Foo usable for dependency injection")]
    [InlineData("Make static class Foo use dependency injection")]
    [InlineData("Convert OrderService from a static class to an instance class")]
    [InlineData("Make the static class OrderService into an instance")]
    [InlineData("Convert a static class into a service that can be injected")]
    [InlineData("Make a static class thread-safe but make another static class injectable")]
    [InlineData("Make a static class with fields injectable")]
    [InlineData("Make a static class with properties and fields injectable")]
    [InlineData("Make this static class injectable")]
    [InlineData("Make a static class instance")]
    [InlineData("Make static class \"OrderService\" injectable")]
    [InlineData("Convert \"OrderService\" from a static class to an instance class")]
    [InlineData("Make a static class with fields, properties and methods injectable")]
    [InlineData("Make a static class with fields, properties, and methods injectable")]
    public void static_class_migration_does_not_recommend_a_method_recipe(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static"),
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("static-class-inventory", category: "analyze", whenToUse: "List static classes")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("static-class-migration");
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNull().ShouldContain("static-class-inventory");
    }

    [Theory]
    [InlineData("Do not convert an instance method to static; list its callers")]
    [InlineData("Do not convert instance method OrderService.Validate(int x, int y) to static; list its callers")]
    [InlineData("Do not convert an instance method to static and list its callers")]
    [InlineData("I do not want to convert an instance method to static; list its callers")]
    [InlineData("Do not convert an instance method to static; list callers of this method")]
    [InlineData("Do not convert an instance method to static; find references to it")]
    [InlineData("Do not convert an instance method to static; show usages of the method")]
    [InlineData("Never convert a static method to instance; show usages")]
    [InlineData("Without converting an instance method to static, find its callers")]
    public void negated_method_conversion_with_generic_relationship_request_needs_a_method_target(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static"),
            Info("relationships", whenToUse: "Find method references")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("method-relationship-target");
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
        response.UnsupportedReason.ShouldNotBeNull().ShouldContain("named method");
    }

    [Theory]
    [InlineData("Do not convert an instance method to static")]
    [InlineData("Do not make this instance method static")]
    [InlineData("Don't make an instance method static")]
    [InlineData("Never convert a static method to instance")]
    [InlineData("Do not convert instance method OrderService.Validate(int x, int y) to static")]
    [InlineData("Do not convert instance method OrderService.Validate<First, Second> to static")]
    [InlineData("Do not make the instance method OrderService.Validate(int x, int y) static")]
    public void standalone_negated_method_conversion_does_not_recommend_a_mutation(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-instance", category: "refactor", whenToUse: "Convert a static method to an instance method"),
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.NoMatch);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Fact]
    public void negated_method_conversion_with_named_reference_clause_uses_read_workflow()
    {
        var solution = Path.GetFullPath("RoslynTools.slnx");
        var response = new SuggestService(new FakeCatalog(
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static"),
            Info("relationships", whenToUse: "Find method references")))
            .Suggest(new SuggestRequest(
                "Do not convert an instance method to static; find references to method OrderService.Validate",
                Top: 1, Solution: solution));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        var step = response.Workflow.ShouldNotBeNull().Steps.Single();
        step.CliInvocation.ShouldBe($"rt inspect relationships references OrderService.Validate --kind method --solution {solution} --json");
        step.McpTool.ShouldBe("relationships");
        step.McpArguments.ShouldNotBeNull()["symbol"].ShouldBe("OrderService.Validate");
    }

    [Fact]
    public void negated_conversion_with_multiple_named_reference_tasks_does_not_drop_the_extra_action()
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static"),
            Info("relationships", whenToUse: "Find method references")))
            .Suggest(new SuggestRequest(
                "Do not convert an instance method to static; find references to method OrderService.Validate and delete it",
                Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("method-staticness-workflow");
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Do not convert an instance method to static; convert another instance method to static")]
    [InlineData("Do not convert an instance method to static and convert another instance method to static")]
    public void earlier_negated_conversion_does_not_hide_later_positive_conversion(string query)
    {
        var response = new SuggestService(new FakeCatalog(
            Info("make-static", category: "refactor", whenToUse: "Convert an instance method to static")))
            .Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe("method-staticness-workflow");
        response.Results.ShouldBeEmpty();
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
