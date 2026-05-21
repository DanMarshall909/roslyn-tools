using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Tests;

internal sealed class CapturingSolutionChangeWriter : ISolutionChangeWriter
{
    public Solution? OriginalSolution { get; private set; }
    public Solution? Solution { get; private set; }
    public Solution? NewSolution => Solution;
    public bool? DryRun { get; private set; }

    public Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun)
    {
        OriginalSolution = originalSolution;
        Solution = newSolution;
        DryRun = dryRun;
        return Task.CompletedTask;
    }
}
