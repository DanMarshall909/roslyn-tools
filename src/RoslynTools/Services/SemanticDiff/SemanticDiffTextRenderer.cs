using System.Diagnostics.CodeAnalysis;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Host-agnostic renderer that turns a <see cref="SemanticDiffResult" /> into a
///     git-diff-style plain-text report. Returns a string — no <c>Console</c>, no
///     <c>System.CommandLine</c> — so any host (CLI, MCP) can wrap it.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class registered in DI per the static-to-instance migration (EP-006). CA1822 enforces the old static-when-stateless rule that conflicts with this direction; suppress per class until the migration completes and the rule is reviewed project-wide.")]
internal sealed class SemanticDiffTextRenderer
{
    /// <summary>Emitted when there is nothing semantically interesting to show.</summary>
    public const string NoChanges = "no semantic changes";

    /// <summary>
    ///     Render <paramref name="result" /> against the <paramref name="before" /> and
    ///     <paramref name="after" /> snapshots. The result carries no source text, so each
    ///     declaration's node is re-located by FQN through <see cref="DeclarationWalker" />.
    /// </summary>
    public string Render(
        SemanticDiffResult result,
        IReadOnlyList<SemanticDiffSource> before,
        IReadOnlyList<SemanticDiffSource> after) => string.Empty;
}
