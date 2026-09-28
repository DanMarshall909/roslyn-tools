using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     The injection seam for applying solution mutations to disk (or previewing
///     them in dry-run). Production code depends on this interface rather than
///     the static <c>SolutionWriter</c> helper so mutation tests can stub the
///     writer and DI-managed writers can layer policy.
/// </summary>
internal interface ISolutionChangeWriter
{
    Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun);

    /// <summary>
    ///     Writes a batch of <see cref="Fix" /> candidates against <paramref name="originalSolution" />.
    ///     Dry-run reports one <c>[fix &lt;id&gt;]</c> header per fix above its file diffs;
    ///     apply writes the composed new solution to disk. An empty fix list reports
    ///     <c>No changes.</c> and writes nothing. Reporting uses the writer's output sink.
    /// </summary>
    Task WriteFixesAsync(Solution originalSolution, IReadOnlyList<Fix> fixes, bool dryRun);
}
