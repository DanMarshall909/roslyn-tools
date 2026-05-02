using Microsoft.CodeAnalysis;

namespace RoslynTools.Services;

/// <summary>
/// Rewriting logic for narrowing consumers: finds all types that depend on a wide
/// interface and rewrites those that only use a selected member subset to use the
/// new narrower interface instead.
/// </summary>
internal static class ConsumerNarrowerService
{
    /// <summary>
    /// Finds all types in the solution that hold a field or constructor parameter
    /// typed as <paramref name="interfaceSymbol"/>.
    /// </summary>
    internal static Task<List<(INamedTypeSymbol Symbol, Document Document)>> FindConsumersAsync(
        Solution solution,
        INamedTypeSymbol interfaceSymbol)
    {
        throw new NotImplementedException("ConsumerNarrowerService.FindConsumersAsync is not yet implemented.");
    }

    /// <summary>
    /// For each consumer that exclusively uses the selected member subset, rewrites
    /// its type annotations from the old interface name to the new one and returns
    /// the updated solution together with lists of narrowed and skipped consumers.
    /// </summary>
    internal static Task<NarrowResult> NarrowConsumersAsync(
        Solution originalSolution,
        INamedTypeSymbol interfaceSymbol,
        List<ISymbol> selectedMembers,
        string newInterfaceName,
        Solution workingSolution)
    {
        throw new NotImplementedException("ConsumerNarrowerService.NarrowConsumersAsync is not yet implemented.");
    }

    internal sealed record NarrowResult(
        Solution Solution,
        List<string> NarrowedConsumers,
        List<(string Name, string Reason)> SkippedConsumers);
}
