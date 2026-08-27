using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;

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
    [property: OutputField(OutputLevel.Default, "catalogVersion")] string CatalogVersion,
    [property: OutputField(OutputLevel.Default, "generatedCode")] string GeneratedCode,
    [property: OutputField(OutputLevel.Default, "totals")] LanguageFeatureTotals Totals,
    [property: OutputField(OutputLevel.Detailed, "projects")] IReadOnlyList<LanguageFeatureProjectResult> Projects,
    [property: OutputField(OutputLevel.Default, "features")] IReadOnlyList<LanguageFeatureResult> Features,
    [property: OutputField(OutputLevel.Default, "diagnostics")] IReadOnlyList<LanguageFeatureDiagnostic> Diagnostics) : IRtOutput, IRtJsonOutput
{
    private static readonly JsonSerializerOptions StrictJsonOptions = CreateJsonOptions(false);
    private static readonly JsonSerializerOptions RelaxedJsonOptions = CreateJsonOptions(true);

    [OutputField(OutputLevel.Llm, "summary")]
    [JsonIgnore]
    public string LlmSummary =>
        $"{CatalogVersion}: {Totals.Projects} projects, {Totals.Files} files, "
        + $"{Features.Count} detected features; "
        + $"{Features.Count(feature => feature.Refactoring is not null)} refactoring reviews";

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine(
            $"C# language features: {Totals.Projects} projects | {Totals.Files} files | "
            + $"{Totals.NonBlankSourceLines} non-blank source lines");
        writer.WriteLine($"Catalog: {CatalogVersion} | generated: {GeneratedCode}");

        if (Features.Count == 0)
        {
            writer.WriteLine("No matching language features were detected.");
        }
        else
        {
            writer.WriteLine();
            writer.WriteLine("Feature                         Since   Uses  Files    /KLOC   File %  Adoption");
            foreach (var feature in Features)
            {
                var adoption = feature.Adoption is null
                    ? "-"
                    : feature.Adoption.Percent.ToString("0.###", CultureInfo.InvariantCulture) + "%";
                writer.WriteLine(
                    $"{feature.Id,-31} {feature.IntroducedIn,5} {feature.Occurrences,6} "
                    + $"{feature.Files,6} {feature.OccurrencesPerKloc,8:0.###} "
                    + $"{feature.FilePercent,8:0.###}%  {adoption}");
            }
        }

        var refactorings = Features.Where(feature => feature.Refactoring is not null).ToList();
        if (refactorings.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Refactoring review candidates:");
            foreach (var feature in refactorings)
            {
                var refactoring = feature.Refactoring!;
                writer.WriteLine(
                    $"- {feature.Id}: {refactoring.ReviewCandidates} — {refactoring.Description}");
                if (refactoring.CandidateLocations is null)
                    continue;

                foreach (var location in refactoring.CandidateLocations)
                    writer.WriteLine($"  {location.Path}:{location.Line}:{location.Column}");
            }
        }

        if (Diagnostics.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Partial-analysis diagnostics:");
            foreach (var diagnostic in Diagnostics)
                writer.WriteLine($"- {diagnostic.Code}: {diagnostic.Message}");
        }
    }

    public void RenderJson(TextWriter writer)
    {
        writer.Write(JsonSerializer.Serialize(
            this,
            RtContext.RelaxJson ? RelaxedJsonOptions : StrictJsonOptions));
    }

    private static JsonSerializerOptions CreateJsonOptions(bool relaxed)
    {
        return new JsonSerializerOptions(JsonEncoders.Select(relaxed))
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase
        };
    }
}

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

    public override string CommandName => "language_features";

    public override async Task<LanguageFeaturesResult> ExecuteAsync(
        LanguageFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.Solution);
        using (workspace)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await _analyzer.AnalyzeAsync(solution, request, cancellationToken);
        }
    }
}
