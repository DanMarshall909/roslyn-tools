using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Services.Suggest;
using Shouldly;

namespace RoslynTools.Tests.Suggest;

public sealed class SuggestRelevanceTests
{
    private sealed class Catalog(params RtCommandInfo[] commands) : ICommandCatalog
    {
        public IReadOnlyList<RtCommandInfo> All { get; } = commands;
    }

    private static RtCommandInfo Info(string name, string whenToUse, string[]? examples = null) =>
        new(name, false, true, "inspect", whenToUse, examples ?? []);

    [Theory]
    [InlineData("Generate unit tests for BookingService")]
    [InlineData("Deploy this application to Azure")]
    [InlineData("Find memory leaks in the running application")]
    [InlineData("Find the cheapest flights")]
    [InlineData("Publish documentation to the website")]
    [InlineData("Remove unused TypeScript imports")]
    [InlineData("Change the signature at the bottom of my email")]
    [InlineData("purple banana spaceship")]
    [InlineData("please")]
    public void absent_tasks_do_not_recommend_incidental_keyword_matches(string query)
    {
        var service = new SuggestService(new Catalog(
            Info("replace-datetime", "Replace DateTime.Now before adding unit tests"),
            Info("move", "Move types in this application"),
            Info("find-pattern", "Find structural patterns in code"),
            Info("add-xml-doc", "Generate XML documentation"),
            Info("remove-unused-usings", "Remove unused C# imports"),
            Info("change-signature", "Change a method signature")));

        var response = service.Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe(query);
        response.UnsupportedReason.ShouldNotBeNull().ShouldContain("rt dump-help --compact");
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Rename OrderService.SendAsync to OrderService.DeliverAsync", "rename")]
    [InlineData("Rename SendAsync to DeliverAsync", "rename")]
    [InlineData("Show source of OrderService.SendAsync", "source")]
    [InlineData("Outline src/OrderService.cs", "outline")]
    public void explicit_csharp_targets_do_not_count_as_missing_task_concepts(string query, string command)
    {
        var response = new SuggestService(new Catalog(
            Info("rename", "Rename a C# symbol across the solution"),
            Info("source", "Read the source of a specific C# symbol"),
            Info("outline", "Get a C# file's type and member outline")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe([command]);
    }

    [Fact]
    public void admission_happens_before_top_and_ignores_repeated_terms()
    {
        var service = new SuggestService(new Catalog(
            Info("rename-rename-rename", "An unrelated action"),
            Info("correct", "Rename a symbol")));

        var response = service.Suggest(new SuggestRequest("rename rename rename symbol", Top: 1));

        response.Results.Select(result => result.Name).ShouldBe(["correct"]);
    }

    [Fact]
    public void examples_alone_do_not_establish_task_support()
    {
        var response = new SuggestService(new Catalog(
                Info("other", "Inspect a symbol", ["rt other generate invoices"])))
            .Suggest(new SuggestRequest("generate invoices"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.Results.ShouldBeEmpty();
    }

    [Fact]
    public void single_task_term_requires_a_name_or_synonym_anchor()
    {
        var response = new SuggestService(new Catalog(
                Info("source", "Read a symbol's source"),
                Info("move-method", "Move a method into the source class")))
            .Suggest(new SuggestRequest("Show source of Widget.Foo"));

        response.Results.Select(result => result.Name).ShouldBe(["source"]);
    }

    [Fact]
    public void substantive_metadata_can_match_without_a_command_name_hit()
    {
        var response = new SuggestService(new Catalog(Info("shape", "Read the public interface surface")))
            .Suggest(new SuggestRequest("Inspect public interface surface"));

        response.Results.Single().Name.ShouldBe("shape");
    }

    [Theory]
    [InlineData("Show source of TypeScript files")]
    [InlineData("Show source of JavaScript code")]
    [InlineData("Show source of Foo.sql")]
    [InlineData("Show source of Foo.SQL")]
    [InlineData("Show source of Foo.sQl")]
    [InlineData("Show source of Foo.JS")]
    [InlineData("Show source of Foo.Js")]
    [InlineData("Show source of Foo.TS")]
    [InlineData("Show source of Foo.tS")]
    [InlineData("Show source of Foo.JSX")]
    [InlineData("Show source of Foo.TsX")]
    [InlineData("Show source of Foo.mJS")]
    [InlineData("Show source of Foo.CjS")]
    [InlineData("Show source of Foo.PY")]
    [InlineData("Show source of Foo.Py")]
    [InlineData("Show source of Foo.PYI")]
    [InlineData("Show source of Foo.PyW")]
    [InlineData("Show source of Foo.JAVA")]
    [InlineData("Show source of Foo.Java")]
    [InlineData("Remove unused imports from TypeScript")]
    [InlineData("Find code complexity of JavaScript")]
    public void foreign_task_concepts_are_not_treated_as_symbol_targets(string query)
    {
        var response = new SuggestService(new Catalog(
                Info("source", "Read C# source files and code"),
                Info("remove-unused-usings", "Remove unused C# imports"),
                Info("complexity", "Find code complexity")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.Results.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Show source of method Widget.SQL", "source")]
    [InlineData("Show source of class Widget.Java", "source")]
    [InlineData("Rename method Widget.SQL to Widget.JS", "rename")]
    public void explicit_csharp_operands_take_precedence_over_foreign_suffixes(string query, string intended)
    {
        var response = new SuggestService(new Catalog(
                Info("source", "Read C# source of methods and classes"),
                Info("rename", "Rename a C# method")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe([intended]);
    }

    [Theory]
    [InlineData("Show source of Widget.Foo")]
    [InlineData("Show source of Widget.FOO")]
    [InlineData("Show source of Foo.cs")]
    [InlineData("Show source of Foo.CS")]
    public void qualified_csharp_targets_remain_represented(string query)
    {
        var response = new SuggestService(new Catalog(
                Info("source", "Read C# source files and code")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe(["source"]);
    }

    [Fact]
    public void unmatched_task_label_is_bounded_without_splitting_unicode()
    {
        var query = "Generate unit tests " + string.Concat(Enumerable.Repeat("🚀", 300));
        var response = new SuggestService(new Catalog()).Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        var label = response.MissingCapability.ShouldNotBeNull();
        label.EnumerateRunes().Count().ShouldBeLessThanOrEqualTo(161);
        label.ShouldEndWith("…");
        label.ShouldNotContain("\uFFFD");
    }

    [Theory]
    [InlineData(160)]
    [InlineData(161)]
    public void task_label_is_preserved_until_the_unicode_limit_is_exceeded(int length)
    {
        var query = string.Concat(Enumerable.Repeat("🚀", length));
        var response = new SuggestService(new Catalog()).Suggest(new SuggestRequest(query));

        var expected = length == 160 ? query : string.Concat(Enumerable.Repeat("🚀", 160)) + "…";
        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.MissingCapability.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Show which C# language features this solution uses", "language-features")]
    [InlineData("Show which C# language features are used in the solution", "language-features")]
    [InlineData("Diagnose an invalid rt configuration file", "config")]
    [InlineData("Rename this class", "rename")]
    [InlineData("I need Read.cs to rename a class", "rename")]
    [InlineData("Please! Rename a class", "rename")]
    [InlineData("I need \"Show! members.cs\" to rename a class", "rename")]
    [InlineData("I need \"Show. members.cs\" to rename a class", "rename")]
    [InlineData("I need \"Show; members.cs\" to rename a class", "rename")]
    [InlineData("Outline this file", "outline")]
    [InlineData("Extract an interface from class BookingService", "extract-interface")]
    [InlineData("Show me how to extract an interface from class BookingService", "extract-interface")]
    [InlineData("Split a large class by moving selected members into a new class", "extract-class")]
    [InlineData("Narrow consumers of an interface to only the members they use", "narrow-dependency")]
    [InlineData("Convert console output to structured logging", "convert-console-logging")]
    [InlineData("Compare C# declarations in main and HEAD", "semantic-diff")]
    [InlineData("Comparing C# declarations in main and HEAD", "semantic-diff")]
    [InlineData("Recording feedback about an rt tool problem", "feedback")]
    [InlineData("List global and static using directives", "list-imports")]
    [InlineData("Rename method sendAsync to deliverAsync", "rename")]
    [InlineData("Rename foo to bar", "rename")]
    [InlineData("Show source of OrderService.sendAsync", "source")]
    [InlineData("Show source of the OrderService class", "source")]
    [InlineData("Show callers of method sendAsync", "relationships")]
    [InlineData("Find implementations of an interface", "relationships")]
    [InlineData("List interface implementations", "relationships")]
    [InlineData("Move a type across projects", "move")]
    [InlineData("Move a type into a namespace", "move")]
    [InlineData("Replace all invocations of a static method with a different method name", "replace-call")]
    [InlineData("Measure cyclomatic complexity", "complexity")]
    public void represented_tasks_remain_discoverable_in_the_real_catalogue(string query, string expected)
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>().Suggest(new SuggestRequest(query, Top: 1));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe(expected);
    }

    [Fact]
    public void read_intent_does_not_admit_a_mutator_that_mentions_the_same_concepts()
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo("extract-interface", true, true, "refactor", "List public members of a class", []),
                Info("shape", "List public members of a class")))
            .Suggest(new SuggestRequest("List public members of a class"));

        response.Results.Select(result => result.Name).ShouldBe(["shape"]);
    }

    [Theory]
    [InlineData("Show me how to rename a class")]
    [InlineData("Show how to rename a class")]
    public void conversational_show_framing_preserves_the_requested_mutation(string query)
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo("rename", true, true, "refactor", "Rename a class", [])))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe("rename");
    }

    [Fact]
    public void declared_alias_does_not_require_incidental_example_overlap()
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo("shape", false, true, "inspect", "Inspect members", [], Aliases: ["sketch"])))
            .Suggest(new SuggestRequest("sketch"));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Single().Name.ShouldBe("shape");
    }

    [Fact]
    public void unknown_command_lookup_with_no_lexical_overlap_retains_no_match()
    {
        var response = new SuggestService(new Catalog(Info("rename", "Rename a symbol")))
            .SuggestCommandName("qzzvxx");

        response.Outcome.ShouldBe(SuggestOutcome.NoMatch);
        response.Results.ShouldBeEmpty();
        response.Related.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Rename @foo to @bar")]
    [InlineData("Rename @class to @interface")]
    [InlineData("Rename \"foo\" to \"bar\"")]
    [InlineData("Rename method _foo to _bar")]
    [InlineData("Rename a class")]
    [InlineData("Show me how to rename a class")]
    [InlineData("Show how to rename a class")]
    public void real_rename_tasks_exclude_incidental_alternatives(string query)
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>().Suggest(new SuggestRequest(query, Top: 3));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe(["rename"]);
    }

    [Fact]
    public void incidental_explanation_does_not_establish_a_different_primary_capability()
    {
        var response = new SuggestService(new Catalog(
                Info("solid", "Detect heuristic class violations. Classes have public members.")))
            .Suggest(new SuggestRequest("List public members of a class"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.Results.ShouldBeEmpty();
    }

    [Fact]
    public void real_public_member_inventory_is_not_substituted_with_heuristic_checks()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>()
            .Suggest(new SuggestRequest("List public members of a class"));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.Results.ShouldBeEmpty();
    }

    [Fact]
    public void real_source_read_excludes_outline_without_source_contents()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>()
            .Suggest(new SuggestRequest("Show source of the OrderService class", Top: 3));

        response.Results.Select(result => result.Name).ShouldBe(["source"]);
    }

    [Theory]
    [InlineData("Show C# 7 language features")]
    [InlineData("Show C# 999 language features")]
    [InlineData("Show C# 7.3 language features")]
    [InlineData("Show source of method 7")]
    [InlineData("Show source of method @foo#")]
    [InlineData("Show source of Mies")]
    [InlineData("Measure source")]
    [InlineData("Measuring source")]
    public void substantive_constraints_are_not_silently_discarded(string query)
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();

        var response = provider.GetRequiredService<SuggestService>().Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        response.Results.ShouldBeEmpty();
        response.Workflow.ShouldBeNull();
    }

    [Theory]
    [InlineData("Show C# 7 language features", "version-seven")]
    [InlineData("Show C# 7.7 language features", "version-seven-point-seven")]
    public void explicitly_declared_numeric_capabilities_remain_distinct(string query, string expected)
    {
        var response = new SuggestService(new Catalog(
                Info("version-seven", "Inspect C# 7 language features"),
                Info("version-seven-point-seven", "Inspect C# 7.7 language features")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe([expected]);
    }

    [Theory]
    [InlineData("Rename foo1 to foo2", "rename")]
    [InlineData("Show source of method parse2", "source")]
    [InlineData("Outline src/v7.3/OrderService2.cs", "outline")]
    public void digits_in_explicit_csharp_operands_remain_valid(string query, string expected)
    {
        var response = new SuggestService(new Catalog(
                Info("rename", "Rename C# symbols"),
                Info("source", "Read C# source of a method"),
                Info("outline", "Inspect file members")))
            .Suggest(new SuggestRequest(query));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe([expected]);
    }

    [Theory]
    [InlineData("", "Do not create a static field and list public members of a class")]
    [InlineData("", "DO NOT CREATE A STATIC FIELD AND LIST public members of a class")]
    [InlineData("tr-TR", "DO NOT CREATE A STATIC FIELD AND LIST public members of a class")]
    [InlineData("", "Do not create a static field; list public members of a class")]
    [InlineData("", "Do not create a static field named cache; list public members of a class")]
    public void negated_member_clause_preserves_the_following_read_intent(string culture, string query)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            var response = new SuggestService(new Catalog(
                    new RtCommandInfo("alter", true, true, "refactor", "Public members of a class", []),
                    Info("shape", "Public members of a class")))
                .Suggest(new SuggestRequest(query));

            response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
            response.Results.Select(result => result.Name).ShouldBe(["shape"]);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void quoted_paths_preserve_adjacent_read_tokens()
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo("alter", true, true, "refactor", "Class members", []),
                Info("outline", "Inspect class members")))
            .Suggest(new SuggestRequest("Can\"Foo.cs\"inspect class members"));

        response.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        response.Results.Select(result => result.Name).ShouldBe(["outline"]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void an_empty_requested_result_window_is_no_match(int top)
    {
        var response = new SuggestService(new Catalog(Info("rename", "Rename a symbol")))
            .Suggest(new SuggestRequest("Rename a symbol", Top: top));

        response.Outcome.ShouldBe(SuggestOutcome.NoMatch);
        response.Results.ShouldBeEmpty();
        response.MissingCapability.ShouldBeNull();
    }

    [Fact]
    public void lexical_command_hints_report_recommendations_independently_of_task_admission()
    {
        var service = new SuggestService(new Catalog(Info("rename", "Rename a symbol")));

        var hints = service.SuggestCommandName("rename-flights");

        hints.Outcome.ShouldBe(SuggestOutcome.Recommendations);
        hints.Results.Select(result => result.Name).ShouldBe(["rename"]);
        service.Suggest(new SuggestRequest("rename-flights")).Outcome.ShouldBe(SuggestOutcome.Unsupported);
    }

    [Theory]
    [InlineData("I want to rename my type")]
    [InlineData("We need to rename our type")]
    [InlineData("Can you please rename members for us?")]
    [InlineData("Would you kindly rename that type?")]
    [InlineData("Rename every type and its members")]
    [InlineData("Rename this type with all members")]
    public void conversational_framing_preserves_a_declared_mutation(string query)
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo("rename", true, true, "refactor", "Rename types and members", [])))
            .Suggest(new SuggestRequest(query));

        response.Results.Select(result => result.Name).ShouldBe(["rename"]);
    }

