using RoslynTools.Services.SemanticDiff;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Tests for <see cref="SemanticDiffService" /> — the host-agnostic semantic
///     comparison engine. The service is constructed directly with in-memory source
///     strings: no MSBuild workspace, no host references (AC6).
/// </summary>
public sealed class SemanticDiffServiceTests
{
    private static SemanticDiffSource Source(string path, string text) => new(path, text);

    private static SemanticDiffRequest Request(
        SemanticDiffSource[] before,
        SemanticDiffSource[] after) =>
        new(before, after);

    [Fact]
    public void one_type_with_one_method_yields_a_type_delta_and_a_member_delta_with_syntactic_fqns()
    {
        const string text = """
            namespace Acme.Widgets;

            public class Gadget
            {
                public int Compute(int x, string label) => x;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("Gadget.cs", text)],
            after: [Source("Gadget.cs", text)]));

        result.Deltas.ShouldContain(d =>
            d.Fqn == "Acme.Widgets.Gadget" && d.Kind == DeclarationKind.Class);
        result.Deltas.ShouldContain(d =>
            d.Fqn == "Acme.Widgets.Gadget.Compute(int,string)" && d.Kind == DeclarationKind.Method);
    }

    [Fact]
    public void a_declaration_only_in_the_after_set_is_added_with_new_path_and_null_old_path()
    {
        const string before = """
            namespace Acme;

            public class A { }
            """;
        const string after = """
            namespace Acme;

            public class A { }
            public class B { }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var added = result.Deltas.Single(d => d.Fqn == "Acme.B");
        added.Status.ShouldBe(DeclarationStatus.Added);
        added.NewPath.ShouldBe("after.cs");
        added.OldPath.ShouldBeNull();
    }

    [Fact]
    public void an_empty_before_set_makes_every_after_declaration_added()
    {
        const string after = """
            namespace Acme;

            public class A { }
            public class B { }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [],
            after: [Source("after.cs", after)]));

        result.Deltas.ShouldNotBeEmpty();
        result.Deltas.ShouldAllBe(d => d.Status == DeclarationStatus.Added);
    }

    [Fact]
    public void a_declaration_only_in_the_before_set_is_removed_with_old_path_and_null_new_path()
    {
        const string before = """
            namespace Acme;

            public class A { }
            public class B { }
            """;
        const string after = """
            namespace Acme;

            public class A { }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var removed = result.Deltas.Single(d => d.Fqn == "Acme.B");
        removed.Status.ShouldBe(DeclarationStatus.Removed);
        removed.OldPath.ShouldBe("before.cs");
        removed.NewPath.ShouldBeNull();
    }

    [Fact]
    public void an_empty_after_set_makes_every_before_declaration_removed()
    {
        const string before = """
            namespace Acme;

            public class A { }
            public class B { }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: []));

        result.Deltas.ShouldNotBeEmpty();
        result.Deltas.ShouldAllBe(d => d.Status == DeclarationStatus.Removed);
    }

    [Fact]
    public void a_declaration_present_on_both_sides_with_identical_text_is_unchanged_with_both_paths()
    {
        const string text = """
            namespace Acme;

            public class A
            {
                public int Value => 1;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("old.cs", text)],
            after: [Source("new.cs", text)]));

        var delta = result.Deltas.Single(d => d.Fqn == "Acme.A");
        delta.Status.ShouldBe(DeclarationStatus.Unchanged);
        delta.OldPath.ShouldBe("old.cs");
        delta.NewPath.ShouldBe("new.cs");
    }

    [Fact]
    public void identical_before_and_after_sets_produce_only_unchanged_deltas()
    {
        const string text = """
            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", text)],
            after: [Source("a.cs", text)]));

        result.Deltas.ShouldNotBeEmpty();
        result.Deltas.ShouldAllBe(d => d.Status == DeclarationStatus.Unchanged);
    }

    [Fact]
    public void two_overloads_with_different_parameter_lists_are_kept_as_distinct_deltas()
    {
        const string text = """
            namespace Acme;

            public class A
            {
                public void Run(int x) { }
                public void Run(string s) { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", text)],
            after: [Source("a.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Run(int)");
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Run(string)");
    }
}
