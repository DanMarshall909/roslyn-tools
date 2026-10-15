using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>Unwired boundary for retaining original authority across candidate edits.</summary>
internal sealed class StaticDependencyPreparation
{
    internal static Task<(StaticDependencyPreparation? Preparation, IReadOnlyList<StaticDependencySite> Blockers)> AcquireAsync(
        Solution original, StaticDependencySelection selection, string expectedFingerprint, CancellationToken cancellationToken) =>
        throw new NotImplementedException("The original-authority preparation boundary is not implemented.");

    internal Task<IReadOnlyList<StaticDependencySite>> VerifyAsync(Solution candidate, CancellationToken cancellationToken) =>
        throw new NotImplementedException("The original-authority verification boundary is not implemented.");
}
