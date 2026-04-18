using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class LongMethodRule
{
    internal static IEnumerable<SmellResult> Check(TypeDeclarationSyntax typeDecl, string? filePath, int maxMethodLines)
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
}
