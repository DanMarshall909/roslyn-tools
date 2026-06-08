namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     What happened to a single declaration between the "before" and "after"
///     snapshots of a <see cref="SemanticDiffRequest" />.
/// </summary>
internal enum DeclarationStatus
{
    /// <summary>The declaration's FQN exists only in the "after" snapshot.</summary>
    Added,

    /// <summary>The declaration's FQN exists only in the "before" snapshot.</summary>
    Removed,

    /// <summary>
    ///     The declaration's FQN exists in both snapshots but the two bodies are not
    ///     equivalent. Not produced by the foundational cycle (body-equality is a
    ///     later story); the value exists so the contract is stable for serializers.
    /// </summary>
    Changed,

    /// <summary>The declaration's FQN exists in both snapshots with no observed change.</summary>
    Unchanged,

    /// <summary>
    ///     The declaration was renamed: an otherwise-Removed declaration in the
    ///     "before" snapshot pairs with an otherwise-Added one in the "after" snapshot.
    ///     Not produced by the foundational cycle (rename pairing is a later story); the
    ///     value exists so the contract is stable for serializers.
    /// </summary>
    Renamed,
}

/// <summary>
///     The syntactic kind of a declaration, used to classify a
///     <see cref="DeclarationDelta" />. Covers the type kinds and member kinds the
///     engine walks. Enum members are intentionally <em>not</em> reported as
///     declarations.
/// </summary>
internal enum DeclarationKind
{
    Class,
    Struct,
    Record,
    Interface,
    Enum,
    Delegate,
    Method,
    Constructor,
    Property,
    Field,
    Event,
    Indexer,
    Operator,
}

/// <summary>
///     One declaration's delta between the two snapshots. The <see cref="Fqn" /> is
///     the syntactic fully-qualified name the engine keys on — identical to the key
///     used internally so display and identity never drift. For types it is
///     <c>Namespace.Outer.Type`Arity</c>; for members it is
///     <c>ContainingTypeFqn.Member`Arity(paramType1,paramType2)</c> with parameter
///     types rendered as written (internal whitespace collapsed). Later stories
///     serialize this shape.
/// </summary>
/// <param name="Fqn">Syntactic fully-qualified name of the declaration.</param>
/// <param name="Kind">The declaration's syntactic kind.</param>
/// <param name="Status">Added / Removed / Changed / Unchanged.</param>
/// <param name="OldPath">
///     Logical path of the document containing the declaration in the "before"
///     snapshot, or <c>null</c> when the declaration is <see cref="DeclarationStatus.Added" />.
/// </param>
/// <param name="NewPath">
///     Logical path of the document containing the declaration in the "after"
///     snapshot, or <c>null</c> when the declaration is <see cref="DeclarationStatus.Removed" />.
/// </param>
/// <param name="OldFqn">
///     The declaration's prior FQN. Reserved for the rename pairing that lands in a
///     later cycle: a <see cref="DeclarationStatus.Renamed" /> delta will set this to
///     the "before" name and <see cref="NewFqn" /> to the "after" name. The
///     foundational cycle never produces a Renamed delta, so this is always
///     <c>null</c> here.
/// </param>
/// <param name="NewFqn">
///     The declaration's new FQN. See <see cref="OldFqn" /> — always <c>null</c> in
///     the foundational cycle.
/// </param>
internal sealed record DeclarationDelta(
    string Fqn,
    DeclarationKind Kind,
    DeclarationStatus Status,
    string? OldPath,
    string? NewPath,
    string? OldFqn = null,
    string? NewFqn = null);

/// <summary>
///     Result of <see cref="SemanticDiffService.Compare" />: the per-declaration
///     deltas, ordered deterministically by <see cref="DeclarationDelta.Fqn" /> so
///     callers and tests see a stable sequence.
/// </summary>
/// <param name="Deltas">The declaration deltas, sorted by FQN.</param>
internal sealed record SemanticDiffResult(IReadOnlyList<DeclarationDelta> Deltas);
