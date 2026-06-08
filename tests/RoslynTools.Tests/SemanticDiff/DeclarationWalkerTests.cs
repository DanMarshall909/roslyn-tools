using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Services.SemanticDiff;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Tests for <see cref="DeclarationWalker" /> — the shared FQN-keyed declaration
///     index used by both the semantic-diff engine and (from batch 4) the git-diff
///     renderer. These assert the FQN→node contract directly on the walker so the two
///     hosts provably locate declarations by one identical scheme and can never drift.
///     The walker is exercised in-memory (no MSBuild workspace, no host references).
/// </summary>
public sealed class DeclarationWalkerTests
{
    private static SemanticDiffSource Source(string path, string text) => new(path, text);

    private static IReadOnlyDictionary<string, DeclEntry> Collect(string text) =>
        DeclarationWalker.Collect([Source("x.cs", text)]);

    [Fact]
    public void a_top_level_type_keys_as_namespace_dot_type()
    {
        const string text = """
            namespace Ns;

            public class Type { }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.Type");
        entries["Ns.Type"].Kind.ShouldBe(DeclarationKind.Class);
    }

    [Fact]
    public void a_nested_type_keys_by_its_containing_type_path()
    {
        const string text = """
            namespace Ns;

            public class Outer
            {
                public class Inner { }
            }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.Outer.Inner");
        entries["Ns.Outer.Inner"].Kind.ShouldBe(DeclarationKind.Class);
    }

    [Fact]
    public void generic_type_and_method_arity_appears_as_a_backtick_count()
    {
        const string text = """
            namespace Ns;

            public class Box<T>
            {
                public void Map<U>(T input) { }
            }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.Box`1");
        entries.ShouldContainKey("Ns.Box`1.Map`1(T)");
    }

    [Fact]
    public void a_method_keys_with_whitespace_stripped_as_written_parameter_types()
    {
        const string text = """
            namespace Ns;

            public class A
            {
                public void Run(System.Collections.Generic.Dictionary<int, string> m) { }
            }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.A.Run(System.Collections.Generic.Dictionary<int,string>)");
    }

    [Fact]
    public void overloads_with_different_parameter_lists_stay_distinct_keys()
    {
        const string text = """
            namespace Ns;

            public class A
            {
                public void Run(int x) { }
                public void Run(string s) { }
            }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.A.Run(int)");
        entries.ShouldContainKey("Ns.A.Run(string)");
    }

    [Fact]
    public void a_multi_declarator_field_yields_one_entry_per_variable()
    {
        const string text = """
            namespace Ns;

            public class A
            {
                public int x, y;
            }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.A.x");
        entries.ShouldContainKey("Ns.A.y");
        entries["Ns.A.x"].Kind.ShouldBe(DeclarationKind.Field);
        entries["Ns.A.y"].Kind.ShouldBe(DeclarationKind.Field);
    }

    [Fact]
    public void each_multi_declarator_field_entry_carries_its_own_reduced_single_variable_node()
    {
        // US-121 per-declarator fix: every field entry must reference a node holding
        // only its OWN declarator, so an edit to a sibling declarator cannot bleed
        // into this entry's classification downstream.
        const string text = """
            namespace Ns;

            public class A
            {
                public int x, y;
            }
            """;

        var entries = Collect(text);

        var xField = entries["Ns.A.x"].Node.ShouldBeOfType<FieldDeclarationSyntax>();
        xField.Declaration.Variables.Count.ShouldBe(1);
        xField.Declaration.Variables[0].Identifier.Text.ShouldBe("x");

        var yField = entries["Ns.A.y"].Node.ShouldBeOfType<FieldDeclarationSyntax>();
        yField.Declaration.Variables.Count.ShouldBe(1);
        yField.Declaration.Variables[0].Identifier.Text.ShouldBe("y");
    }

    [Fact]
    public void an_enum_type_is_reported_but_its_members_are_not()
    {
        const string text = """
            namespace Ns;

            public enum Color { Red, Green, Blue }
            """;

        var entries = Collect(text);

        entries.ShouldContainKey("Ns.Color");
        entries["Ns.Color"].Kind.ShouldBe(DeclarationKind.Enum);
        entries.Keys.ShouldNotContain("Ns.Color.Red");
        entries.Keys.ShouldNotContain("Ns.Color.Green");
        entries.Keys.ShouldNotContain("Ns.Color.Blue");
    }

    [Fact]
    public void the_located_node_for_a_method_fqn_is_the_matching_method_declaration_syntax()
    {
        const string text = """
            namespace Ns;

            public class A
            {
                public int Compute(int x) => x;
            }
            """;

        var entries = Collect(text);

        var method = entries["Ns.A.Compute(int)"].Node.ShouldBeOfType<MethodDeclarationSyntax>();
        method.Identifier.Text.ShouldBe("Compute");
    }

    [Fact]
    public void on_a_duplicate_key_the_last_collected_declaration_wins()
    {
        // Partial class split across two sources keys A under one FQN; the engine
        // never assumes a unique key per side, so the later source wins rather than
        // throwing. The walker is the single owner of that last-wins rule.
        const string part1 = """
            namespace Ns;

            public partial class A
            {
                public int Marker => 1;
            }
            """;
        const string part2 = """
            namespace Ns;

            public partial class A { }
            """;

        var entries = DeclarationWalker.Collect(
            [Source("first.cs", part1), Source("second.cs", part2)]);

        entries["Ns.A"].Path.ShouldBe("second.cs");
    }
}
