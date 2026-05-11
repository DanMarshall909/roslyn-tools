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

    public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
    {
        // Check BEFORE visiting children (the name hasn't been rewritten yet)
        if (node.Name.ToString() == _oldNs)
            IsDeclarationFile = true;

        return base.VisitNamespaceDeclaration(node);
    }

    public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
    {
        if (node.Name.ToString() == _oldNs)
            IsDeclarationFile = true;

        return base.VisitFileScopedNamespaceDeclaration(node);
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

        if (text.StartsWithOrdinal(_oldNs + "."))
        {
            // Stryker disable all
            Changed = true;
            // Stryker restore all
            var newText = _newNs + text[_oldNs.Length..];
            return SyntaxFactory.ParseName(newText).WithTriviaFrom(visited);
        }

        return visited;
    }

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // Handle FQ references in expression context (e.g. new TourStore.Models.User { })
        var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
        var text = visited.ToString();

        if (text == _oldNs)
        {
            // Stryker disable all
            Changed = true;
            // Stryker restore all
            return SyntaxFactory.ParseExpression(_newNs).WithTriviaFrom(visited);
        }

        if (text.StartsWithOrdinal(_oldNs + "."))
        {
            Changed = true;
            var newText = _newNs + text[_oldNs.Length..];
            return SyntaxFactory.ParseExpression(newText).WithTriviaFrom(visited);
        }

        return visited;
    }
}
