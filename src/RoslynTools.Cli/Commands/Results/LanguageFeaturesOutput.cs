using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands.Results;

/// <summary>CLI projection for the host-neutral language-feature inventory.</summary>
internal sealed record LanguageFeaturesOutput(
    [property: OutputField(OutputLevel.Default, "catalogVersion")] string CatalogVersion,
    [property: OutputField(OutputLevel.Default, "generatedCode")] string GeneratedCode,
    [property: OutputField(OutputLevel.Default, "totals")] LanguageFeatureTotals Totals,
    [property: OutputField(OutputLevel.Detailed, "projects")]
    IReadOnlyList<LanguageFeatureProjectResult> Projects,
    [property: OutputField(OutputLevel.Default, "features")]
    IReadOnlyList<LanguageFeatureResult> Features,
    [property: OutputField(OutputLevel.Default, "diagnostics")]
    IReadOnlyList<LanguageFeatureDiagnostic> Diagnostics) : IRtOutput, IRtJsonOutput
{
    private static readonly JsonSerializerOptions StrictJsonOptions = CreateJsonOptions(false);
    private static readonly JsonSerializerOptions RelaxedJsonOptions = CreateJsonOptions(true);

    [OutputField(OutputLevel.Llm, "summary")]
    [JsonIgnore]
    public string LlmSummary =>
        $"{CatalogVersion}: {Totals.Projects} projects, {Totals.Files} files, "
        + $"{Features.Count} detected features; "
        + $"{Features.Count(feature => feature.Refactoring is not null)} refactoring reviews";

    internal static LanguageFeaturesOutput From(LanguageFeaturesResult result) => new(
        result.CatalogVersion,
        result.GeneratedCode,
        result.Totals,
        result.Projects,
        result.Features,
        result.Diagnostics);

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
