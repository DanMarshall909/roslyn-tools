using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public sealed class NamespaceMoveRewriterTests
{
    private static (string Result, bool Changed, bool IsDeclarationFile) Rewrite(string source, string oldNs, string newNs)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new NamespaceMoveRewriter(oldNs, newNs);
        var newRoot = rewriter.Visit(root)!;
        return (newRoot.ToFullString(), rewriter.Changed, rewriter.IsDeclarationFile);
    }

    [Fact]
    public void File_scoped_namespace_matching_old_sets_IsDeclarationFile()
    {
        var source = "namespace Old.Ns;\nclass Foo { }";
        var (_, _, isDecl) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(isDecl);
    }

    [Fact]
    public void Block_scoped_namespace_matching_old_sets_IsDeclarationFile()
    {
        var source = "namespace Old.Ns { class Foo { } }";
        var (_, _, isDecl) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(isDecl);
    }

    [Fact]
    public void Non_matching_namespace_does_not_set_IsDeclarationFile()
    {
        var source = "namespace Other.Ns;\nclass Foo { }";
        var (_, _, isDecl) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(isDecl);
    }

    [Fact]
    public void Qualified_name_equal_to_old_ns_is_rewritten()
    {
        var source = "using Old.Ns;";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(changed);
        Assert.Contains("New.Ns", result);
        Assert.DoesNotContain("Old.Ns", result);
    }

    [Fact]
    public void Qualified_name_starting_with_old_ns_is_rewritten()
    {
        var source = "using Old.Ns.Sub;";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(changed);
        Assert.Contains("New.Ns.Sub", result);
    }

    [Fact]
    public void Non_matching_qualified_name_is_not_changed()
    {
        var source = "using Other.Namespace;";
        var (_, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
    }

    [Fact]
    public void Changed_flag_is_false_for_unrelated_source()
    {
        var source = "class Foo { }";
        var (_, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
    }

    [Fact]
    public void Namespace_with_same_prefix_but_different_name_is_not_rewritten()
    {
        // Kills the L50 "." string mutation: if "." is removed, "Old.Nsp".StartsWithOrdinal("Old.Ns")
        // is true and the namespace would be incorrectly rewritten.
        var source = "using Old.Nsp;";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
        Assert.Contains("Old.Nsp", result);
        Assert.DoesNotContain("New.Ns", result);
    }

    [Fact]
    public void Member_access_with_same_prefix_but_different_name_is_not_rewritten()
    {
        // Same test for VisitMemberAccessExpression path.
        var source = "class C { object M() => Old.Nsp.Thing.Value; }";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
        Assert.Contains("Old.Nsp", result);
    }

    [Fact]
    public void Member_access_starting_with_old_ns_is_rewritten()
    {
        // Covers VisitMemberAccessExpression L74 (Changed = true in prefix-match path).
        var source = "class C { object M() { return Old.Ns.Thing.Value; } }";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(changed);
        Assert.Contains("New.Ns", result);
        Assert.DoesNotContain("Old.Ns.Thing", result);
    }
}
