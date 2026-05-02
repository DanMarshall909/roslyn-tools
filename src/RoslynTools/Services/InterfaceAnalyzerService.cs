using Microsoft.CodeAnalysis;

namespace RoslynTools.Services;

/// <summary>
/// Pure analysis functions for interface members: determines which members of an
/// interface a given consumer type actually uses, and generates new interface text
/// from a selected member subset.
/// </summary>
internal static class InterfaceAnalyzerService
{
    /// <summary>
    /// Returns the set of member names from <paramref name="interfaceSymbol"/> that are
    /// actually referenced within the body of <paramref name="consumerSymbol"/>.
    /// </summary>
    internal static Task<HashSet<string>> GetUsedMembersAsync(
        Solution solution,
        INamedTypeSymbol consumerSymbol,
        INamedTypeSymbol interfaceSymbol)
    {
        throw new NotImplementedException("InterfaceAnalyzerService.GetUsedMembersAsync is not yet implemented.");
    }

    /// <summary>
    /// Generates C# source text for a new interface named <paramref name="interfaceName"/>
    /// in <paramref name="namespaceName"/> containing exactly the given <paramref name="members"/>.
    /// </summary>
    internal static string BuildInterfaceText(
        string namespaceName,
        string interfaceName,
        List<ISymbol> members)
    {
        throw new NotImplementedException("InterfaceAnalyzerService.BuildInterfaceText is not yet implemented.");
    }
}
