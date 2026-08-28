using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for C# language-feature analysis.</summary>
internal sealed record LanguageFeaturesRequest(
    string? Solution = null,
    string? Feature = null,
    string? Category = null,
    string? IntroducedSince = null,
    int? Top = null,
    bool IncludeLocations = false,
    bool IncludeGenerated = false,
    bool RefactorableOnly = false);

/// <summary>Analyzed solution totals used as the universal density denominator.</summary>
internal sealed record LanguageFeatureTotals(int Projects, int Files, int NonBlankSourceLines);

/// <summary>A one-based source position for a feature occurrence or review candidate.</summary>
internal sealed record LanguageFeatureLocation(
    string Project,
    string Path,
    int Line,
    int Column);

/// <summary>Opportunity-based adoption, emitted only when its denominator is mechanical.</summary>
internal sealed record LanguageFeatureAdoption(
    int Numerator,
    int Denominator,
    string Opportunity,
    double Percent,
    int Exceptions,
    IReadOnlyList<LanguageFeatureLocation>? ExceptionLocations);

/// <summary>
///     Comparable alternatives worth reviewing. <see cref="IsAutomatic" /> remains false unless
///     every candidate is proven safe and maps deterministically to an existing rt command.
/// </summary>
internal sealed record LanguageFeatureRefactoringOpportunity(
    string Description,
    int ReviewCandidates,
    bool IsAutomatic,
    string? Command,
    IReadOnlyList<LanguageFeatureLocation>? CandidateLocations);

/// <summary>One catalogue feature with solution-wide counts and normalized density.</summary>
internal sealed record LanguageFeatureResult(
    string Id,
    string Name,
    string Category,
    string IntroducedIn,
    string DetectorKind,
    string CountingBasis,
    int Occurrences,
    int Files,
    double OccurrencesPerKloc,
    double FilePercent,
    LanguageFeatureAdoption? Adoption,
    LanguageFeatureRefactoringOpportunity? Refactoring,
    IReadOnlyList<LanguageFeatureLocation>? Locations);

/// <summary>Per-project metrics reuse the stable catalogue feature ID.</summary>
internal sealed record LanguageFeatureProjectMetric(
    string Id,
    int Occurrences,
    int Files,
    double OccurrencesPerKloc,
    double FilePercent,
    LanguageFeatureAdoption? Adoption);

/// <summary>One analyzed C# project and its effective language configuration.</summary>
internal sealed record LanguageFeatureProjectResult(
    string Name,
    string LanguageVersion,
    string EffectiveLanguageVersion,
    int Files,
    int NonBlankSourceLines,
    IReadOnlyList<LanguageFeatureProjectMetric> Features);

/// <summary>A typed partial-analysis or request diagnostic.</summary>
internal sealed record LanguageFeatureDiagnostic(
    string Code,
    string Severity,
    string Message,
    string? Project = null,
    string? Path = null,
    int? Line = null,
    int? Column = null);

/// <summary>Host-neutral language-feature inventory and refactoring drill-down.</summary>
internal sealed record LanguageFeaturesResult(
    string CatalogVersion,
    string GeneratedCode,
    LanguageFeatureTotals Totals,
    IReadOnlyList<LanguageFeatureProjectResult> Projects,
    IReadOnlyList<LanguageFeatureResult> Features,
    IReadOnlyList<LanguageFeatureDiagnostic> Diagnostics);

/// <summary>Shared typed application operation for CLI, daemon, and MCP adapters.</summary>
internal sealed class LanguageFeaturesOperation
    : CommandApplicationPort<LanguageFeaturesRequest, LanguageFeaturesResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly LanguageFeaturesAnalyzer _analyzer;

    public LanguageFeaturesOperation(
        IWorkspaceSessionFactory sessionFactory,
        LanguageFeaturesAnalyzer analyzer)
    {
        _sessionFactory = sessionFactory;
        _analyzer = analyzer;
    }

    public override ApplicationCommandDefinition Definition { get; } = new(
        LanguageFeaturesCommandDefinition.Info,
        typeof(LanguageFeaturesRequest), typeof(LanguageFeaturesResult), true,
        CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, []);

    public override async Task<Result<LanguageFeaturesResult, RtError>> ExecuteAsync(
        LanguageFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Result<LanguageFeaturesResult, RtError>.Ok(await _analyzer.AnalyzeAsync(solution, request, cancellationToken));
        }
    }
}
