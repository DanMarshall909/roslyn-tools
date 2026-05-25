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
    /// <summary>
    ///     The sanctioned default factory for code paths that may run without an
    ///     IServiceProvider (test paths using <c>App.RunAsync</c> with
    ///     <c>services: null</c>). Centralising the construction here is what
    ///     lets the <c>NoDirectSolutionWriterUse</c> architecture gate ban every
    ///     other production reference to the static <c>SolutionWriter</c> helper.
    /// </summary>
    static ISolutionChangeWriter CreateDefault() => new SolutionChangeWriter();

    Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun);

    /// <summary>
    ///     Writes a batch of <see cref="Fix" /> candidates against <paramref name="originalSolution" />.
    ///     Dry-run emits one <c>[fix &lt;id&gt;]</c> header per fix above its file diffs; apply writes
    ///     the composed new solution to disk. An empty fix list emits the existing <c>No changes.</c>
    ///     no-op message and writes nothing.
    /// </summary>
    Task WriteFixesAsync(Solution originalSolution, IReadOnlyList<Fix> fixes, bool dryRun);
}
