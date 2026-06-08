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
///     Invocation-agnostic input to <see cref="SemanticDiffService.Compare" />: a
///     "before" snapshot and an "after" snapshot, each an ordered set of in-memory
///     <see cref="SemanticDiffSource" /> documents. The service keys declarations by
///     a syntactic FQN (no semantic model) and reports per-declaration deltas.
/// </summary>
/// <param name="Before">Source documents representing the prior state.</param>
/// <param name="After">Source documents representing the new state.</param>
internal sealed record SemanticDiffRequest(
    IReadOnlyList<SemanticDiffSource> Before,
    IReadOnlyList<SemanticDiffSource> After);
