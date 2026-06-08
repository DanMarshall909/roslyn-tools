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

    [Fact]
    public void each_top_level_type_kind_is_classified_with_its_own_kind()
    {
        const string text = """
            namespace Acme;

            public class C { }
            public struct S { }
            public record R { }
            public interface I { }
            public enum E { One }
            public delegate void D(int x);
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("t.cs", text)],
            after: [Source("t.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.C" && d.Kind == DeclarationKind.Class);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.S" && d.Kind == DeclarationKind.Struct);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.R" && d.Kind == DeclarationKind.Record);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.I" && d.Kind == DeclarationKind.Interface);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.E" && d.Kind == DeclarationKind.Enum);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.D" && d.Kind == DeclarationKind.Delegate);
    }

    [Fact]
    public void enum_members_are_not_reported_as_declarations()
    {
        const string text = """
            namespace Acme;

            public enum Color { Red, Green, Blue }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("e.cs", text)],
            after: [Source("e.cs", text)]));

        result.Deltas.Count.ShouldBe(1);
        result.Deltas[0].Fqn.ShouldBe("Acme.Color");
    }

    [Fact]
    public void each_member_kind_is_classified_with_its_own_kind()
    {
        const string text = """
            namespace Acme;

            public class A
            {
                public A() { }
                public int Field;
                public int Prop { get; set; }
                public event System.Action Tick;
                public event System.Action Boom { add { } remove { } }
                public int this[int i] => i;
                public static A operator +(A x, A y) => x;
                public static explicit operator int(A a) => 0;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", text)],
            after: [Source("a.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.A()" && d.Kind == DeclarationKind.Constructor);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Field" && d.Kind == DeclarationKind.Field);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Prop" && d.Kind == DeclarationKind.Property);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Tick" && d.Kind == DeclarationKind.Event);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Boom" && d.Kind == DeclarationKind.Event);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.this(int)" && d.Kind == DeclarationKind.Indexer);
        result.Deltas.ShouldContain(d =>
            d.Fqn == "Acme.A.operator +(A,A)" && d.Kind == DeclarationKind.Operator);
        result.Deltas.ShouldContain(d =>
            d.Fqn == "Acme.A.operator int(A)" && d.Kind == DeclarationKind.Operator);
    }

    [Fact]
    public void a_comma_separated_field_declarator_yields_one_delta_per_variable()
    {
        const string text = """
            namespace Acme;

            public class A
            {
                public int x, y;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", text)],
            after: [Source("a.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.x" && d.Kind == DeclarationKind.Field);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.y" && d.Kind == DeclarationKind.Field);
    }

    [Fact]
    public void generic_arity_is_part_of_the_type_and_method_key()
    {
        const string text = """
            namespace Acme;

            public class Box<T>
            {
                public void Map<U>(T input) { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("b.cs", text)],
            after: [Source("b.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.Box`1" && d.Kind == DeclarationKind.Class);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.Box`1.Map`1(T)" && d.Kind == DeclarationKind.Method);
    }

    [Fact]
    public void nested_types_are_keyed_by_their_containing_type_path()
    {
        const string text = """
            namespace Acme;

            public class Outer
            {
                public class Inner
                {
                    public void Go() { }
                }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("n.cs", text)],
            after: [Source("n.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.Outer.Inner" && d.Kind == DeclarationKind.Class);
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.Outer.Inner.Go()" && d.Kind == DeclarationKind.Method);
    }

    [Fact]
    public void block_scoped_and_dotted_namespaces_contribute_to_the_key()
    {
        const string text = """
            namespace Acme.Deep.Space
            {
                public class Ship { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("s.cs", text)],
            after: [Source("s.cs", text)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.Deep.Space.Ship");
    }

    [Fact]
    public void parameter_type_whitespace_is_normalized_so_overloads_do_not_split_spuriously()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public void Run(System.Collections.Generic.Dictionary<int, string> m) { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void Run(System.Collections.Generic.Dictionary<int,string> m) { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        var method = result.Deltas.Single(d => d.Kind == DeclarationKind.Method);
        method.Fqn.ShouldBe("Acme.A.Run(System.Collections.Generic.Dictionary<int,string>)");
        method.Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_partial_class_split_across_two_documents_does_not_throw_on_duplicate_keys()
    {
        const string part1 = """
            namespace Acme;

            public partial class A
            {
                public void First() { }
            }
            """;
        const string part2 = """
            namespace Acme;

            public partial class A
            {
                public void Second() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a1.cs", part1), Source("a2.cs", part2)],
            after: [Source("a1.cs", part1), Source("a2.cs", part2)]));

        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A");
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.First()");
        result.Deltas.ShouldContain(d => d.Fqn == "Acme.A.Second()");
    }

    [Fact]
    public void a_request_exposes_options_that_default_to_not_ignoring_accessibility()
    {
        // AC6 (smoke): the options seam exists, defaults are safe, and a request is
        // constructible without specifying options. IgnoreAccessibility logic is a
        // later cycle — here we only assert the contract is present and wired.
        var defaultOptions = new SemanticDiffOptions();
        defaultOptions.IgnoreAccessibility.ShouldBeFalse();
        defaultOptions.DetectRenames.ShouldBeTrue();

        var request = Request(before: [], after: []);
        request.Options.ShouldNotBeNull();
        request.Options.IgnoreAccessibility.ShouldBeFalse();
        request.Options.DetectRenames.ShouldBeTrue();

        var customised = request with { Options = new SemanticDiffOptions { IgnoreAccessibility = true } };
        customised.Options.IgnoreAccessibility.ShouldBeTrue();
    }

    [Fact]
    public void renamed_is_a_defined_status_member_reserved_for_a_later_cycle()
    {
        // AC6 (smoke): Renamed is part of the contract now so serializers are stable,
        // even though the rename post-pass that produces it lands in a later cycle.
        System.Enum.IsDefined(DeclarationStatus.Renamed).ShouldBeTrue();
    }

    [Fact]
    public void renamed_delta_carries_old_and_new_fqn_while_unpaired_deltas_keep_null_identity()
    {
        // Cycle 5 update (was: delta_exposes_old_and_new_fqn_identity_left_null_...).
        // Two empty same-kind type headers — Gone removed, Fresh added — are equivalent
        // modulo their own name, so rename detection (on by default) pairs them into a
        // single Renamed delta carrying old->new identity. Stable is untouched on both
        // sides and stays Unchanged with null OldFqn/NewFqn, proving the fqn fields are
        // populated only on the Renamed delta.
        const string before = """
            namespace Acme;

            public class Gone { }
            public class Stable { }
            """;
        const string after = """
            namespace Acme;

            public class Fresh { }
            public class Stable { }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Status == DeclarationStatus.Renamed);
        renamed.Fqn.ShouldBe("Acme.Fresh");
        renamed.OldFqn.ShouldBe("Acme.Gone");
        renamed.NewFqn.ShouldBe("Acme.Fresh");

        var stable = result.Deltas.Single(d => d.Fqn == "Acme.Stable");
        stable.Status.ShouldBe(DeclarationStatus.Unchanged);
        stable.OldFqn.ShouldBeNull();
        stable.NewFqn.ShouldBeNull();

        // No separate Added/Removed survives for the paired declarations.
        result.Deltas.ShouldNotContain(d => d.Fqn == "Acme.Gone");
        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Added);
        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Removed);
    }

    [Fact]
    public void a_method_with_a_genuinely_different_body_on_both_sides_is_changed_with_both_paths()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x * 2;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var method = result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)");
        method.Status.ShouldBe(DeclarationStatus.Changed);
        method.OldPath.ShouldBe("before.cs");
        method.NewPath.ShouldBe("after.cs");

        // No bubble-up: the containing type stays Unchanged (type-header comparison
        // is deferred to a later cycle). A member edit must not mark the type Changed.
        var type = result.Deltas.Single(d => d.Fqn == "Acme.A");
        type.Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void two_bodies_with_the_same_structure_but_different_identifiers_and_literals_are_changed()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Compute(int a) => a + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Compute(int b) => b + 2;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        var method = result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)");
        method.Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_member_differing_only_in_comments_is_unchanged()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                /// <summary>Adds one.</summary>
                // line comment
                public int Compute(int x) => x + 1 /* trailing */;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        var method = result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)");
        method.Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_member_differing_only_in_whitespace_and_brace_style_is_unchanged()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Compute(int x)
                {
                    return x + 1;
                }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) {
                        return x   +   1; }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        var method = result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)");
        method.Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_member_with_an_equivalent_body_moved_to_a_different_path_is_unchanged_with_both_paths()
    {
        const string text = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x + 1;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("old/A.cs", text)],
            after: [Source("new/A.cs", text)]));

        var method = result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)");
        method.Status.ShouldBe(DeclarationStatus.Unchanged);
        method.OldPath.ShouldBe("old/A.cs");
        method.NewPath.ShouldBe("new/A.cs");
        method.OldPath.ShouldNotBe(method.NewPath);
    }

    [Fact]
    public void a_member_moved_to_a_different_path_and_edited_is_changed_with_distinct_paths()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x * 2;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("old/A.cs", before)],
            after: [Source("new/A.cs", after)]));

        var method = result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)");
        method.Status.ShouldBe(DeclarationStatus.Changed);
        method.OldPath.ShouldBe("old/A.cs");
        method.NewPath.ShouldBe("new/A.cs");
        method.OldPath.ShouldNotBe(method.NewPath);
    }

    // ----- Cycle 3: type-header-only comparison (no member bubble-up) -----

    [Fact]
    public void a_type_that_gains_a_modifier_in_its_own_header_is_changed()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        const string after = """
            namespace Acme;

            public sealed class A
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_type_that_gains_a_base_interface_is_changed()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A : System.IDisposable
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_type_whose_type_parameter_is_renamed_at_the_same_arity_is_changed()
    {
        // Same arity keeps the key (Acme.Box`1) so the type pairs across sides;
        // the identifier T -> U is a header change and must register as Changed.
        const string before = """
            namespace Acme;

            public class Box<T>
            {
                public void Set(T value) { }
            }
            """;
        const string after = """
            namespace Acme;

            public class Box<U>
            {
                public void Set(U value) { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("b.cs", before)],
            after: [Source("b.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Box`1").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_type_whose_constraint_clause_changes_is_changed()
    {
        const string before = """
            namespace Acme;

            public class Box<T> where T : class
            {
            }
            """;
        const string after = """
            namespace Acme;

            public class Box<T> where T : struct
            {
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("b.cs", before)],
            after: [Source("b.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Box`1").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_type_that_gains_an_attribute_on_its_own_header_is_changed()
    {
        const string before = """
            namespace Acme;

            public class A
            {
            }
            """;
        const string after = """
            namespace Acme;

            [System.Obsolete]
            public class A
            {
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void adding_a_member_leaves_the_containing_type_unchanged_and_reports_the_member_added()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void Run() { }
                public void Extra() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A").Status.ShouldBe(DeclarationStatus.Unchanged);
        result.Deltas.Single(d => d.Fqn == "Acme.A.Extra()").Status.ShouldBe(DeclarationStatus.Added);
    }

    [Fact]
    public void removing_a_member_leaves_the_containing_type_unchanged_and_reports_the_member_removed()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public void Run() { }
                public void Gone() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A").Status.ShouldBe(DeclarationStatus.Unchanged);
        result.Deltas.Single(d => d.Fqn == "Acme.A.Gone()").Status.ShouldBe(DeclarationStatus.Removed);
    }

    [Fact]
    public void editing_a_nested_member_body_leaves_both_the_nested_type_and_the_outer_type_unchanged()
    {
        const string before = """
            namespace Acme;

            public class Outer
            {
                public class Inner
                {
                    public int Go() => 1;
                }
            }
            """;
        const string after = """
            namespace Acme;

            public class Outer
            {
                public class Inner
                {
                    public int Go() => 2;
                }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("n.cs", before)],
            after: [Source("n.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Outer.Inner.Go()").Status.ShouldBe(DeclarationStatus.Changed);
        result.Deltas.Single(d => d.Fqn == "Acme.Outer.Inner").Status.ShouldBe(DeclarationStatus.Unchanged);
        result.Deltas.Single(d => d.Fqn == "Acme.Outer").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_header_change_on_a_deeply_nested_type_is_reported_only_on_that_type()
    {
        const string before = """
            namespace Acme;

            public class Outer
            {
                public class Inner
                {
                    public class Deepest
                    {
                    }
                }
            }
            """;
        const string after = """
            namespace Acme;

            public class Outer
            {
                public class Inner
                {
                    public sealed class Deepest
                    {
                    }
                }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("n.cs", before)],
            after: [Source("n.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Outer.Inner.Deepest").Status.ShouldBe(DeclarationStatus.Changed);
        result.Deltas.Single(d => d.Fqn == "Acme.Outer.Inner").Status.ShouldBe(DeclarationStatus.Unchanged);
        result.Deltas.Single(d => d.Fqn == "Acme.Outer").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_nested_type_with_identical_text_moved_to_a_different_path_is_unchanged_with_both_paths()
    {
        const string text = """
            namespace Acme;

            public class Outer
            {
                public class Inner
                {
                    public int Go() => 1;
                }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("old/Outer.cs", text)],
            after: [Source("new/Outer.cs", text)]));

        var inner = result.Deltas.Single(d => d.Fqn == "Acme.Outer.Inner");
        inner.Status.ShouldBe(DeclarationStatus.Unchanged);
        inner.OldPath.ShouldBe("old/Outer.cs");
        inner.NewPath.ShouldBe("new/Outer.cs");
        inner.OldPath.ShouldNotBe(inner.NewPath);
    }

    [Fact]
    public void changing_a_record_positional_parameter_marks_the_record_changed()
    {
        const string before = """
            namespace Acme;

            public record R(int X);
            """;
        const string after = """
            namespace Acme;

            public record R(int Y);
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("r.cs", before)],
            after: [Source("r.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.R").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void changing_an_enum_underlying_type_in_its_header_is_changed()
    {
        const string before = """
            namespace Acme;

            public enum Color { Red, Green }
            """;
        const string after = """
            namespace Acme;

            public enum Color : byte { Red, Green }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("e.cs", before)],
            after: [Source("e.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Color").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void an_enum_with_identical_text_on_both_sides_is_unchanged()
    {
        // Locks branch-2's true-return: a same-text enum compares header-shell-equal.
        const string text = """
            namespace Acme;

            public enum Color { Red, Green }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("e.cs", text)],
            after: [Source("e.cs", text)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Color").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_type_replaced_by_an_enum_at_the_same_fqn_is_changed_without_throwing()
    {
        // class Foo -> enum Foo keeps the key Acme.Foo but flips the node category.
        // Exercises the false-branch of branch-1's both-sided guard (b is not a
        // TypeDeclarationSyntax): the comparison falls through to the whole-node
        // path and reports Changed instead of throwing InvalidCastException.
        const string before = """
            namespace Acme;

            public class Foo
            {
            }
            """;
        const string after = """
            namespace Acme;

            public enum Foo { One }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("f.cs", before)],
            after: [Source("f.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Foo").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void an_enum_replaced_by_a_class_at_the_same_fqn_is_changed_without_throwing()
    {
        // The mirror of the previous test: exercises the false-branch of branch-2's
        // both-sided guard (b is not an EnumDeclarationSyntax). Fully condition-covers
        // the shell-strip guards across both declaration-with-members categories.
        const string before = """
            namespace Acme;

            public enum Foo { One }
            """;
        const string after = """
            namespace Acme;

            public class Foo
            {
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("f.cs", before)],
            after: [Source("f.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.Foo").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void changing_a_delegate_signature_is_changed()
    {
        const string before = """
            namespace Acme;

            public delegate void D(int x);
            """;
        const string after = """
            namespace Acme;

            public delegate void D(string x);
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("d.cs", before)],
            after: [Source("d.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.D").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void reordering_members_within_a_type_leaves_the_type_and_every_member_unchanged()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public void First() { }
                public void Second() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void Second() { }
                public void First() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.ShouldAllBe(d => d.Status == DeclarationStatus.Unchanged);
    }

    [Fact]
    public void reordering_using_directives_leaves_all_declarations_unchanged()
    {
        const string before = """
            using System;
            using System.Collections.Generic;

            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        const string after = """
            using System.Collections.Generic;
            using System;

            namespace Acme;

            public class A
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.ShouldNotBeEmpty();
        result.Deltas.ShouldAllBe(d => d.Status == DeclarationStatus.Unchanged);
    }

    [Fact]
    public void deltas_are_ordered_deterministically_by_fqn()
    {
        const string text = """
            namespace Acme;

            public class Zebra { }
            public class Apple { }
            public class Mango { }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("z.cs", text)],
            after: [Source("z.cs", text)]));

        var fqns = result.Deltas.Select(d => d.Fqn).ToList();
        fqns.ShouldBe(fqns.OrderBy(f => f, StringComparer.Ordinal).ToList());
    }

    // ----- Cycle 4: IgnoreAccessibility option -----

    private static SemanticDiffRequest IgnoringAccessibility(
        SemanticDiffSource[] before,
        SemanticDiffSource[] after) =>
        new(before, after, new SemanticDiffOptions(IgnoreAccessibility: true));

    [Fact]
    public void a_method_differing_only_in_accessibility_is_unchanged_when_accessibility_is_ignored()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                private void M() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void M() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.M()").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_method_differing_only_in_accessibility_is_changed_under_default_options()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                private void M() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void M() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.M()").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_type_header_differing_only_in_accessibility_is_unchanged_when_accessibility_is_ignored()
    {
        const string before = """
            namespace Acme;

            internal class C
            {
                public void Run() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class C
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("c.cs", before)],
            after: [Source("c.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.C").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_type_header_differing_only_in_accessibility_is_changed_under_default_options()
    {
        const string before = """
            namespace Acme;

            internal class C
            {
                public void Run() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class C
            {
                public void Run() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("c.cs", before)],
            after: [Source("c.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.C").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_non_accessibility_modifier_change_is_still_changed_when_accessibility_is_ignored()
    {
        // Critical: stripping must touch ONLY accessibility. An instance->static
        // change is a real modifier difference and must remain Changed even with
        // the flag on.
        const string before = """
            namespace Acme;

            public class A
            {
                public void M() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public static void M() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.M()").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void a_non_accessibility_type_modifier_change_is_still_changed_when_accessibility_is_ignored()
    {
        // The type-header mirror of the member case: non-sealed -> sealed is a real
        // header modifier change and must remain Changed even with the flag on.
        const string before = """
            namespace Acme;

            public class A
            {
            }
            """;
        const string after = """
            namespace Acme;

            public sealed class A
            {
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void an_accessibility_change_alongside_a_real_body_change_is_still_changed_when_accessibility_is_ignored()
    {
        // Stripping accessibility must not mask a genuine body difference: the body
        // change dominates and the member is Changed.
        const string before = """
            namespace Acme;

            public class A
            {
                private int Compute(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Compute(int x) => x * 2;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.Compute(int)").Status.ShouldBe(DeclarationStatus.Changed);
    }

    [Fact]
    public void combined_accessibility_forms_are_treated_as_equivalent_when_accessibility_is_ignored()
    {
        // protected internal -> public and private protected -> internal: both are
        // pure accessibility-form changes and must report Unchanged with the flag on.
        const string before = """
            namespace Acme;

            public class A
            {
                protected internal void M() { }
                private protected void N() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public void M() { }
                internal void N() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("a.cs", before)],
            after: [Source("a.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.M()").Status.ShouldBe(DeclarationStatus.Unchanged);
        result.Deltas.Single(d => d.Fqn == "Acme.A.N()").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    [Fact]
    public void a_file_local_type_changed_to_a_named_accessibility_is_unchanged_when_accessibility_is_ignored()
    {
        // `file` is an accessibility modifier (a contextual keyword valid only on a
        // top-level type). file -> internal is a pure accessibility change and must
        // report Unchanged with the flag on.
        const string before = """
            namespace Acme;

            file class C
            {
            }
            """;
        const string after = """
            namespace Acme;

            internal class C
            {
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(IgnoringAccessibility(
            before: [Source("c.cs", before)],
            after: [Source("c.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.C").Status.ShouldBe(DeclarationStatus.Unchanged);
    }

    // ----- Cycle 5: rename detection (Renamed status, pure-rename pairing) -----

    [Fact]
    public void a_method_renamed_with_an_identical_body_is_one_renamed_delta_carrying_old_and_new_fqn()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Foo(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Bar(int x) => x + 1;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Method);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.Fqn.ShouldBe("Acme.A.Bar(int)");
        renamed.OldFqn.ShouldBe("Acme.A.Foo(int)");
        renamed.NewFqn.ShouldBe("Acme.A.Bar(int)");
        renamed.OldPath.ShouldBe("before.cs");
        renamed.NewPath.ShouldBe("after.cs");

        result.Deltas.ShouldNotContain(d => d.Fqn == "Acme.A.Foo(int)");
    }

    [Fact]
    public void a_method_rename_is_left_as_added_and_removed_when_rename_detection_is_off()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Foo(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Bar(int x) => x + 1;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(new SemanticDiffRequest(
            [Source("before.cs", before)],
            [Source("after.cs", after)],
            new SemanticDiffOptions(DetectRenames: false)));

        result.Deltas.Single(d => d.Fqn == "Acme.A.Foo(int)").Status.ShouldBe(DeclarationStatus.Removed);
        result.Deltas.Single(d => d.Fqn == "Acme.A.Bar(int)").Status.ShouldBe(DeclarationStatus.Added);
        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Renamed);
    }

    [Fact]
    public void a_type_renamed_with_an_equivalent_header_and_members_is_reported_renamed()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Go() => 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class B
            {
                public int Go() => 1;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamedType = result.Deltas.Single(d =>
            d.Kind == DeclarationKind.Class && d.Status == DeclarationStatus.Renamed);
        renamedType.Fqn.ShouldBe("Acme.B");
        renamedType.OldFqn.ShouldBe("Acme.A");
        renamedType.NewFqn.ShouldBe("Acme.B");

        result.Deltas.ShouldNotContain(d => d.Fqn == "Acme.A");
    }

    [Fact]
    public void a_rename_combined_with_a_genuine_body_change_stays_added_and_removed()
    {
        // v1 limitation: only PURE renames pair. A name change plus a body change is
        // not equivalent-modulo-name, so the pair does not collapse.
        const string before = """
            namespace Acme;

            public class A
            {
                public int Foo(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Bar(int x) => x * 99;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.Foo(int)").Status.ShouldBe(DeclarationStatus.Removed);
        result.Deltas.Single(d => d.Fqn == "Acme.A.Bar(int)").Status.ShouldBe(DeclarationStatus.Added);
        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Renamed);
    }

    [Fact]
    public void a_removed_method_and_an_added_property_with_equivalent_text_do_not_pair_across_kinds()
    {
        // Different DeclarationKind must never pair, even if the canonical text aligns.
        // The removed one is a method, the added one a property, so kinds differ.
        const string beforeMethod = """
            namespace Acme;

            public class A
            {
                public int Foo() => 0;
            }
            """;
        const string afterProp = """
            namespace Acme;

            public class A
            {
                public int Bar => 0;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", beforeMethod)],
            after: [Source("after.cs", afterProp)]));

        result.Deltas.Single(d => d.Fqn == "Acme.A.Foo()").Status.ShouldBe(DeclarationStatus.Removed);
        result.Deltas.Single(d => d.Fqn == "Acme.A.Bar").Status.ShouldBe(DeclarationStatus.Added);
        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Renamed);
    }

    [Fact]
    public void a_rename_that_also_changes_accessibility_is_renamed_when_accessibility_is_ignored()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                private int Foo(int x) => x + 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Bar(int x) => x + 1;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(new SemanticDiffRequest(
            [Source("before.cs", before)],
            [Source("after.cs", after)],
            new SemanticDiffOptions(IgnoreAccessibility: true)));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Method);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.A.Foo(int)");
        renamed.NewFqn.ShouldBe("Acme.A.Bar(int)");
    }

    [Fact]
    public void ambiguous_same_kind_renames_pair_deterministically_by_ordinal_fqn()
    {
        // Two removed (A, B), two added (C, D), all methods with identical-modulo-name
        // bodies. Greedy Ordinal pairing: removed sorted [A, B], added candidates in
        // Ordinal order [C, D]. A takes the first match C; B takes the next D.
        const string before = """
            namespace Acme;

            public class T
            {
                public int A() => 0;
                public int B() => 0;
            }
            """;
        const string after = """
            namespace Acme;

            public class T
            {
                public int C() => 0;
                public int D() => 0;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renames = result.Deltas
            .Where(d => d.Status == DeclarationStatus.Renamed)
            .ToList();
        renames.Count.ShouldBe(2);

        var ac = renames.Single(d => d.NewFqn == "Acme.T.C()");
        ac.OldFqn.ShouldBe("Acme.T.A()");

        var bd = renames.Single(d => d.NewFqn == "Acme.T.D()");
        bd.OldFqn.ShouldBe("Acme.T.B()");

        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Added);
        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Removed);
    }

    [Fact]
    public void a_renamed_property_with_an_identical_body_is_one_renamed_delta()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int Foo => 0;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int Bar => 0;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Property);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.A.Foo");
        renamed.NewFqn.ShouldBe("Acme.A.Bar");
    }

    [Fact]
    public void a_renamed_field_with_an_identical_type_is_one_renamed_delta()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public int foo;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int bar;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Field);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.A.foo");
        renamed.NewFqn.ShouldBe("Acme.A.bar");
    }

    [Fact]
    public void a_renamed_field_form_event_with_an_identical_type_is_one_renamed_delta()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public event System.Action Foo;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public event System.Action Bar;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Event);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.A.Foo");
        renamed.NewFqn.ShouldBe("Acme.A.Bar");
    }

    [Fact]
    public void a_renamed_declaration_form_event_with_identical_accessors_is_one_renamed_delta()
    {
        const string before = """
            namespace Acme;

            public class A
            {
                public event System.Action Foo { add { } remove { } }
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public event System.Action Bar { add { } remove { } }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Event);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.A.Foo");
        renamed.NewFqn.ShouldBe("Acme.A.Bar");
    }

    [Fact]
    public void a_renamed_delegate_with_an_identical_signature_is_one_renamed_delta()
    {
        const string before = """
            namespace Acme;

            public delegate void Foo(int x);
            """;
        const string after = """
            namespace Acme;

            public delegate void Bar(int x);
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Delegate);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.Foo");
        renamed.NewFqn.ShouldBe("Acme.Bar");
    }

    [Fact]
    public void a_renamed_enum_with_identical_members_is_one_renamed_delta()
    {
        const string before = """
            namespace Acme;

            public enum Foo { One, Two }
            """;
        const string after = """
            namespace Acme;

            public enum Bar { One, Two }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamed = result.Deltas.Single(d => d.Kind == DeclarationKind.Enum);
        renamed.Status.ShouldBe(DeclarationStatus.Renamed);
        renamed.OldFqn.ShouldBe("Acme.Foo");
        renamed.NewFqn.ShouldBe("Acme.Bar");
    }

    [Fact]
    public void renaming_a_type_cascades_its_constructor_to_a_renamed_delta()
    {
        // A ctor's identifier IS the type name, so a ctor rename can only ride a type
        // rename. The input produces a Renamed type AND a Renamed ctor (the documented
        // member-cascade behaviour). Covers the ConstructorDeclarationSyntax arm.
        const string before = """
            namespace Acme;

            public class A
            {
                public A() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class B
            {
                public B() { }
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        var renamedCtor = result.Deltas.Single(d => d.Kind == DeclarationKind.Constructor);
        renamedCtor.Status.ShouldBe(DeclarationStatus.Renamed);
        renamedCtor.OldFqn.ShouldBe("Acme.A.A()");
        renamedCtor.NewFqn.ShouldBe("Acme.B.B()");

        var renamedType = result.Deltas.Single(d => d.Kind == DeclarationKind.Class);
        renamedType.Status.ShouldBe(DeclarationStatus.Renamed);
    }

    [Fact]
    public void indexers_and_operators_are_never_paired_as_renames()
    {
        // Indexers (named `this`) and operators (keyed on a token, not a name) have no
        // renameable own-name, so an apparent "rename" of either stays Added + Removed.
        const string before = """
            namespace Acme;

            public class A
            {
                public int this[int i] => i;
                public static A operator +(A x, A y) => x;
            }
            """;
        const string after = """
            namespace Acme;

            public class A
            {
                public int this[string s] => 0;
                public static A operator -(A x, A y) => x;
            }
            """;
        var service = new SemanticDiffService();

        var result = service.Compare(Request(
            before: [Source("before.cs", before)],
            after: [Source("after.cs", after)]));

        result.Deltas.ShouldNotContain(d => d.Status == DeclarationStatus.Renamed);
        result.Deltas.ShouldContain(d => d.Kind == DeclarationKind.Indexer && d.Status == DeclarationStatus.Removed);
        result.Deltas.ShouldContain(d => d.Kind == DeclarationKind.Indexer && d.Status == DeclarationStatus.Added);
        result.Deltas.ShouldContain(d => d.Kind == DeclarationKind.Operator && d.Status == DeclarationStatus.Removed);
        result.Deltas.ShouldContain(d => d.Kind == DeclarationKind.Operator && d.Status == DeclarationStatus.Added);
    }
}
