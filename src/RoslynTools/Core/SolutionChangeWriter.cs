using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Core;

/// <summary>
/// The default <see cref="ISolutionChangeWriter" /> — a thin adapter over the
/// static <c>SolutionWriter</c> helper. DI-registered as the production binding.
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
