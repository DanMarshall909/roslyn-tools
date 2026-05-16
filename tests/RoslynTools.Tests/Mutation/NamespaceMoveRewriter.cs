using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public sealed class NamespaceMoveRewriter
{
    private static (string Result, bool Changed, bool IsDeclarationFile) Rewrite(string source, string oldNs,
        string newNs)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var rewriter = new Core.NamespaceMoveRewriter(oldNs, newNs);
        var newRoot = rewriter.Visit(root)!;
        return (newRoot.ToFullString(), rewriter.Changed, rewriter.IsDeclarationFile);
    }

    [Fact]
    public void file_scoped_namespace_matching_old_sets_is_declaration_file()
    {
        var source = "namespace Old.Ns;\nclass Foo { }";
        var (_, _, isDecl) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(isDecl);
    }

    [Fact]
    public void block_scoped_namespace_matching_old_sets_is_declaration_file()
    {
        var source = "namespace Old.Ns { class Foo { } }";
        var (_, _, isDecl) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(isDecl);
    }

    [Fact]
    public void rejects_non_matching_namespace_does_not_set_is_declaration_file()
    {
        var source = "namespace Other.Ns;\nclass Foo { }";
        var (_, _, isDecl) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(isDecl);
    }

    [Fact]
    public void rewrites_qualified_name_equal_to_old_namespace()
    {
        var source = "using Old.Ns;";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(changed);
        Assert.Contains("New.Ns", result);
        Assert.DoesNotContain("Old.Ns", result);
    }

    [Fact]
    public void rewrites_qualified_name_starting_with_old_namespace()
    {
        var source = "using Old.Ns.Sub;";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(changed);
        Assert.Contains("New.Ns.Sub", result);
    }

    [Fact]
    public void rejects_non_matching_qualified_name_is_not_changed()
    {
        var source = "using Other.Namespace;";
        var (_, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
    }

    [Fact]
    public void handles_changed_flag_is_false_for_unrelated_source()
    {
        var source = "class Foo { }";
        var (_, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
    }

    [Fact]
    public void handles_namespace_with_same_prefix_but_different_name_is_not_rewritten()
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
    public void handles_member_access_with_same_prefix_but_different_name_is_not_rewritten()
    {
        // Same test for VisitMemberAccessExpression path.
        var source = "class C { object M() => Old.Nsp.Thing.Value; }";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.False(changed);
        Assert.Contains("Old.Nsp", result);
    }

    [Fact]
    public void handles_member_access_starting_with_old_ns_is_rewritten()
    {
        // Covers VisitMemberAccessExpression L74 (Changed = true in prefix-match path).
        var source = "class C { object M() { return Old.Ns.Thing.Value; } }";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");
        Assert.True(changed);
        Assert.Contains("New.Ns", result);
        Assert.DoesNotContain("Old.Ns.Thing", result);
    }

    [Fact]
    public void handles_member_access_equal_to_old_ns_is_rewritten()
    {
        var source = "class C { object M() { return Old.Ns; } }";
        var (result, changed, _) = Rewrite(source, "Old.Ns", "New.Ns");

        Assert.True(changed);
        Assert.Contains("return New.Ns;", result);
        Assert.DoesNotContain("return Old.Ns;", result);
    }

    [Fact]
    public void rewrites_qualified_name_node_starting_with_old_namespace()
    {
        var name = (QualifiedNameSyntax)SyntaxFactory.ParseName("Old.Ns.Sub");
        var rewriter = new Core.NamespaceMoveRewriter("Old.Ns", "New.Ns");

        var result = rewriter.Visit(name)!.ToFullString();

        Assert.True(rewriter.Changed);
        Assert.Equal("New.Ns.Sub", result);
    }

    [Fact]
    public void rewrites_member_access_node_starting_with_old_namespace()
    {
        var expression = (MemberAccessExpressionSyntax)SyntaxFactory.ParseExpression("Old.Ns.Thing");
        var rewriter = new Core.NamespaceMoveRewriter("Old.Ns", "New.Ns");

        var result = rewriter.Visit(expression)!.ToFullString();

        Assert.True(rewriter.Changed);
        Assert.Equal("New.Ns.Thing", result);
    }
}
