using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

internal sealed record UndocumentedSymbol(
    string Fqn,
    string Signature,
    string Kind,
    string FilePath,
    int Line);

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal sealed class FindUndocumentedService : IRtService
{
    internal async Task<List<UndocumentedSymbol>> AnalyzeAsync(
        Solution solution, bool includeMembers, string? scope)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<UndocumentedSymbol>();

        foreach (var project in solution.Projects)
        foreach (var document in project.Documents)
        {
            if (!IsSourceDocument(document)) continue;

            var root = await document.GetSyntaxRootAsync();
            var model = await document.GetSemanticModelAsync();
            if (root is null || model is null || document.FilePath is null) continue;

            Collect(root, model, document.FilePath, includeMembers, scope, seen, results);
        }

        return [.. results.OrderBy(s => s.FilePath).ThenBy(s => s.Line)];
    }

    private static void Collect(
        SyntaxNode root, SemanticModel model, string filePath,
        bool includeMembers, string? scope,
        HashSet<string> seen, List<UndocumentedSymbol> results)
    {
        foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            if (!IsPublic(typeDecl.Modifiers)) continue;
            if (model.GetDeclaredSymbol(typeDecl) is not { } typeSymbol) continue;

            var fqn = typeSymbol.ToDisplayString();
            if (scope is not null && !fqn.StartsWith(scope, StringComparison.Ordinal)) continue;
            if (!seen.Add(fqn)) continue;

            if (!HasXmlDoc(typeDecl))
                results.Add(BuildType(typeSymbol, filePath, typeDecl));

            if (!includeMembers || typeDecl is not TypeDeclarationSyntax typeWithMembers) continue;

            foreach (var member in typeWithMembers.Members)
            {
                if (member is not MemberDeclarationSyntax memberDecl) continue;
                if (!IsPublic(memberDecl.Modifiers)) continue;
                if (HasXmlDoc(memberDecl)) continue;
                if (model.GetDeclaredSymbol(memberDecl) is not { } memberSymbol) continue;

                var memberFqn = memberSymbol.ToDisplayString();
                if (!seen.Add(memberFqn)) continue;

                var line = memberDecl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                results.Add(new UndocumentedSymbol(
                    memberFqn,
                    memberSymbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat),
                    memberSymbol.Kind.ToString().ToLowerInvariant(),
                    filePath,
                    line));
            }
        }
    }

    private static UndocumentedSymbol BuildType(INamedTypeSymbol symbol, string filePath, SyntaxNode decl)
    {
        var line = decl.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var sig = symbol.ToDisplayString(SymbolDisplayFormat.CSharpShortErrorMessageFormat);
        return new UndocumentedSymbol(symbol.ToDisplayString(), sig,
            symbol.TypeKind.ToString().ToLowerInvariant(), filePath, line);
    }

    private static bool HasXmlDoc(SyntaxNode node)
    {
        return node.GetLeadingTrivia().Any(t =>
            t.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
            t.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia));
    }

    private static bool IsPublic(SyntaxTokenList modifiers)
    {
        return modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword));
    }

    private static bool IsSourceDocument(Document doc)
    {
        return doc.FilePath is not null
               && !doc.FilePath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
               && !doc.Name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
               && !doc.Name.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase);
    }
}
