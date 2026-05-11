using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// An interface representing a solution change writer.
/// </summary>
internal interface ISolutionChangeWriter
{
    Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun);
}

/// <summary>
/// A class that implements the ISolutionChangeWriter interface.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class SolutionChangeWriter : ISolutionChangeWriter
{
    public Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun)
    {
        return SolutionWriter.ApplyChangesAsync(originalSolution, newSolution, dryRun);
    }
}
