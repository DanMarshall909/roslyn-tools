using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal static class TooManyDependenciesRule
{
    internal static SmellResult? Check(TypeDeclarationSyntax typeDecl, string? filePath, int maxParams)
    {
        var typeName = typeDecl.Identifier.Text;

        var ctor = typeDecl.DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .FirstOrDefault();

        if (ctor is null)
            return null;

        var paramCount = ctor.ParameterList.Parameters.Count;
        if (paramCount <= maxParams)
            return null;

        var lineSpan = ctor.GetLocation().GetLineSpan();
        var startLine = lineSpan.StartLinePosition.Line;

        return new SmellResult(
            "TooManyDependencies",
            typeName,
            null,
            filePath,
            startLine + 1,
            $"ctor ({paramCount} params)");
    }
}
