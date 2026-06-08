using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticDiff;

namespace RoslynTools.Commands.Results;

/// <summary>
///     Output projection for <c>rt analyze semantic-diff</c>. Carries the engine's
///     <see cref="SemanticDiffResult.Deltas" /> alongside the already-rendered git-diff text so the
///     shared renderer can pick the shape from the global <c>--json</c> flag:
///     <list type="bullet">
///         <item><see cref="RenderText" /> writes the custom git-diff report verbatim.</item>
///         <item><see cref="RenderJson" /> emits a JSON array — one object per delta.</item>
///     </list>
///     The text is pre-rendered in the command handler (which owns the before/after snapshots the
///     <see cref="SemanticDiffTextRenderer" /> needs); this record stays a pure data carrier so no
///     service reference leaks into the result type.
/// </summary>
internal sealed record SemanticDiffOutput(IReadOnlyList<DeclarationDelta> Deltas, string Text)
    : IRtOutput, IRtJsonOutput
{
    public void RenderText(TextWriter writer)
    {
        writer.Write(Text);
    }

    public void RenderJson(TextWriter writer)
    {
        var array = Deltas.Where(SemanticDiffDelta.IsSurfaced).Select(d => new
        {
            fqn = d.Fqn,
            kind = d.Kind.ToString(),
            status = d.Status.ToString(),
            oldPath = d.OldPath,
            newPath = d.NewPath,
            oldFqn = d.OldFqn,
            newFqn = d.NewFqn
        }).ToArray();

        writer.Write(JsonSerializer.Serialize(array, SymbolFormatter.DefaultJsonOptions));
    }
}
