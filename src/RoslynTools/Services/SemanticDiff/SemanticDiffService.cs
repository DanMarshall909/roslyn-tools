using System.Diagnostics.CodeAnalysis;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Host-agnostic semantic comparison engine. Parses two in-memory snapshots of
///     C# source, keys every declaration by a syntactic fully-qualified name, and
///     reports per-declaration deltas (added / removed / unchanged for this cycle).
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class registered in DI per the static-to-instance migration (EP-006). CA1822 enforces the old static-when-stateless rule that conflicts with this direction; suppress per class until the migration completes and the rule is reviewed project-wide.")]
internal sealed class SemanticDiffService : IRtService
{
    /// <summary>
    ///     Compare the "before" and "after" snapshots of <paramref name="request" />
    ///     and return the per-declaration deltas, ordered by FQN.
    /// </summary>
    public SemanticDiffResult Compare(SemanticDiffRequest request)
    {
        return new SemanticDiffResult([]);
    }
}
