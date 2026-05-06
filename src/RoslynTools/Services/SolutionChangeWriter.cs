using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal interface ISolutionChangeWriter
{
    Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class SolutionChangeWriter : ISolutionChangeWriter
{
    public Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun) =>
        SolutionWriter.ApplyChangesAsync(originalSolution, newSolution, dryRun);
}
