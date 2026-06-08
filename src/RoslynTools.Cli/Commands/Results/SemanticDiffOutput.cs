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
        // Delegates to the shared core projection so the CLI's --json and the native MCP
        // semantic_diff handler emit byte-identical JSON for the same deltas.
        writer.Write(SemanticDiffJson.Serialize(Deltas));
    }
}
