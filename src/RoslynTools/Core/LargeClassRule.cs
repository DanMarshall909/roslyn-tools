using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// A class that enforces rules on large classes in the code.
/// </summary>
internal static class LargeClassRule
{
    /// <summary>
    /// A method that checks if a class declaration meets the large class rule criteria.
    /// </summary>
    internal static SmellResult? Check(TypeDeclarationSyntax typeDecl, string? filePath, int maxClassLines)
    {
        var typeName = typeDecl.Identifier.Text;
        var lineSpan = typeDecl.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line;
        var lineCount = lineSpan.EndLinePosition.Line - startLine + 1;

        if (lineCount <= maxClassLines)
            return null;

        return new SmellResult(
            "LargeClass",
            typeName,
            null,
            filePath,
            startLine + 1,
            $"({lineCount} lines)");
    }
}
