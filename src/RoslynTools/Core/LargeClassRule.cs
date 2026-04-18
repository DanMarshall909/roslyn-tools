using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class LargeClassRule
{
    internal static SmellResult? Check(TypeDeclarationSyntax typeDecl, string? filePath, int maxClassLines)
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
}
