using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class RoslynSyntax
{
    internal static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root, string ns)
    {
        var existing = root.Usings
            .Select(u => u.Name?.ToString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existing.Contains(ns))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(" " + ns))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        return root.AddUsings(newUsing);
    }
}

internal static class SymbolDocumentLocator
{
    internal static DocumentId? FindDocumentId(Solution solution, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
            return null;

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return doc.Id;
                }
            }
        }

        return null;
    }
}
