using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Services;

/// <summary>
///     Recognizes only ordinary constructors whose conversion to a primary constructor has a
///     small, mechanically comparable shape. This intentionally favors false negatives: it
///     reports review candidates, not proof that a rewrite is behavior-preserving.
/// </summary>
internal static class PrimaryConstructorOpportunityDetector
{
    /// <summary>Cheap syntax gate used before requesting a semantic model.</summary>
    internal static bool HasPotentialShape(ClassDeclarationSyntax declaration) =>
        TryGetPotentialConstructor(declaration, out _);

    /// <summary>
    ///     Proves the remaining symbol-sensitive constraints and returns the constructor whose
    ///     location should be reported as a review candidate.
    /// </summary>
    internal static bool TryGetCandidate(
        ClassDeclarationSyntax declaration,
        SemanticModel semanticModel,
        CancellationToken cancellationToken,
        out ConstructorDeclarationSyntax constructor)
    {
        if (!TryGetPotentialConstructor(declaration, out constructor))
            return false;

        var classSymbol = semanticModel.GetDeclaredSymbol(declaration, cancellationToken);
        if (classSymbol is null
            || classSymbol.DeclaringSyntaxReferences.Length != 1
            || classSymbol.BaseType?.SpecialType != SpecialType.System_Object)
        {
            return false;
        }

        var parameters = constructor.ParameterList.Parameters;
        if (HasPrimaryParameterNameConflict(declaration, constructor, classSymbol, parameters))
            return false;

        var parameterSymbols = parameters
            .Select(parameter => semanticModel.GetDeclaredSymbol(parameter, cancellationToken))
            .OfType<IParameterSymbol>()
            .ToHashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        if (parameterSymbols.Count != parameters.Count)
            return false;

        var assignedParameters = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        var assignedFields = new HashSet<IFieldSymbol>(SymbolEqualityComparer.Default);
        foreach (var statement in constructor.Body!.Statements)
        {
            var assignment = (AssignmentExpressionSyntax)((ExpressionStatementSyntax)statement).Expression;
            var parameterReference = (IdentifierNameSyntax)assignment.Right;
            var parameter = semanticModel.GetSymbolInfo(parameterReference, cancellationToken).Symbol
                as IParameterSymbol;
            var field = semanticModel.GetSymbolInfo(assignment.Left, cancellationToken).Symbol
                as IFieldSymbol;
            if (parameter is null
                || field is null
                || !parameterSymbols.Contains(parameter)
                || !field.IsReadOnly
                || field.IsStatic
                || !SymbolEqualityComparer.Default.Equals(field.ContainingType, classSymbol)
                || !assignedParameters.Add(parameter)
                || !assignedFields.Add(field))
            {
                return false;
            }
        }

        return assignedParameters.SetEquals(parameterSymbols);
    }

    private static bool TryGetPotentialConstructor(
        ClassDeclarationSyntax declaration,
        out ConstructorDeclarationSyntax constructor)
    {
        constructor = null!;
        if (declaration.ParameterList is not null
            || declaration.Modifiers.Any(SyntaxKind.AbstractKeyword)
            || declaration.Modifiers.Any(SyntaxKind.StaticKeyword)
            || declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            return false;
        }

        var constructors = declaration.Members.OfType<ConstructorDeclarationSyntax>()
            .Where(candidate => !candidate.Modifiers.Any(SyntaxKind.StaticKeyword))
            .ToList();
        if (constructors.Count != 1)
            return false;

        constructor = constructors[0];
        if (constructor.Modifiers.Count != 1
            || !constructor.Modifiers[0].IsKind(SyntaxKind.PublicKeyword)
            || constructor.ParameterList.Parameters.Count == 0
            || constructor.Initializer is not null
            || constructor.AttributeLists.Count != 0
            || constructor.ExpressionBody is not null
            || constructor.Body is null
            || HasUnmovableConstructorTrivia(constructor))
        {
            return false;
        }

        var parameters = constructor.ParameterList.Parameters;
        if (parameters.Any(parameter =>
                parameter.AttributeLists.Count != 0
                || parameter.Modifiers.Count != 0
                || parameter.Default is not null))
        {
            return false;
        }

        return !HasRequiredMembers(declaration)
               && !HasInstanceInitializers(declaration)
               && constructor.Body.Statements.Count == parameters.Count
               && constructor.Body.Statements.All(IsDirectParameterAssignmentShape);
    }

    private static bool IsDirectParameterAssignmentShape(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax
        {
            Expression: AssignmentExpressionSyntax
            {
                RawKind: (int)SyntaxKind.SimpleAssignmentExpression,
                Right: IdentifierNameSyntax
            }
        };

    private static bool HasUnmovableConstructorTrivia(ConstructorDeclarationSyntax constructor) =>
        constructor.GetLeadingTrivia()
            .Concat(constructor.DescendantTrivia(descendIntoTrivia: true))
            .Any(trivia =>
                trivia.IsDirective
                || trivia.GetStructure() is DocumentationCommentTriviaSyntax);

    private static bool HasRequiredMembers(ClassDeclarationSyntax declaration) =>
        declaration.Members.Any(member => member switch
        {
            FieldDeclarationSyntax field => field.Modifiers.Any(SyntaxKind.RequiredKeyword),
            PropertyDeclarationSyntax property => property.Modifiers.Any(SyntaxKind.RequiredKeyword),
            _ => false
        });

    private static bool HasInstanceInitializers(ClassDeclarationSyntax declaration) =>
        declaration.Members.Any(member => member switch
        {
            FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.StaticKeyword) =>
                field.Declaration.Variables.Any(variable => variable.Initializer is not null),
            EventFieldDeclarationSyntax eventField when !eventField.Modifiers.Any(SyntaxKind.StaticKeyword) =>
                eventField.Declaration.Variables.Any(variable => variable.Initializer is not null),
            PropertyDeclarationSyntax property when !property.Modifiers.Any(SyntaxKind.StaticKeyword) =>
                property.Initializer is not null,
            _ => false
        });

    private static bool HasPrimaryParameterNameConflict(
        ClassDeclarationSyntax declaration,
        ConstructorDeclarationSyntax constructor,
        INamedTypeSymbol classSymbol,
        SeparatedSyntaxList<ParameterSyntax> parameters)
    {
        var parameterNames = parameters.Select(parameter => parameter.Identifier.ValueText)
            .ToHashSet(StringComparer.Ordinal);
        if (classSymbol.TypeParameters.Any(parameter => parameterNames.Contains(parameter.Name)))
            return true;

        for (var type = classSymbol; type is not null; type = type.BaseType)
        {
            if (type.GetMembers().Any(member => parameterNames.Contains(member.Name)))
                return true;
        }

        return declaration.DescendantTokens()
            .Where(token => !constructor.FullSpan.Contains(token.Span))
            .Any(token =>
                token.IsKind(SyntaxKind.IdentifierToken)
                && parameterNames.Contains(token.ValueText));
    }
}
