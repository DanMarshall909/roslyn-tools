using RoslynTools.Services.SemanticDiff;
using Shouldly;

namespace RoslynTools.Tests.SemanticDiff;

/// <summary>
///     Tests for <see cref="SemanticDiffTextRenderer" /> — the host-agnostic git-diff-style
///     text renderer. The renderer is fed a real <see cref="SemanticDiffResult" /> produced by
///     <see cref="SemanticDiffService" /> plus the same before/after snapshots, and the
///     rendered string is asserted. No host references, no Console (AC: host-agnostic).
/// </summary>
public sealed class SemanticDiffTextRendererTests
{
    private static SemanticDiffSource Source(string path, string text) => new(path, text);

    private static string Render(SemanticDiffSource[] before, SemanticDiffSource[] after)
    {
        var result = new SemanticDiffService().Compare(new SemanticDiffRequest(before, after));
        return new SemanticDiffTextRenderer().Render(result, before, after);
    }

    [Fact]
    public void a_changed_method_renders_a_header_and_a_unified_hunk_with_removed_and_added_body_lines()
    {
        const string before = """
            namespace Acme.Widgets;

            public class Gadget
            {
                public int Compute(int x) => x;
            }
            """;
        const string after = """
            namespace Acme.Widgets;

            public class Gadget
            {
                public int Compute(int x) => x + 1;
            }
            """;

        var output = Render([Source("Gadget.cs", before)], [Source("Gadget.cs", after)]);

        output.ShouldContain("~ Changed");
        output.ShouldContain("Acme.Widgets.Gadget.Compute(int)");
        output.ShouldContain("@@");
        output.ShouldContain("-");
        // The hunk shows the old body removed and the new body added.
        output.ShouldMatch("(?m)^-.*=> x;");
        output.ShouldMatch(@"(?m)^\+.*=> x \+ 1;");
    }

    [Fact]
    public void an_added_declaration_renders_an_added_block_and_a_removed_one_renders_a_removed_block()
    {
        // Distinct KINDS (class added, method removed) so the rename post-pass — on by
        // default — cannot collapse them into a single Renamed delta.
        const string before = """
            namespace Acme;

            public class Host
            {
                public void Gone() { }
            }
            """;
        const string after = """
            namespace Acme;

            public class Host
            {
            }

            public class Helper { }
            """;

        var output = Render([Source("a.cs", before)], [Source("a.cs", after)]);

        output.ShouldContain("+ Added");
        output.ShouldContain("Acme.Helper");
        output.ShouldMatch(@"(?m)^\+.*class Helper");

        output.ShouldContain("- Removed");
        output.ShouldContain("Acme.Host.Gone()");
        output.ShouldMatch("(?m)^-.*void Gone");
    }

    [Fact]
    public void a_renamed_method_renders_old_to_new_and_no_separate_add_or_remove()
    {
        const string before = """
            namespace Acme;

            public class C
            {
                public int Alpha(int n) => n * 2;
            }
            """;
        const string after = """
            namespace Acme;

            public class C
            {
                public int Beta(int n) => n * 2;
            }
            """;

        var output = Render([Source("c.cs", before)], [Source("c.cs", after)]);

        output.ShouldContain("R Renamed");
        output.ShouldContain("Acme.C.Alpha(int) -> Acme.C.Beta(int)");
        output.ShouldNotContain("+ Added");
        output.ShouldNotContain("- Removed");
        // A pure rename is the rename line alone — no body hunk noise.
        output.ShouldNotContain("@@");
    }

    [Fact]
    public void a_class_moved_to_a_different_file_renders_a_single_relocation_note_not_a_delete_and_add()
    {
        const string body = """
            namespace Acme;

            public class Gadget { }
            """;

        var output = Render([Source("Old.cs", body)], [Source("New.cs", body)]);

        output.ShouldContain("> Moved");
        output.ShouldContain("Acme.Gadget");
        output.ShouldContain("Old.cs -> New.cs");
        output.ShouldNotContain("- Removed");
        output.ShouldNotContain("+ Added");
    }

