using System.Text.Json;
using RoslynTools.Core;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     The single source of truth for the JSON projection of a semantic diff. Both the CLI
///     (<c>rt analyze semantic-diff --json</c>, via <c>SemanticDiffOutput.RenderJson</c>) and the
///     native MCP <c>semantic_diff</c> handler serialise through this method, so their JSON is
///     byte-identical for the same set of deltas. Filters to <see cref="SemanticDiffDelta.IsSurfaced" />
///     deltas, projects each to a stable camelCase shape (enum names as strings), and serialises with
///     <see cref="SymbolFormatter.DefaultJsonOptions" /> so the wire format cannot drift between hosts.
/// </summary>
internal static class SemanticDiffJson
{
    internal static string Serialize(IReadOnlyList<DeclarationDelta> deltas)
    {
        var array = deltas.Where(SemanticDiffDelta.IsSurfaced).Select(d => new
        {
            fqn = d.Fqn,
            kind = d.Kind.ToString(),
            status = d.Status.ToString(),
            oldPath = d.OldPath,
            newPath = d.NewPath,
            oldFqn = d.OldFqn,
            newFqn = d.NewFqn
        }).ToArray();

        return JsonSerializer.Serialize(array, SymbolFormatter.DefaultJsonOptions);
    }
}
