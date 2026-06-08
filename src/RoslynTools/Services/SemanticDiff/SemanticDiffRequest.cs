namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     A single in-memory source document fed to <see cref="SemanticDiffService" />.
///     Carries a logical file path (used only as provenance in the result — never
///     opened from disk) and the raw C# source text, which the service parses with
///     <c>CSharpSyntaxTree.ParseText</c>. No MSBuild workspace is involved.
/// </summary>
/// <param name="Path">
///     Logical path of the document, surfaced as
///     <see cref="DeclarationDelta.OldPath" /> / <see cref="DeclarationDelta.NewPath" />.
/// </param>
/// <param name="Text">The full C# source text to parse.</param>
internal sealed record SemanticDiffSource(string Path, string Text);

/// <summary>
///     Tuning knobs for <see cref="SemanticDiffService.Compare" />. Both options are
///     part of the contract from the foundational cycle so serializers and host
///     adapters are stable, but neither changes behaviour yet — they are acted on by
///     later cycles (accessibility-aware equality; rename detection).
/// </summary>
/// <param name="IgnoreAccessibility">
///     When <c>true</c>, a later cycle treats two declarations that differ only in
///     accessibility modifiers as equivalent. No effect in the foundational cycle.
/// </param>
/// <param name="DetectRenames">
///     When <c>true</c>, a later cycle pairs an otherwise-Removed declaration with an
///     otherwise-Added one as a single <see cref="DeclarationStatus.Renamed" /> delta.
///     No effect in the foundational cycle.
/// </param>
internal sealed record SemanticDiffOptions(
    bool IgnoreAccessibility = false,
    bool DetectRenames = true)
{
    /// <summary>Sensible defaults: accessibility differences matter, rename detection on.</summary>
    public static SemanticDiffOptions Default { get; } = new();
}

/// <summary>
///     Invocation-agnostic input to <see cref="SemanticDiffService.Compare" />: a
///     "before" snapshot and an "after" snapshot, each an ordered set of in-memory
///     <see cref="SemanticDiffSource" /> documents. The service keys declarations by
///     a syntactic FQN (no semantic model) and reports per-declaration deltas.
/// </summary>
/// <param name="Before">Source documents representing the prior state.</param>
/// <param name="After">Source documents representing the new state.</param>
/// <param name="Options">
///     Comparison options; defaults to <see cref="SemanticDiffOptions.Default" /> so
///     callers never have to supply one.
/// </param>
internal sealed record SemanticDiffRequest(
    IReadOnlyList<SemanticDiffSource> Before,
    IReadOnlyList<SemanticDiffSource> After,
    SemanticDiffOptions Options)
{
    public SemanticDiffRequest(
        IReadOnlyList<SemanticDiffSource> before,
        IReadOnlyList<SemanticDiffSource> after)
        : this(before, after, SemanticDiffOptions.Default)
    {
    }
}
