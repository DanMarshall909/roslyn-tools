using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal sealed class ParameterChangeHelper
{
    /// <summary>
    /// Returns the <see cref="ParameterListSyntax"/> for a method, constructor, or local
    /// function declaration, or <c>null</c> for any other syntax node kind.
    /// </summary>
    internal ParameterListSyntax? GetParameterList(SyntaxNode syntaxNode) =>
        syntaxNode switch
        {
            MethodDeclarationSyntax m => m.ParameterList,
            ConstructorDeclarationSyntax c => c.ParameterList,
            LocalFunctionStatementSyntax lf => lf.ParameterList,
            _ => null
        };
}
