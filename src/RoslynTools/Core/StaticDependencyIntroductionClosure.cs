using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>Unwired original closure and independent candidate verification boundary.</summary>
internal sealed class StaticDependencyIntroductionClosure
{
    internal StaticDependencyIntroductionClosure(StaticDependencyPreparation preparation,
        StaticDependencyIntroductionRequest request, INamedTypeSymbol receiverType)
    {
    }

    internal string FieldName => throw new NotImplementedException("Dependency naming is not implemented.");

    internal Task<IReadOnlyList<StaticDependencySite>> AnalyzeAsync(
        IReadOnlyList<IParameterSymbol> callers, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Original constructor and value closure analysis is not implemented.");

    internal Task<Solution> IntroduceAsync(Solution annotated, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Dependency introduction is not implemented.");

    internal Task<(IReadOnlyList<StaticDependencyClosureChange> Changes, IReadOnlyList<StaticDependencySite> Blockers)> VerifyAsync(
        Solution candidate, CancellationToken cancellationToken) =>
        throw new NotImplementedException("Independent candidate closure verification is not implemented.");
}
