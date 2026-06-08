namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Shared rules for projecting <see cref="DeclarationDelta" />s. The single source of truth for
///     "which deltas are worth surfacing" so the text renderer and the JSON projection cannot drift.
/// </summary>
internal static class SemanticDiffDelta
{
    /// <summary>
    ///     A delta is surfaced (rendered as text, included in the JSON array) iff it represents an
    ///     observable change. Every status except <see cref="DeclarationStatus.Unchanged" /> qualifies;
    ///     an Unchanged delta only qualifies when its document path changed — a relocation the text
    ///     renderer reports as <c>&gt; Moved</c>. Same-path Unchanged deltas are engine bookkeeping
    ///     (needed for move/rename detection) and are silent in both shapes.
    /// </summary>
    internal static bool IsSurfaced(DeclarationDelta delta) =>
        delta.Status != DeclarationStatus.Unchanged || delta.OldPath != delta.NewPath;
}
