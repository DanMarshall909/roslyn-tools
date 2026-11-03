using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynTools.Core;

internal static class StaticDependencyMappingSemantics
{
    internal static IMethodSymbol? ResolveMethod(Compilation compilation, StaticDependencySymbol identity)
    {
        var matches = DocumentationCommentId.GetSymbolsForDeclarationId(identity.DocumentationId, compilation)
            .OfType<IMethodSymbol>().Where(method => Matches(method, identity)).ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    internal static bool Matches(ISymbol symbol, StaticDependencySymbol identity) =>
        string.Equals(symbol.GetDocumentationCommentId(), identity.DocumentationId, StringComparison.Ordinal)
        && string.Equals(symbol.ContainingAssembly?.Identity.ToString(), identity.AssemblyIdentity, StringComparison.Ordinal);

    internal static IEnumerable<string> ValidateDeclarations(StaticDependencyMethodMapping mapping)
    {
        if (mapping.SchemaVersion != "rt.static-dependency.mapping.v1")
            yield return $"Mapping schema '{mapping.SchemaVersion}' is unsupported; use rt.static-dependency.mapping.v1.";
        var declarations = new (string Name, string Value)[]
        {
            ("receiver availability", mapping.Preconditions.ReceiverAvailability),
            ("receiver lifetime", mapping.Preconditions.ReceiverLifetime),
            ("receiver non-null", mapping.Preconditions.ReceiverNonNull),
            ("adapter exceptions", mapping.Preconditions.AdapterExceptions),
            ("adapter encoding", mapping.Preconditions.AdapterEncoding),
            ("adapter cancellation", mapping.Preconditions.AdapterCancellation),
            ("adapter resource ownership", mapping.Preconditions.AdapterResourceOwnership)
        };
        foreach (var (name, value) in declarations)
        {
            if (string.IsNullOrWhiteSpace(value))
                yield return $"Mapping precondition '{name}' must contain a caller declaration; binding does not prove adapter behavior.";
        }
        if (mapping.Preconditions.ContractEvidence.Count == 0 || mapping.Preconditions.ContractEvidence.Any(string.IsNullOrWhiteSpace))
            yield return "Mapping precondition contract evidence must contain nonempty caller-supplied evidence.";
    }

    internal static IEnumerable<string> ValidateMethods(IMethodSymbol source, IMethodSymbol target)
    {
        if (!source.IsStatic || source.MethodKind != MethodKind.Ordinary || source.IsGenericMethod)
            yield return "Source mapping must identify an ordinary non-generic static method.";
        if (target.IsStatic || target.MethodKind != MethodKind.Ordinary || target.IsGenericMethod)
            yield return "Target mapping must identify an ordinary non-generic instance method.";
        if (source.ReturnsByRef || source.ReturnsByRefReadonly || target.ReturnsByRef || target.ReturnsByRefReadonly)
            yield return "Ref return mapping is unsupported.";
        if (!SymbolEqualityComparer.Default.Equals(source.ReturnType, target.ReturnType))
            yield return "Source and target return types must match exactly; return adaptation is unsupported.";
        if (source.Parameters.Length != target.Parameters.Length)
            yield return "Source and target parameter counts must match exactly; argument adaptation is unsupported.";
        foreach (var parameter in source.Parameters.Concat(target.Parameters))
        {
            if (parameter.RefKind != RefKind.None)
                yield return $"Ref parameter '{parameter.Name}' is unsupported; use ordinary value parameters.";
            if (parameter.IsParams)
                yield return $"Params parameter '{parameter.Name}' is unsupported; expansion adaptation is not available.";
            if (parameter.IsOptional)
                yield return $"Optional parameter '{parameter.Name}' is unsupported; default argument adaptation is not available.";
        }
        for (var index = 0; index < Math.Min(source.Parameters.Length, target.Parameters.Length); index++)
        {
            if (!SymbolEqualityComparer.Default.Equals(source.Parameters[index].Type, target.Parameters[index].Type))
                yield return $"Parameter {index} types must match exactly to preserve argument conversions; parameter adaptation is unsupported.";
        }
    }

    internal static string? ValidateBinding(
        IInvocationOperation original, IInvocationOperation candidate,
        StaticDependencyMethodMapping mapping, IFieldSymbol receiver)
    {
        // One proof owns the actual target, receiver and ordered argument evidence.
        // A successful compilation alone cannot establish any of these identities.
        var field = candidate.Instance as IFieldReferenceOperation;
        var sameBinding = Matches(candidate.TargetMethod, mapping.Target)
            && field is { Instance: IInstanceReferenceOperation }
            && field.Field.GetDocumentationCommentId() == receiver.GetDocumentationCommentId()
            && string.Equals(field.Field.ContainingAssembly.Identity.ToString(), receiver.ContainingAssembly.Identity.ToString(), StringComparison.Ordinal)
            && original.Arguments.Length == candidate.Arguments.Length
            && original.Arguments.Zip(candidate.Arguments).All(pair => SameArgumentBinding(pair.First, pair.Second));
        return sameBinding ? null
            : $"Target binding resolved '{candidate.TargetMethod.GetDocumentationCommentId()}' in assembly '{candidate.TargetMethod.ContainingAssembly.Identity}'; expected exact target '{mapping.Target.DocumentationId}' in '{mapping.Target.AssemblyIdentity}' on this instance's field '{receiver.GetDocumentationCommentId()}' in '{receiver.ContainingAssembly.Identity}', with unchanged ordered parameter/argument bindings and conversions. Argument adaptation is unsupported.";
    }

    internal static bool SameArgumentBinding(IArgumentOperation before, IArgumentOperation after) =>
        before.Parameter?.Ordinal == after.Parameter?.Ordinal
        && before.ArgumentKind == after.ArgumentKind
        && TypeIdentity(before.Parameter?.Type) == TypeIdentity(after.Parameter?.Type)
        && before.Syntax.ToFullString() == after.Syntax.ToFullString()
        && ConversionIdentity(before.InConversion) == ConversionIdentity(after.InConversion)
        && ConversionIdentity(before.OutConversion) == ConversionIdentity(after.OutConversion)
        && SameValueBinding(before.Value, after.Value);

    // Contextual typing can change nested calls/operators while compilation succeeds.
    private static bool SameValueBinding(IOperation before, IOperation after) =>
        before.Kind == after.Kind
        && TypeIdentity(before.Type) == TypeIdentity(after.Type)
        && (before is not IConversionOperation conversion
            || after is IConversionOperation changed
            && conversion.IsChecked == changed.IsChecked
            && ConversionIdentity(conversion.Conversion) == ConversionIdentity(changed.Conversion))
        && BoundSymbol(before) == BoundSymbol(after)
        && before.ChildOperations.Count == after.ChildOperations.Count
        && before.ChildOperations.Zip(after.ChildOperations).All(pair => SameValueBinding(pair.First, pair.Second));

    private static string? BoundSymbol(IOperation operation) => operation switch
    {
        IInvocationOperation invocation => SymbolIdentity(invocation.TargetMethod),
        IConversionOperation conversion => SymbolIdentity(conversion.OperatorMethod),
        IBinaryOperation binary => SymbolIdentity(binary.OperatorMethod),
        IUnaryOperation unary => SymbolIdentity(unary.OperatorMethod),
        IMemberReferenceOperation member => SymbolIdentity(member.Member),
        IObjectCreationOperation creation => SymbolIdentity(creation.Constructor),
        _ => null
    };

    private static string ConversionIdentity(CommonConversion conversion) =>
        $"{conversion.Exists}|{conversion.IsIdentity}|{conversion.IsImplicit}|{conversion.IsNumeric}|{conversion.IsReference}|{conversion.IsNullable}|{conversion.IsUserDefined}|{SymbolIdentity(conversion.MethodSymbol)}";

    private static string? SymbolIdentity(ISymbol? symbol) => symbol is null ? null
        : $"{symbol.GetDocumentationCommentId()}|{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}|{symbol.ContainingAssembly?.Identity}";

    private static string? TypeIdentity(ITypeSymbol? type) => type is null ? null
        : $"{type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}|{type.ContainingAssembly?.Identity}";
}
