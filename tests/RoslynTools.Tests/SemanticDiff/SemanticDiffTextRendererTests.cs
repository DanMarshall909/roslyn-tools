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
}
