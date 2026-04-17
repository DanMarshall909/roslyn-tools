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

                // Check classes and records for size
                var typeDeclarations = root.DescendantNodes()
                    .OfType<TypeDeclarationSyntax>()
                    .Where(t => t is ClassDeclarationSyntax or RecordDeclarationSyntax);

                foreach (var typeDecl in typeDeclarations)
                {
                    var typeName = typeDecl.Identifier.Text;

                    if (!MatchesScope(typeName, scope))
                        continue;

                    var lineSpan = typeDecl.GetLocation().GetLineSpan();
                    var startLine = lineSpan.StartLinePosition.Line;
                    var endLine = lineSpan.EndLinePosition.Line;
                    var lineCount = endLine - startLine + 1;

                    if (lineCount > maxClassLines)
                    {
                        results.Add(new SmellResult(
                            SmellType: "LargeClass",
                            TypeName: typeName,
                            MemberName: null,
                            FilePath: filePath,
                            Line: startLine + 1,
                            Detail: $"({lineCount} lines)"));
                    }
                }

                // Check methods for length and constructors for too many params
                var methodDeclarations = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>();

                foreach (var method in methodDeclarations)
                {
                    var containingType = method.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault();

                    var typeName = containingType?.Identifier.Text ?? "<unknown>";

                    if (!MatchesScope(typeName, scope))
                        continue;

                    var lineSpan = method.GetLocation().GetLineSpan();
                    var lineCount = lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1;
                    var startLine = lineSpan.StartLinePosition.Line;

                    if (lineCount > maxMethodLines)
                    {
                        results.Add(new SmellResult(
                            SmellType: "LongMethod",
                            TypeName: typeName,
                            MemberName: method.Identifier.Text,
                            FilePath: filePath,
                            Line: startLine + 1,
                            Detail: $"({lineCount} lines)"));
                    }
                }

                var constructorDeclarations = root.DescendantNodes()
                    .OfType<ConstructorDeclarationSyntax>();

                foreach (var ctor in constructorDeclarations)
                {
                    var paramCount = ctor.ParameterList.Parameters.Count;

                    if (paramCount <= maxParams)
                        continue;

                    var containingType = ctor.Ancestors()
                        .OfType<TypeDeclarationSyntax>()
                        .FirstOrDefault();

                    var typeName = containingType?.Identifier.Text ?? ctor.Identifier.Text;

                    if (!MatchesScope(typeName, scope))
                        continue;

                    var lineSpan = ctor.GetLocation().GetLineSpan();
                    var startLine = lineSpan.StartLinePosition.Line;

                    results.Add(new SmellResult(
                        SmellType: "TooManyDependencies",
                        TypeName: typeName,
                        MemberName: null,
                        FilePath: filePath,
                        Line: startLine + 1,
                        Detail: $"ctor ({paramCount} params)"));
                }
            }
        }

        return results;
    }

    private static bool MatchesScope(string typeName, string? scope)
    {
        if (scope is null)
            return true;

        return typeName.StartsWith(scope, StringComparison.OrdinalIgnoreCase);
    }
}
