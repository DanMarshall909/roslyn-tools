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

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var filePath = document.FilePath;

                var typeDeclarations = root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(t => t is ClassDeclarationSyntax or RecordDeclarationSyntax);

                foreach (var typeDecl in typeDeclarations)
                {
                    var typeName = typeDecl.Identifier.Text;

                    if (!MatchesScope(typeName, scope))
                        continue;

                    var largeClass = CheckLargeClass(typeDecl, filePath, scope, maxClassLines);
                    if (largeClass is not null)
                        results.Add(largeClass);

                    results.AddRange(CheckLongMethods(typeDecl, filePath, scope, maxMethodLines));

                    var tooManyDeps = CheckTooManyDependencies(typeDecl, filePath, scope, maxParams);
                    if (tooManyDeps is not null)
                        results.Add(tooManyDeps);
                }
            }
        }

        return results;
    }

    private static SmellResult? CheckLargeClass(
        TypeDeclarationSyntax typeDecl,
        string? filePath,
        string? scope,
        int maxClassLines)
    {
        var typeName = typeDecl.Identifier.Text;
        var lineSpan = typeDecl.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line;
        var lineCount = lineSpan.EndLinePosition.Line - startLine + 1;

        if (lineCount <= maxClassLines)
            return null;

        return new SmellResult(
            SmellType: "LargeClass",
            TypeName: typeName,
            MemberName: null,
            FilePath: filePath,
            Line: startLine + 1,
            Detail: $"({lineCount} lines)");
    }

    private static IEnumerable<SmellResult> CheckLongMethods(
        TypeDeclarationSyntax typeDecl,
        string? filePath,
        string? scope,
        int maxMethodLines)
    {
        var typeName = typeDecl.Identifier.Text;

        foreach (var method in typeDecl.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            var lineSpan = method.GetLocation().GetLineSpan();
            var startLine = lineSpan.StartLinePosition.Line;
            var lineCount = lineSpan.EndLinePosition.Line - startLine + 1;

            if (lineCount > maxMethodLines)
            {
                yield return new SmellResult(
                    SmellType: "LongMethod",
                    TypeName: typeName,
                    MemberName: method.Identifier.Text,
                    FilePath: filePath,
                    Line: startLine + 1,
                    Detail: $"({lineCount} lines)");
            }
        }
    }

    private static SmellResult? CheckTooManyDependencies(
        TypeDeclarationSyntax typeDecl,
        string? filePath,
        string? scope,
        int maxDeps)
    {
        var typeName = typeDecl.Identifier.Text;

        var ctor = typeDecl.DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();

        if (ctor is null)
            return null;

        var paramCount = ctor.ParameterList.Parameters.Count;
        if (paramCount <= maxDeps)
            return null;

        var lineSpan = ctor.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line;

        return new SmellResult(
            SmellType: "TooManyDependencies",
            TypeName: typeName,
            MemberName: null,
            FilePath: filePath,
            Line: startLine + 1,
            Detail: $"ctor ({paramCount} params)");
    }

    private static bool MatchesScope(string typeName, string? scope)
    {
        if (scope is null)
            return true;

        return typeName.StartsWith(scope, StringComparison.OrdinalIgnoreCase);
    }
}
