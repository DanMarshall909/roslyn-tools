using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal record SmellResult(
    string SmellType,
    string TypeName,
    string? MemberName,
    string? FilePath,
    int Line,
    string Detail);

internal static class SmellAnalyzer
{
    internal static async Task<List<SmellResult>> AnalyzeAsync(
        Solution solution,
        int maxClassLines,
        int maxMethodLines,
        int maxParams,
        string? scope)
    {
        var results = new List<SmellResult>();
        var rtIgnore = RtIgnore.ForSolution(solution);

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                if (rtIgnore.IsIgnored(document.FilePath))
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var filePath = document.FilePath;

                var typeDeclarations = root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(t => t is ClassDeclarationSyntax or RecordDeclarationSyntax);

                foreach (var typeDecl in typeDeclarations)
                {
                    if (!MatchesScope(GetTypeScopeName(typeDecl), scope)
                        && !MatchesScope(project.Name, scope))
                        continue;

                    var largeClass = LargeClassRule.Check(typeDecl, filePath, maxClassLines);
                    if (largeClass is not null)
                        results.Add(largeClass);

                    results.AddRange(LongMethodRule.Check(typeDecl, filePath, maxMethodLines));

                    var tooManyDeps = TooManyDependenciesRule.Check(typeDecl, filePath, maxParams);
                    if (tooManyDeps is not null)
                        results.Add(tooManyDeps);
                }
            }

        return results;
    }

    internal static bool MatchesScope(string typeName, string? scope)
    {
        if (scope is null)
            return true;

        return typeName.StartsWith(scope, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetTypeScopeName(TypeDeclarationSyntax typeDecl)
    {
        var names = new Stack<string>();
        names.Push(typeDecl.Identifier.Text);

        for (var parent = typeDecl.Parent; parent is not null; parent = parent.Parent)
            switch (parent)
            {
                case TypeDeclarationSyntax containingType:
                    names.Push(containingType.Identifier.Text);
                    break;
                case FileScopedNamespaceDeclarationSyntax fileScopedNamespace:
                    return fileScopedNamespace.Name + "." + string.Join(".", names);
                case NamespaceDeclarationSyntax namespaceDeclaration:
                    return namespaceDeclaration.Name + "." + string.Join(".", names);
            }

        return string.Join(".", names);
    }
}
