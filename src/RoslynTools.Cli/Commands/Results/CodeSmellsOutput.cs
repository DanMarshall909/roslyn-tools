using System.Text.Json;

using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticReview;

namespace RoslynTools.Commands.Results;

internal sealed record CodeSmellsOutput(CodeSmellsResult Result) : IRtOutput, IRtJsonOutput
{
    public void RenderJson(TextWriter writer) => writer.Write(JsonSerializer.Serialize(Result, SymbolFormatter.DefaultJsonOptions));

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"Code smell review: {Result.Outcome}; catalogue {Result.CatalogVersion}");
        writer.WriteLine($"Estimated requests: {Result.EstimatedRequestCount}; findings: {Result.Findings.Count}");
        foreach (var item in Result.Plan)
            writer.WriteLine($"{item.CandidateId}: {item.Disposition} {item.Reason}");
        foreach (var failure in Result.Failures)
            writer.WriteLine($"{failure.CandidateId}: {failure.Kind}: {failure.Message}");
    }
}
