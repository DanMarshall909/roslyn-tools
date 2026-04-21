using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal enum ReachableInstanceKind
{
    None,
    Match,
    Ambiguous
}

internal readonly record struct ReachableInstanceResult(ReachableInstanceKind Kind, ExpressionSyntax? Expression)
{
    internal static ReachableInstanceResult None { get; } = new(ReachableInstanceKind.None, null);
    internal static ReachableInstanceResult Ambiguous { get; } = new(ReachableInstanceKind.Ambiguous, null);

    internal static ReachableInstanceResult FromExpression(ExpressionSyntax expression) =>
        new(ReachableInstanceKind.Match, expression);
}

/// <summary>
/// Determines which instance of a given type is reachable at a call site,
/// used by make-instance to find a suitable receiver expression.
/// </summary>
internal static class InstanceReachabilityAnalyzer
{
    /// <summary>
    /// Finds a reachable instance of <paramref name="targetType"/> at <paramref name="callSite"/>.
    /// Priority: local variables → parameters → this (same-class instance context) → fields/properties.
    /// Returns an explicit result so callers can distinguish no-match from ambiguity.
    /// </summary>
    internal static ReachableInstanceResult FindReachableInstance(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        var enclosingMember = callSite.Ancestors()
            .FirstOrDefault(n => n is MethodDeclarationSyntax
                              || n is ConstructorDeclarationSyntax
                              || n is PropertyDeclarationSyntax
                              || n is AccessorDeclarationSyntax);

        if (enclosingMember is null)
            return ReachableInstanceResult.None;

        // 1. Local variables of the target type declared before this call site
        var localVar = FindLocalVariableCandidate(callSite, semanticModel, targetType);
        if (localVar.Kind == CandidateKind.Unsupported)
            return ReachableInstanceResult.None;
        if (localVar.Kind == CandidateKind.Ambiguous)
            return ReachableInstanceResult.Ambiguous;
        if (localVar.Name is not null)
            return ReachableInstanceResult.FromExpression(SyntaxFactory.IdentifierName(localVar.Name));

        // 2. Parameters of the target type in the enclosing member
        var param = FindParameterCandidate(enclosingMember, semanticModel, targetType);
        if (param.Kind == CandidateKind.Ambiguous)
            return ReachableInstanceResult.Ambiguous;
        if (param.Name is not null)
            return ReachableInstanceResult.FromExpression(SyntaxFactory.IdentifierName(param.Name));

        // 3. Same-class call site: `this` when in an instance context
        var enclosingType = GetEnclosingTypeSymbol(enclosingMember, semanticModel);
        bool isInstance = IsInstanceContext(enclosingMember);

        if (enclosingType is not null && isInstance &&
            SymbolEqualityComparer.Default.Equals(enclosingType, targetType))
        {
            return ReachableInstanceResult.FromExpression(SyntaxFactory.ThisExpression());
        }

        // 4. Instance field or property on the enclosing class that holds the target type
        if (enclosingType is not null && isInstance)
        {
            var member = FindFieldOrPropertyCandidate(enclosingType, targetType);
            if (member.Kind == CandidateKind.Ambiguous)
                return ReachableInstanceResult.Ambiguous;
            if (member.Name is not null)
                return ReachableInstanceResult.FromExpression(SyntaxFactory.IdentifierName(member.Name));
        }

        return ReachableInstanceResult.None;
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
            PropertyDeclarationSyntax p =>
                !p.Modifiers.Any(t => t.IsKind(SyntaxKind.StaticKeyword)),
            AccessorDeclarationSyntax a =>
                !GetAccessorOwnerModifiers(a).Any(t => t.IsKind(SyntaxKind.StaticKeyword)),
            _ => true
        };

    internal static string? FindLocalVariableName(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType) =>
        FindLocalVariableCandidate(callSite, semanticModel, targetType).Name;

    internal static string? FindParameterName(
        SyntaxNode enclosingMember,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType) =>
        FindParameterCandidate(enclosingMember, semanticModel, targetType).Name;

    internal static string? FindFieldOrPropertyName(INamedTypeSymbol enclosingType, INamedTypeSymbol targetType) =>
        FindFieldOrPropertyCandidate(enclosingType, targetType).Name;

    private static CandidateNameResult FindLocalVariableCandidate(
        InvocationExpressionSyntax callSite,
        SemanticModel semanticModel,
        INamedTypeSymbol targetType)
    {
        var block = callSite.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        if (block is null)
            return CandidateNameResult.None;

        var matches = new List<string>();

        foreach (var localSymbol in semanticModel.LookupSymbols(callSite.SpanStart)
            .OfType<ILocalSymbol>())
        {
            if (!SymbolEqualityComparer.Default.Equals(localSymbol.Type, targetType))
                continue;

            var declarationInfo = GetLocalDeclarationInfo(localSymbol);
            if (declarationInfo.Safety == LocalDeclarationSafety.Unsupported)
                return CandidateNameResult.Unsupported;

            if (declarationInfo.DeclarationStart < callSite.SpanStart)
                matches.Add(localSymbol.Name);
        }

        return ToCandidateResult(matches);
    }

