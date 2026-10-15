using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares one coherent dependency, constructor, caller and callee proposal.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "The internal planning service is composed through application operations.")]
internal sealed class StaticDependencyIntroductionService : IRtService
{
    internal Task<StaticDependencyIntroductionPlan> PrepareAsync(
        Solution solution,
        StaticDependencyIntroductionRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotImplementedException("Bounded static dependency introduction is not implemented.");
}
