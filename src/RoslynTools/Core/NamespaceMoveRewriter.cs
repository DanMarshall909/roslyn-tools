using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// A class used to rewrite namespace moves in the code.
/// </summary>
internal sealed class NamespaceMoveRewriter : CSharpSyntaxRewriter
{
    private readonly string _newNs;
    private readonly string _oldNs;

    public NamespaceMoveRewriter(string oldNs, string newNs)
    {
        _oldNs = oldNs;
        _newNs = newNs;
    }

    public bool Changed { get; private set; }
    public bool IsDeclarationFile { get; private set; }

    /// <summary>Rewrites an exact block-scoped namespace declaration and records its file role.</summary>
    public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        if (node.Name.ToString() != _oldNs)
            return base.VisitNamespaceDeclaration(node);

        IsDeclarationFile = true;
        Changed = true;
        return base.VisitNamespaceDeclaration(
            node.WithName(SyntaxFactory.ParseName(_newNs).WithTriviaFrom(node.Name)));
    }

    /// <summary>Rewrites an exact file-scoped namespace declaration and records its file role.</summary>
    public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        if (node.Name.ToString() != _oldNs)
            return base.VisitFileScopedNamespaceDeclaration(node);

        IsDeclarationFile = true;
        Changed = true;
        return base.VisitFileScopedNamespaceDeclaration(
            node.WithName(SyntaxFactory.ParseName(_newNs).WithTriviaFrom(node.Name)));
    }

    /// <summary>Rewrites an exact namespace import, including single-segment names.</summary>
    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
    {
        if (node.Name?.ToString() != _oldNs)
            return base.VisitUsingDirective(node);

        Changed = true;
        return base.VisitUsingDirective(
            node.WithName(SyntaxFactory.ParseName(_newNs).WithTriviaFrom(node.Name)));
    }

    public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
    {
        // Visit children first so inner names are rewritten bottom-up
        var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;
        var text = visited.ToString();

        if (text == _oldNs)
        {
            Changed = true;
            return SyntaxFactory.ParseName(_newNs).WithTriviaFrom(visited);
        }

        return visited;
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
        var text = visited.ToString();

        if (text == _oldNs)
        {
            // Stryker disable all
            Changed = true;
            // Stryker restore all
            return SyntaxFactory.ParseExpression(_newNs).WithTriviaFrom(visited);
        }

        return visited;
    }
}
