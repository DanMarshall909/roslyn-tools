using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares static dependency migrations without writing repository files.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "The internal planning service is composed through application operations.")]
internal sealed class StaticDependencyMappingService : IRtService
{
    internal Task<StaticDependencyPlan> PrepareAsync(
        Solution solution,
        StaticDependencyMappingRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Static dependency planning has not been implemented.");
    }
}
