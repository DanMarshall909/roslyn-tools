using System.Text.Json;

using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticReview;

namespace RoslynTools.Commands.Results;

internal sealed record SolidReviewOutput(SolidReviewResult Result) : IRtOutput, IRtJsonOutput
{
    public void RenderJson(TextWriter writer)
    {
        writer.Write(JsonSerializer.Serialize(Result, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"SOLID semantic review: {Result.Outcome}");
        writer.WriteLine(Result.DryRun
            ? "Mode: preview (no source transmitted)"
            : "Mode: live source transmission");
        writer.WriteLine($"Provider: {Result.Provider} / {Result.RequestedModel}");
        writer.WriteLine($"Question set: {Result.QuestionSetVersion}");
        writer.WriteLine($"Repository revision: {Result.RepositoryRevision}");
        writer.WriteLine($"Estimated requests: {Result.EstimatedRequestCount}");

        if (Result.Plan.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Bounded review plan:");
            foreach (var item in Result.Plan)
            {
                var completeness = item.EvidenceComplete ? "complete" : "incomplete";
                writer.WriteLine(
                    $"- [{item.Principle}] {item.Symbol} ({item.FilePath}:{item.Line}) "
                    + $"{item.SourceBytes} bytes, {item.Disposition}, evidence {completeness}");
                if (!string.IsNullOrWhiteSpace(item.Reason))
                    writer.WriteLine($"  {item.Reason}");
            }
        }

        if (Result.Assessments.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Advisory assessments:");
            foreach (var assessment in Result.Assessments)
            {
                writer.WriteLine(
                    $"- [{assessment.Principle}] {assessment.Symbol}: {assessment.Advisory} "
                    + $"(confidence {assessment.Confidence:0.00}, request {assessment.RequestId})");
                writer.WriteLine(
                    $"  Provider: {assessment.ProviderClassification}; meaningful problem "
                    + $"{assessment.MeaningfulDesignProblem:0.00}; accepted exception "
                    + $"{assessment.AcceptedException:0.00}");
                if (assessment.ProviderFalsePositiveReason
                    != SemanticReviewFalsePositiveReasons.NoAcceptedException)
                {
                    writer.WriteLine(
                        $"  Candidate exception: {assessment.ProviderFalsePositiveReason} "
                        + $"(confidence {assessment.FalsePositiveReasonConfidence:0.00})");
                }

                writer.WriteLine($"  Policy: {string.Join(", ", assessment.PolicyReasons)}");
            }
        }

        if (Result.Failures.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Provider failures:");
            foreach (var failure in Result.Failures)
                writer.WriteLine($"- {failure.CandidateId ?? "request"}: {failure.Kind}: {failure.Message}");
        }

        writer.WriteLine();
        writer.WriteLine("Advisory only; deterministic findings are preserved in raw output.");
    }
}
