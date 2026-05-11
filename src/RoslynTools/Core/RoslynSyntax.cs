using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// Syntax class for analyzing and manipulating C# source code.
/// </summary>
internal static class RoslynSyntax
{
    /// <summary>
    /// Method to ensure the use of a specific syntax for a compilation unit.
    /// </summary>
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

/// <summary>
/// Class for locating symbol documents in a solution.
/// </summary>
internal static class SymbolDocumentLocator
{
    /// <summary>
    /// Method to find the document ID for a symbol in a solution.
    /// </summary>
    internal static DocumentId? FindDocumentId(Solution solution, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null)
            return null;

        foreach (var project in solution.Projects)
        foreach (var doc in project.Documents)
            if (doc.FilePath != null &&
                string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                return doc.Id;

        return null;
    }
}