    [Fact]
    public void a_comment_only_change_with_the_same_path_renders_nothing_for_that_declaration()
    {
        const string before = """
            namespace Acme;

            public class C
            {
                public int M() => 1;
            }
            """;
        const string after = """
            namespace Acme;

            public class C
            {
                // a brand new comment
                public int M() => 1;
            }
            """;

        var output = Render([Source("c.cs", before)], [Source("c.cs", after)]);

        output.ShouldBe(SemanticDiffTextRenderer.NoChanges);
    }

    [Fact]
    public void an_empty_result_renders_a_terse_no_changes_line()
    {
        const string body = """
            namespace Acme;

            public class C
            {
                public int M() => 1;
            }
            """;

        var output = Render([Source("c.cs", body)], [Source("c.cs", body)]);

        output.ShouldBe(SemanticDiffTextRenderer.NoChanges);
    }

    [Fact]
    public void multiple_deltas_render_in_ordinal_fqn_order()
    {
        const string before = """
            namespace Acme;

            public class A { }
            public class M { }
            public class Z { }
            """;
        // Add a member to each so all three become Changed and all are emitted, in FQN order.
        const string after = """
            namespace Acme;

            public class A { public int X; }
            public class M { public int X; }
            public class Z { public int X; }
            """;

        var output = Render([Source("s.cs", before)], [Source("s.cs", after)]);

        // Each added field keys as <Type>.X; assert the three appear in Ordinal FQN order.
        var posA = output.IndexOf("Acme.A.X", StringComparison.Ordinal);
        var posM = output.IndexOf("Acme.M.X", StringComparison.Ordinal);
        var posZ = output.IndexOf("Acme.Z.X", StringComparison.Ordinal);

        posA.ShouldBeGreaterThanOrEqualTo(0);
        posA.ShouldBeLessThan(posM);
        posM.ShouldBeLessThan(posZ);
    }

    // ----- US-127: mutation hardening (null guards, kind labels, exact hunk shape) -----

