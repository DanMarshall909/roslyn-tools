using RoslynTools.Core;

namespace RoslynTools.Services.SemanticReview;

internal sealed partial class SolidReviewOperation
{
    internal static RtError? Validate(SolidReviewRequest request)
    {
        if (request.DryRun && request.TransmitSource)
        {
            return new RtError(
                RtErrorKind.InvalidInput,
                "--dry-run and --transmit-source are mutually exclusive.");
        }

        var principle = request.Principle.ToUpperInvariant();
        if (principle is not ("SRP" or "ISP" or "DIP" or "OCP" or "LSP" or "ALL"))
        {
            return new RtError(
                RtErrorKind.InvalidInput,
                "--principle must be SRP, ISP, DIP, OCP, LSP, or all.");
        }

        if (new[] { request.MinMethods, request.MinDeps, request.MinMembers, request.MinBranches }
            .Any(value => value < 0))
        {
            return new RtError(RtErrorKind.InvalidInput, "SOLID thresholds must be >= 0.");
        }

        if (request.MaxCandidates is < 1 or > 100)
        {
            return new RtError(
                RtErrorKind.InvalidInput,
                "--max-candidates must be between 1 and 100.");
        }

        if (string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Model))
        {
            return new RtError(
                RtErrorKind.InvalidInput,
                "Provider and model must be non-empty.");
        }

        return null;
    }
}
