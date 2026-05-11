using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
///     Builds the new extracted method declaration and the call-site statement
///     for the extract-method refactoring.
/// </summary>
internal static class MethodExtractorBuilder
{
    /// <summary>
    /// A method that builds a new method from symbol and statement lists.
    /// </summary>
    internal static MethodDeclarationSyntax BuildNewMethod(
        string name,
        IReadOnlyList<ISymbol> parameters,
        IReadOnlyList<ISymbol> refParams,
        IReadOnlyList<ISymbol> outParams,
        List<ISymbol> returnVars,
        List<StatementSyntax> bodyStatements,
        SemanticModel model,
        bool isStatic)
    {
        var modifiers = $"private{(isStatic ? " static" : "")}";
        var paramParts = parameters.Select(p => $"{GetTypeName(p)} {p.Name}")
            .Concat(refParams.Select(p => $"ref {GetTypeName(p)} {p.Name}"))
            .Concat(outParams.Select(p => $"out {GetTypeName(p)} {p.Name}"));
        var paramStr = string.Join(", ", paramParts);

        string returnTypeStr;
        string returnStmtStr;

        if (returnVars.Count == 0)
        {
            returnTypeStr = "void";
            returnStmtStr = "";
        }
        else if (returnVars.Count == 1)
        {
            returnTypeStr = GetTypeName(returnVars[0]);
            // Stryker disable all
            returnStmtStr = "\n" + IndentOf(bodyStatements[^1]) + $"return {returnVars[0].Name};";
            // Stryker restore all
        }
        else
        {
            // Stryker disable all
            // For tuple returns, all elements are always initialized before return, so remove nullable annotations
            var tupleTypes = string.Join(", ",
                returnVars.Select(v => $"{RemoveNullableAnnotation(GetTypeName(v))} {v.Name}"));
            returnTypeStr = $"({tupleTypes})";
            var tupleValues = string.Join(", ", returnVars.Select(v => v.Name));
            returnStmtStr = "\n" + IndentOf(bodyStatements[^1]) + $"return ({tupleValues});";
            // Stryker restore all
        }

        var bodyText = string.Join("", bodyStatements.Select(s => s.ToFullString()));

        var memberIndent = MemberIndentOf(bodyStatements[0]);
        var methodText =
            $"{modifiers} {returnTypeStr} {name}({paramStr})\n{memberIndent}{{\n{bodyText}{returnStmtStr}\n{memberIndent}}}";

        // Stryker disable all
        var method = (MethodDeclarationSyntax)(SyntaxFactory.ParseMemberDeclaration(methodText)
                                               ?? throw new InvalidOperationException(
                                                   "Failed to parse the generated method."));
        // Stryker restore all

        return method.WithLeadingTrivia(
            SyntaxFactory.LineFeed,
            SyntaxFactory.Whitespace(memberIndent));
    }

    /// <summary>
    /// A method that builds a call site from symbol and statement lists.
    /// </summary>
    internal static StatementSyntax BuildCallSite(
        string methodName,
        IReadOnlyList<ISymbol> parameters,
        IReadOnlyList<ISymbol> refParams,
        IReadOnlyList<ISymbol> outParams,
        List<ISymbol> returnVars,
        StatementSyntax firstExtracted)
    {
        var argParts = parameters.Select(p => p.Name)
            .Concat(refParams.Select(p => $"ref {p.Name}"))
            .Concat(outParams.Select(p => $"out {p.Name}"));
        var argStr = string.Join(", ", argParts);

        string stmtText;
        if (returnVars.Count == 0)
        {
            stmtText = $"{methodName}({argStr});";
        }
        else if (returnVars.Count == 1)
        {
            stmtText = $"var {returnVars[0].Name} = {methodName}({argStr});";
        }
        else
        {
            var varNames = string.Join(", ", returnVars.Select(v => v.Name));
            stmtText = $"var ({varNames}) = {methodName}({argStr});";
        }

        return SyntaxFactory.ParseStatement(stmtText)
            .WithLeadingTrivia(firstExtracted.GetLeadingTrivia())
            .WithTrailingTrivia(SyntaxFactory.LineFeed);
    }

    /// <summary>
    /// A method that gets the type name from a symbol.
    /// </summary>
    internal static string GetTypeName(ISymbol symbol)
    {
        var type = symbol switch
        {
            ILocalSymbol loc => loc.Type,
            IParameterSymbol param => param.Type,
            // Stryker disable all
            _ => throw new InvalidOperationException(
                $"Unexpected symbol kind when resolving type: {symbol.Kind} ({symbol.Name})")
            // Stryker restore all
        };
        return type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
    }

    /// <summary>Removes trailing '?' nullable annotation from a type name string.</summary>
    internal static string RemoveNullableAnnotation(string typeName)
    {
        return typeName.EndsWith('?') ? typeName[..^1] : typeName;
    }

    /// <summary>Returns the leading whitespace string of a statement (body-level indent).</summary>
    internal static string IndentOf(StatementSyntax stmt)
    {
        var ws = stmt.GetLeadingTrivia()
            .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        return ws == default ? "        " : ws.ToString();
    }

    /// <summary>Returns the indent string one level up from body indent (member/class-body level).</summary>
    internal static string MemberIndentOf(StatementSyntax stmt)
    {
        var bodyIndent = IndentOf(stmt);
        return bodyIndent.Length >= 4
            ? bodyIndent[..^4]
            : bodyIndent;
    }
}