    [Fact]
    public void render_rejects_a_null_result()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SemanticDiffTextRenderer().Render(null!, [], []));
    }

    [Fact]
    public void render_rejects_a_null_before_snapshot()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SemanticDiffTextRenderer().Render(new SemanticDiffResult([]), null!, []));
    }

    [Fact]
    public void render_rejects_a_null_after_snapshot()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SemanticDiffTextRenderer().Render(new SemanticDiffResult([]), [], null!));
    }

    [Fact]
    public void every_declaration_kind_renders_its_own_label()
    {
        // One source carrying every reportable kind, removed wholesale (after is an empty
        // namespace), so each renders a "- Removed  <label> <fqn>" header. Asserts the exact
        // label word for all 13 KindLabel arms so a string mutation on any arm is caught.
        const string before = """
            namespace N;
            public class Holder
            {
                public Holder() { }
                public int M() => 0;
                public int P { get; set; }
                public int Fld;
                public event System.Action Evt;
                public int this[int i] => i;
                public static Holder operator +(Holder a, Holder b) => a;
            }
            public struct St { }
            public record Rec { }
            public interface IFace { }
            public enum En { A }
            public delegate void Del();
            """;

        var output = Render([Source("k.cs", before)], [Source("k.cs", "namespace N;")]);

        output.ShouldContain("- Removed  class N.Holder");
        output.ShouldContain("- Removed  struct N.St");
        output.ShouldContain("- Removed  record N.Rec");
        output.ShouldContain("- Removed  interface N.IFace");
        output.ShouldContain("- Removed  enum N.En");
        output.ShouldContain("- Removed  delegate N.Del");
        output.ShouldContain("- Removed  method N.Holder.M()");
        output.ShouldContain("- Removed  ctor N.Holder.Holder()");
        output.ShouldContain("- Removed  property N.Holder.P");
        output.ShouldContain("- Removed  field N.Holder.Fld");
        output.ShouldContain("- Removed  event N.Holder.Evt");
        output.ShouldContain("- Removed  indexer N.Holder.this(int)");
        output.ShouldContain("- Removed  operator N.Holder.operator +(Holder,Holder)");
    }

    [Fact]
    public void a_mid_body_change_renders_one_hunk_with_correct_line_numbers_counts_and_context()
    {
        const string before = """
            class G { int Compute(int x) { var a = 1; var b = 2; var c = 3; var d = 4; var e = 5; return a; } }
            """;
        const string after = """
            class G { int Compute(int x) { var a = 1; var b = 2; var c = 30; var d = 4; var e = 5; return a; } }
            """;

        var output = Render([Source("g.cs", before)], [Source("g.cs", after)]);

        // Exact unified hunk: @@ header carries the right start line (2) and counts (7/7); the
        // three context lines on each side are prefixed with a single space; the changed line
        // appears once removed and once added. Any line-number, count, prefix or context-window
        // mutation changes this string.
        output.ShouldBe(string.Join("\n",
            "~ Changed  method G.Compute(int)",
            "@@ -2,7 +2,7 @@",
            " {",
            "     var a = 1;",
            "     var b = 2;",
            "-    var c = 3;",
            "+    var c = 30;",
            "     var d = 4;",
            "     var e = 5;",
            "     return a;"));
    }

    [Fact]
    public void two_changes_within_twice_the_context_merge_into_a_single_hunk()
    {
        // The two edits (first and last statement) are separated by exactly 2*ContextSize (6)
        // unchanged lines, so ExtendHunkEnd merges them into one hunk spanning the whole body.
        const string before = """
            class G { int F() { var a = 1; var b = 2; var c = 3; var d = 4; var e = 5; var f = 6; var g = 7; var h = 8; return a; } }
            """;
        const string after = """
            class G { int F() { var a = 10; var b = 2; var c = 3; var d = 4; var e = 5; var f = 6; var g = 7; var h = 80; return a; } }
            """;

        var output = Render([Source("g.cs", before)], [Source("g.cs", after)]);

        output.ShouldBe(string.Join("\n",
            "~ Changed  method G.F()",
            "@@ -1,12 +1,12 @@",
            " int F()",
            " {",
            "-    var a = 1;",
            "+    var a = 10;",
            "     var b = 2;",
            "     var c = 3;",
            "     var d = 4;",
            "     var e = 5;",
            "     var f = 6;",
            "     var g = 7;",
            "-    var h = 8;",
            "+    var h = 80;",
            "     return a;",
            " }"));
    }

    [Fact]
    public void two_changes_beyond_twice_the_context_split_into_two_hunks()
    {
        // The two edits are separated by more than 2*ContextSize unchanged lines, so they do
        // NOT merge: two hunks emit, and the second hunk's @@ start line (9) proves the line
        // counters advanced correctly across the skipped middle.
        const string before = """
            class G { int F() { var a = 1; var b = 2; var c = 3; var d = 4; var e = 5; var f = 6; var g = 7; var h = 8; var i = 9; var j = 10; return a; } }
            """;
        const string after = """
            class G { int F() { var a = 100; var b = 2; var c = 3; var d = 4; var e = 5; var f = 6; var g = 7; var h = 8; var i = 9; var j = 1000; return a; } }
            """;

        var output = Render([Source("g.cs", before)], [Source("g.cs", after)]);

        output.ShouldBe(string.Join("\n",
            "~ Changed  method G.F()",
            "@@ -1,6 +1,6 @@",
            " int F()",
            " {",
            "-    var a = 1;",
            "+    var a = 100;",
            "     var b = 2;",
            "     var c = 3;",
            "     var d = 4;",
            "@@ -9,6 +9,6 @@",
            "     var g = 7;",
            "     var h = 8;",
            "     var i = 9;",
            "-    var j = 10;",
            "+    var j = 1000;",
            "     return a;",
            " }"));
    }

    [Fact]
    public void surfaced_blocks_are_separated_by_a_single_blank_line()
    {
        // A changed type (header gains 'sealed'), an added type, and a removed method — three
        // surfaced blocks in FQN order, each separated by one blank line. Distinct kinds keep
        // the rename pass from collapsing the add/remove. Asserting the exact joined output
        // pins the trailing blank-line separator after every block kind.
        const string before = """
            namespace Q;
            public class A { }
            public class Z { public int Old() => 0; }
            """;
        const string after = """
            namespace Q;
            public sealed class A { }
            public class Z { }
            public class M { }
            """;

        // Same path on both sides so the unchanged class Z stays silent (a different path
        // would make it a "> Moved" block); this test is about block separators, not moves.
        var output = Render([Source("s.cs", before)], [Source("s.cs", after)]);

        output.ShouldBe(string.Join("\n",
            "~ Changed  class Q.A",
            "@@ -1,3 +1,3 @@",
            "-public class A",
            "+public sealed class A",
            " {",
            " }",
            "",
            "+ Added    class Q.M   (s.cs)",
            "+public class M",
            "+{",
            "+}",
            "",
            "- Removed  method Q.Z.Old()   (s.cs)",
            "-public int Old() => 0;"));
    }

    [Fact]
    public void an_added_multiline_declaration_prefixes_every_body_line_with_plus()
    {
        // An added type with a multi-line body exercises AppendPrefixed + SplitLines: every
        // line of the normalized declaration text is emitted with a '+' prefix, the body split
        // on newlines. A mutation to the split or the prefix changes this exact block.
        const string before = "class H { }";
        const string after = """
            class H { }
            class Added { int X() => 1; int Y() => 2; }
            """;

        var output = Render([Source("a.cs", before)], [Source("a.cs", after)]);

        output.ShouldContain(string.Join("\n",
            "+ Added    class Added   (a.cs)",
            "+class Added",
            "+{",
            "+    int X() => 1;",
            "+    int Y() => 2;",
            "+}"));
    }

    [Fact]
    public void a_removed_block_followed_by_another_block_is_separated_by_a_blank_line()
    {
        // Aaa (class) is removed and Zzz (enum) is added — distinct kinds so the rename pass
        // cannot pair them. FQN order puts the Removed block first, so its trailing blank-line
        // separator is observable (the prior separator test had Removed last, where the trailing
        // newline is trimmed away).
        const string before = """
            namespace P;
            class Aaa { }
            """;
        const string after = """
            namespace P;
            enum Zzz { X }
            """;

        var output = Render([Source("r1.cs", before)], [Source("r2.cs", after)]);

        output.ShouldBe(string.Join("\n",
            "- Removed  class P.Aaa   (r1.cs)",
            "-class Aaa",
            "-{",
            "-}",
            "",
            "+ Added    enum P.Zzz   (r2.cs)",
            "+enum Zzz",
            "+{",
            "+    X",
            "+}"));
    }

    [Fact]
    public void a_single_line_declaration_change_renders_a_one_line_hunk()
    {
        // The normalized property text is a single line, so the changed diff entry is the very
        // last entry with no trailing unchanged line — the hunk extends to the end of the
        // entry list. Guards the inner hunk-extension bound (a <= here would read past the end).
        const string before = "class C { int P => 0; }";
        const string after = "class C { int P => 1; }";

        var output = Render([Source("c.cs", before)], [Source("c.cs", after)]);

        output.ShouldBe(string.Join("\n",
            "~ Changed  property C.P",
            "@@ -1,1 +1,1 @@",
            "-int P => 0;",
            "+int P => 1;"));
    }
}
