using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

internal static class ParameterChangeHelper
{
    /// <summary>
    /// Returns the <see cref="ParameterListSyntax"/> for a method, constructor, or local
    /// function declaration, or <c>null</c> for any other syntax node kind.
    /// </summary>
    internal static ParameterListSyntax? GetParameterList(SyntaxNode syntaxNode) =>
        syntaxNode switch
        {
            MethodDeclarationSyntax m => m.ParameterList,
            ConstructorDeclarationSyntax c => c.ParameterList,
            LocalFunctionStatementSyntax lf => lf.ParameterList,
            _ => null
        };
}
