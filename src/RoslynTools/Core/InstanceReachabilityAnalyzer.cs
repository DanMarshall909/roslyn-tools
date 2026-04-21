using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// Determines which instance of a given type is reachable at a call site,
/// used by make-instance to find a suitable receiver expression.
/// </summary>
internal static class InstanceReachabilityAnalyzer
{
    /// <summary>
    /// Finds a reachable instance of <paramref name="targetType"/> at <paramref name="callSite"/>.
    /// Priority: local variables → parameters → this (same-class instance context) → fields/properties.
    /// Returns null when conservatively no instance can be determined.
    /// </summary>
    internal static ExpressionSyntax? FindReachableInstance(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        var enclosingMember = callSite.Ancestors()
            .FirstOrDefault(n => n is MethodDeclarationSyntax
                              || n is ConstructorDeclarationSyntax
                              || n is PropertyDeclarationSyntax
                              || n is AccessorDeclarationSyntax);

        if (enclosingMember is null) return null;

        // 1. Local variables of the target type declared before this call site
        var localVar = FindLocalVariableName(callSite, semanticModel, targetType);
        if (localVar is not null)
            return SyntaxFactory.IdentifierName(localVar);

        // 2. Parameters of the target type in the enclosing member
        var param = FindParameterName(enclosingMember, semanticModel, targetType);
        if (param is not null)
            return SyntaxFactory.IdentifierName(param);

        // 3. Same-class call site: `this` when in an instance context
        var enclosingType = GetEnclosingTypeSymbol(enclosingMember, semanticModel);
        bool isInstance = IsInstanceContext(enclosingMember);

        if (enclosingType is not null && isInstance &&
            SymbolEqualityComparer.Default.Equals(enclosingType, targetType))
        {
            return SyntaxFactory.ThisExpression();
        }

        // 4. Instance field or property on the enclosing class that holds the target type
        if (enclosingType is not null && isInstance)
        {
            var fieldName = FindFieldOrPropertyName(enclosingType, targetType);
            if (fieldName is not null)
                return SyntaxFactory.IdentifierName(fieldName);
        }

        return null;
    }

    internal static INamedTypeSymbol? GetEnclosingTypeSymbol(SyntaxNode memberNode, SemanticModel semanticModel)
    {
        var typeDecl = memberNode.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (typeDecl is null) return null;
        return semanticModel.GetDeclaredSymbol(typeDecl) as INamedTypeSymbol;
    }

    internal static bool IsInstanceContext(SyntaxNode memberNode) =>
        memberNode switch
        {
            MethodDeclarationSyntax m =>
                !m.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)),
            ConstructorDeclarationSyntax c =>
                !c.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)),
            _ => true
        };

    internal static string? FindLocalVariableName(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        var block = callSite.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (block is null) return null;

        // Stryker disable all
        foreach (var decl in block.Statements
            .OfType<LocalDeclarationStatementSyntax>()
            .Where(d => d.SpanStart < callSite.SpanStart))
        {
            // Stryker restore all
            foreach (var variable in decl.Declaration.Variables)
            {
                var localSymbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                // Stryker disable all
                if (localSymbol is null) continue;
                // Stryker restore all
                if (SymbolEqualityComparer.Default.Equals(localSymbol.Type, targetType))
                    return variable.Identifier.Text;
            }
        }

        return null;
    }

    internal static string? FindParameterName(
        SyntaxNode enclosingMember,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        ParameterListSyntax? paramList = enclosingMember switch
        {
            MethodDeclarationSyntax m => m.ParameterList,
            ConstructorDeclarationSyntax c => c.ParameterList,
            _ => null
        };

        if (paramList is null) return null;

        foreach (var param in paramList.Parameters)
        {
            var paramSymbol = semanticModel.GetDeclaredSymbol(param) as IParameterSymbol;
            // Stryker disable all
            if (paramSymbol is null) continue;
            // Stryker restore all
            if (SymbolEqualityComparer.Default.Equals(paramSymbol.Type, targetType))
                return param.Identifier.Text;
        }

        return null;
    }

    internal static string? FindFieldOrPropertyName(INamedTypeSymbol enclosingType, INamedTypeSymbol targetType)
    {
        foreach (var member in enclosingType.GetMembers())
        {
            if (member.IsStatic) continue;

            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol f when !f.IsImplicitlyDeclared => f.Type,
                IPropertySymbol p => p.Type,
                _ => null
            };

            // Stryker disable all
            if (memberType is null) continue;
            // Stryker restore all
            if (SymbolEqualityComparer.Default.Equals(memberType, targetType))
                return member.Name;
        }

        return null;
    }
}