    [Theory]
    [InlineData("Show me which members are in this class")]
    [InlineData("Show members")]
    [InlineData("Could you inspect your class members?")]
    [InlineData("Can they inspect class members?")]
    [InlineData("Can it list class members?")]
    [InlineData("I need \"Foo.cs\" class members")]
    [InlineData("Can they all inspect class members?")]
    [InlineData("Can this list class members?")]
    [InlineData("All I want is to inspect class members")]
    [InlineData("Please! Show class members.")]
    [InlineData("What is in the class? Get all members.")]
    [InlineData("Read members from that class for us")]
    [InlineData("Show members on a class or its interface")]
    [InlineData("List members as a class/interface inventory")]
    [InlineData("List members by class")]
    [InlineData("Show class members with interface members also")]
    [InlineData("Get it: class members and interface members")]
    [InlineData("They need class members instead of interface members")]
    [InlineData("Inspect members at the class or interface")]
    [InlineData("What are public class members?")]
    [InlineData("Show me how to inspect class members")]
    [InlineData("How to inspect class members")]
    public void conversational_framing_preserves_a_declared_inventory(string query)
    {
        var response = new SuggestService(new Catalog(
                Info("outline", "Public class interface member inventory"),
                new RtCommandInfo("alter", true, true, "refactor", "Public class interface member inventory", [])))
            .Suggest(new SuggestRequest(query));

        response.Results.Select(result => result.Name).ShouldBe(["outline"]);
    }

