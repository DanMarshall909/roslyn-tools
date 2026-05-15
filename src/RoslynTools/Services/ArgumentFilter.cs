using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

internal static class ArgumentFilter
{
    internal static bool MatchesStringLiteral(ArgumentListSyntax? argumentList, int argIndex, string literal)
    {
        if (argumentList is null)
            return false;

        if (argIndex < 0 || argIndex >= argumentList.Arguments.Count)
            return false;

        var arg = argumentList.Arguments[argIndex];
        if (arg.Expression is not LiteralExpressionSyntax lit)
            return false;

        if (lit.Kind() != SyntaxKind.StringLiteralExpression)
            return false;

        return lit.Token.ValueText == literal;
    }
}
