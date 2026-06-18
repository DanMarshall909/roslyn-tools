using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     Produces the canonical, compact member signature used by the default
///     <c>rt inspect describe</c> / <c>rt inspect outline</c> output (US-135).
///     The <see cref="Signature" /> form is human-readable (return type + name + short param types,
///     no param names); the <see cref="MatchSignature" /> form drops the return-type prefix so a
///     displayed signature, re-appended to its type's FQN, round-trips back to its member via
///     <see cref="SymbolLocator.TryFindMemberBySignatureAsync" />.
/// </summary>
internal static class SignatureFormatter
{
    // Display form: "Task<Result<ISymbol, RtError>> TryFindSymbolAsync(Solution, string, string?)".
    // Short type names (NameOnly default), no containing type, no param names, special types ("int"
    // not "Int32"), nullable ref modifiers preserved.
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeType
                       | SymbolDisplayMemberOptions.IncludeParameters
                       | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
                          | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                              | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    // Match form: same as display minus the return-type prefix → "TryFindSymbolAsync(Solution, string, string?)".
    private static readonly SymbolDisplayFormat MatchFormat = new(
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters
                       | SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions: SymbolDisplayParameterOptions.IncludeType
                          | SymbolDisplayParameterOptions.IncludeParamsRefOut,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
                              | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>
    ///     The canonical display signature for a member: return type (for methods/properties/fields),
    ///     name, and parenthesised short param types for methods. Used for the <c>signatures: []</c> array.
    /// </summary>
    internal static string Signature(ISymbol symbol) => symbol.ToDisplayString(DisplayFormat);

    /// <summary>
    ///     The return-type-free signature used to match a drill-in input against a member:
    ///     "<c>Name(paramType, ...)</c>" for methods, or just "<c>Name</c>" for non-method members.
    /// </summary>
    internal static string MatchSignature(ISymbol symbol) => symbol.ToDisplayString(MatchFormat);

    /// <summary>
    ///     One <see cref="Signature" /> per declared member of <paramref name="type" />, in source order.
    ///     Compiler-generated members and property/event accessors (get_/set_/add_/remove_) are excluded
    ///     so the array carries one entry per logical member.
    /// </summary>
    internal static IReadOnlyList<string> TypeMemberSignatures(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Where(m => m is not IMethodSymbol { AssociatedSymbol: not null })
            .Select(Signature)
            .ToList();
}
