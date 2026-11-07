using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynTools.Core;

/// <summary>Exact existing-field identity and a deliberately local constructor availability proof.</summary>
internal static class StaticDependencyReceiverReuse
{
    internal static (IFieldSymbol? Field, string? Reason) Resolve(
        Compilation compilation, INamedTypeSymbol selectedType, INamedTypeSymbol? receiverType, string fieldId)
    {
        var fields = DocumentationCommentId.GetSymbolsForDeclarationId(fieldId, compilation)
            .OfType<IFieldSymbol>().Where(candidate => candidate.GetDocumentationCommentId() == fieldId).ToArray();
        if (fields.Length != 1)
            return (null, $"Existing receiver field '{fieldId}' must resolve to exactly one field; properties and unknown identities cannot supply a receiver.");
        var field = fields[0];
        if (!SymbolEqualityComparer.Default.Equals(field.ContainingType, selectedType)
            || !SymbolEqualityComparer.Default.Equals(field.ContainingAssembly, compilation.Assembly))
            return (null, $"Existing receiver field '{fieldId}' must be declared by the selected source type; inherited and other-owner fields are unsupported.");
        if (field.IsStatic)
            return (null, $"Existing receiver field '{fieldId}' must be an instance field.");
        if (receiverType is null || !SymbolEqualityComparer.Default.Equals(field.Type, receiverType))
            return (null, $"Existing receiver field '{fieldId}' must have exactly the declared receiver type; implicit convertibility is insufficient.");
        return (field, null);
    }

    internal static string? ConstructorUnavailableReason(
        StaticDependencyReference entry, IFieldSymbol field, SemanticModel model, CancellationToken cancellationToken)
    {
        if (entry.Reference.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault()
            is not ConstructorDeclarationSyntax constructor)
            return null; // Method-only reuse changes no constructor API and relies on the declared receiver preconditions.

        var constructorSymbol = model.GetDeclaredSymbol(constructor, cancellationToken);
        var first = constructor.Body?.Statements.FirstOrDefault();
        if (constructor.Initializer is not null || constructorSymbol is not { IsStatic: false, IsVararg: false }
            || first is null || model.GetOperation(first, cancellationToken) is not IExpressionStatementOperation
            {
                Operation: ISimpleAssignmentOperation
                {
                    IsRef: false,
                    Target: IFieldReferenceOperation
                    {
                        Instance: IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance }
                    } stored,
                    Value: IParameterReferenceOperation supplied
                } assignment
            }
            || !SymbolEqualityComparer.Default.Equals(stored.Field, field)
            || supplied.Parameter.RefKind != RefKind.None || supplied.Parameter.IsOptional || supplied.Parameter.IsParams
            || !SymbolEqualityComparer.Default.Equals(supplied.Parameter.Type, field.Type)
            || !SymbolEqualityComparer.Default.Equals(supplied.Parameter.ContainingSymbol, constructorSymbol))
            return $"Existing receiver field '{field.GetDocumentationCommentId()}' requires an ordinary unchained block constructor whose first statement assigns that exact field from an ordinary parameter of exactly its type.";

        // Shared selected-site admission puts the call in this unchained constructor body.
        // The first statement above contains only the exact field and parameter references,
        // so every admitted invocation necessarily follows it.
        // This is a syntactic/semantic witness, not general dataflow or interprocedural lifetime analysis.
        // Ref/out and ref aliases are also competing write authority, even without a second assignment expression.
        var body = model.GetOperation(constructor.Body!, cancellationToken)!;
        foreach (var operation in body.DescendantsAndSelf())
        {
            IOperation? writeTarget = operation switch
            {
                IAssignmentOperation write when write.Syntax.Span != assignment.Syntax.Span => write.Target,
                IIncrementOrDecrementOperation write => write.Target,
                IArgumentOperation { Parameter.RefKind: RefKind.Ref or RefKind.Out } argument => argument.Value,
                _ => null
            };
            if (writeTarget is not null && ReferencesField(writeTarget, field)
                || operation is IFieldReferenceOperation reference
                && SymbolEqualityComparer.Default.Equals(reference.Field, field)
                && reference.Syntax.Ancestors().Any(node => node is RefExpressionSyntax))
                return $"Selected constructor has a competing write or reference escape for existing receiver field '{field.GetDocumentationCommentId()}'; only the first exact assignment is supported.";
        }
        return null;
    }

    private static bool ReferencesField(IOperation operation, IFieldSymbol field) => operation.DescendantsAndSelf()
        .OfType<IFieldReferenceOperation>().Any(reference => SymbolEqualityComparer.Default.Equals(reference.Field, field));
}