    [Theory]
    [InlineData("What is in the class? Get all members.", true)]
    [InlineData("They need class members instead of interface members", true)]
    [InlineData("Show me how to inspect class members", true)]
    [InlineData("Show me how to find interface implementations", false)]
    [InlineData("What symbols are in this class?", false)]
    [InlineData("What namespace is class Foo in?", false)]
    [InlineData("Could they inspect class members?", true)]
    [InlineData("Can it list class members?", true)]
    [InlineData("I need \"Foo.cs\" class members", true)]
    [InlineData("Can they all inspect class members?", true)]
    public void natural_inventory_requests_do_not_recommend_repository_mutators(string query, bool outlineAvailable)
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var catalogue = provider.GetRequiredService<ICommandCatalog>().All;

        var response = provider.GetRequiredService<SuggestService>().Suggest(new SuggestRequest(query, Top: 3));

        response.Outcome.ShouldBeOneOf(SuggestOutcome.Recommendations, SuggestOutcome.Unsupported);
        foreach (var result in response.Results)
            catalogue.Single(command => command.Name == result.Name).Mutates.ShouldBeFalse();
        if (outlineAvailable)
            response.Results.Select(result => result.Name).ShouldContain("outline");
    }

    [Theory]
    [InlineData("rename", true, "Rename", "types", "Renaming types")]
    [InlineData("change-signature", true, "Update", "method signature", "Updating method signatures")]
    [InlineData("remove-unused-usings", true, "Remove", "unused using directive", "Removing unused using directives")]
    [InlineData("replace-call", true, "Replace", "static method invocation", "Replacing static method invocations")]
    [InlineData("delete-unreferenced", true, "Delete", "unreferenced symbol", "Deleting unreferenced symbols")]
    [InlineData("implement-interface", true, "Generate", "interface implementation", "Generating interface implementations")]
    [InlineData("add-dependency", true, "Inject", "constructor dependency", "Injecting constructor dependencies")]
    [InlineData("complexity", false, "Measure", "method complexity", "Measuring method complexity")]
    [InlineData("semantic-diff", false, "Compare", "CSharp declarations", "Comparing CSharp declarations")]
    [InlineData("convert-console-logging", true, "Convert", "console output", "Converting console output")]
    [InlineData("move-method", true, "Move", "static method between one class and another", "Moving static methods between classes")]
    [InlineData("extract-class", true, "Split", "large class", "Splitting large classes")]
    [InlineData("check-architecture", false, "Check", "architecture boundaries", "Checking architecture boundaries")]
    [InlineData("check-architecture", false, "Enforce", "architecture boundaries", "Enforcing architecture boundaries")]
    [InlineData("feedback", false, "Record", "tool problems", "Recording tool problems")]
    public void action_inflections_preserve_the_action_as_a_required_capability(
        string command, bool mutates, string action, string subject, string query)
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo(command, mutates, true, mutates ? "refactor" : "inspect", $"{action} {subject}", []),
                Info("inventory", $"Inspect {subject}")))
            .Suggest(new SuggestRequest(query));

        response.Results.Select(result => result.Name).ShouldBe([command]);
    }

    [Theory]
    [InlineData("C#")]
    [InlineData("CSharp")]
    public void csharp_spelling_does_not_erase_the_language_constraint(string language)
    {
        var response = new SuggestService(new Catalog(
                Info("csharp-source", "Read CSharp source files"),
                Info("javascript-source", "Read JavaScript source files")))
            .Suggest(new SuggestRequest($"Show {language} source files"));

        response.Results.Select(result => result.Name).ShouldBe(["csharp-source"]);
    }

    [Theory]
    [InlineData("Finding class members", "Class members")]
    [InlineData("Measuring method complexity", "Measure method complexity")]
    [InlineData("They need to measure method complexity", "Measure method complexity")]
    [InlineData("They need \"Foo.cs\" to measure method complexity", "Measure method complexity")]
    public void inflected_read_intent_excludes_mutators(string query, string capability)
    {
        var response = new SuggestService(new Catalog(
                new RtCommandInfo("alter", true, true, "refactor", capability, []),
                Info("outline", capability)))
            .Suggest(new SuggestRequest(query));

        response.Results.Select(result => result.Name).ShouldBe(["outline"]);
    }

    [Theory]
    [InlineData("fns", "fix-namespace")]
    [InlineData("mns", "make-instance")]
    public void short_aliases_do_not_declare_unregistered_singular_names(string alias, string expected)
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<SuggestService>();

        service.Suggest(new SuggestRequest(alias, Top: 1)).Results.Single().Name.ShouldBe(expected);
        var singular = service.Suggest(new SuggestRequest(alias[..^1]));
        singular.Outcome.ShouldBe(SuggestOutcome.Unsupported);
        singular.Results.ShouldBeEmpty();
    }
}