    private static CandidateNameResult FindParameterCandidate(
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

        if (paramList is null)
            return CandidateNameResult.None;

        var matches = new List<string>();

        foreach (var param in paramList.Parameters)
        {
            var paramSymbol = semanticModel.GetDeclaredSymbol(param) as IParameterSymbol;
            if (paramSymbol is null)
                continue;
            if (SymbolEqualityComparer.Default.Equals(paramSymbol.Type, targetType))
                matches.Add(param.Identifier.Text);
        }

        return ToCandidateResult(matches);
    }

    private static CandidateNameResult FindFieldOrPropertyCandidate(INamedTypeSymbol enclosingType, INamedTypeSymbol targetType)
    {
        var matches = new List<string>();

        foreach (var member in enclosingType.GetMembers())
        {
            if (member.IsStatic) continue;

            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol f when !f.IsImplicitlyDeclared => f.Type,
                IPropertySymbol p => p.Type,
                _ => null
            };

            if (memberType is null)
                continue;
            if (SymbolEqualityComparer.Default.Equals(memberType, targetType))
                matches.Add(member.Name);
        }

        return ToCandidateResult(matches);
    }

    private static CandidateNameResult ToCandidateResult(IEnumerable<string> matches)
    {
        var distinctMatches = matches.Distinct(StringComparer.Ordinal).ToArray();
        return distinctMatches.Length switch
        {
            0 => CandidateNameResult.None,
            1 => CandidateNameResult.Match(distinctMatches[0]),
            _ => CandidateNameResult.Ambiguous
        };
    }

    private static LocalDeclarationInfo GetLocalDeclarationInfo(ILocalSymbol localSymbol)
    {
        foreach (var syntaxReference in localSymbol.DeclaringSyntaxReferences)
        {
            var syntax = syntaxReference.GetSyntax();

            if (syntax is VariableDeclaratorSyntax variableDeclarator)
            {
                var localDeclaration = variableDeclarator.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
                if (localDeclaration is not null)
                    return new LocalDeclarationInfo(LocalDeclarationSafety.Supported, localDeclaration.SpanStart);
            }

            if (syntax is SingleVariableDesignationSyntax designation &&
                designation.Ancestors().OfType<DeclarationExpressionSyntax>().Any())
            {
                var localDeclaration = designation.Ancestors().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault();
                if (localDeclaration is not null)
                    return new LocalDeclarationInfo(LocalDeclarationSafety.Supported, localDeclaration.SpanStart);

                var assignmentStatement = designation.Ancestors()
                    .OfType<ExpressionStatementSyntax>()
                    .FirstOrDefault(statement => statement.Expression is AssignmentExpressionSyntax assignment &&
                        assignment.IsKind(SyntaxKind.SimpleAssignmentExpression));

                if (assignmentStatement is not null)
                    return new LocalDeclarationInfo(LocalDeclarationSafety.Supported, assignmentStatement.SpanStart);
            }

            var foreachStatement = syntax.AncestorsAndSelf().OfType<ForEachStatementSyntax>().FirstOrDefault();
            if (foreachStatement is not null)
                return new LocalDeclarationInfo(LocalDeclarationSafety.Supported, foreachStatement.SpanStart);
        }

        return new LocalDeclarationInfo(LocalDeclarationSafety.Unsupported, int.MaxValue);
    }

    private static SyntaxTokenList GetAccessorOwnerModifiers(AccessorDeclarationSyntax accessor)
    {
        var property = accessor.Parent?.Parent as BasePropertyDeclarationSyntax;
        return property?.Modifiers ?? default;
    }

    private enum CandidateKind
    {
        None,
        Match,
        Ambiguous,
        Unsupported
    }

    private enum LocalDeclarationSafety
    {
        Supported,
        Unsupported
    }

    private readonly record struct LocalDeclarationInfo(LocalDeclarationSafety Safety, int DeclarationStart);

    private readonly record struct CandidateNameResult(CandidateKind Kind, string? Name)
    {
        internal static CandidateNameResult None { get; } = new(CandidateKind.None, null);
        internal static CandidateNameResult Ambiguous { get; } = new(CandidateKind.Ambiguous, null);
        internal static CandidateNameResult Unsupported { get; } = new(CandidateKind.Unsupported, null);

        internal static CandidateNameResult Match(string name) => new(CandidateKind.Match, name);
    }
}
