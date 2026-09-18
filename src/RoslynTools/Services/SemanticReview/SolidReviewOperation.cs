using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services.SemanticReview;

internal sealed partial class SolidReviewOperation
    : CommandApplicationPort<SolidReviewRequest, SolidReviewResult>, IRtService
{
    private readonly IReadOnlyWorkspaceSessions _sessions;
    private readonly IReadOnlyList<ISemanticReviewProvider> _providers;

    internal SolidReviewOperation(
        IReadOnlyWorkspaceSessions sessions,
        IEnumerable<ISemanticReviewProvider> providers)
    {
        _sessions = sessions;
        _providers = providers.ToArray();
    }

    public override ApplicationCommandDefinition Definition => SolidReviewCommandDefinition.Definition;

    public override async Task<Result<SolidReviewResult, RtError>> ExecuteAsync(
        SolidReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationError = Validate(request);
        if (validationError is not null)
            return Result<SolidReviewResult, RtError>.Err(validationError);

        try
        {
            using var lease = await _sessions.AcquireAsync(request.Solution, cancellationToken);
            var raw = await AnalyzeRawAsync(lease.Solution, request, cancellationToken);
            var prepared = await SolidReviewCandidateBuilder.BuildAsync(
                lease.Solution,
                raw,
                request.MaxCandidates,
                cancellationToken);
            var plan = prepared.Select(candidate => candidate.Plan).ToArray();
            var eligible = prepared
                .Where(candidate => string.Equals(
                    candidate.Plan.Disposition,
                    "eligible",
                    StringComparison.Ordinal))
                .ToArray();

            if (!request.TransmitSource)
            {
                return Result<SolidReviewResult, RtError>.Ok(new SolidReviewResult(
                    "rt.solid-review.v1",
                    "Planned",
                    true,
                    request.Provider,
                    request.Model,
                    SemanticReviewQuestionSets.SolidV1,
                    lease.SourceRevision,
                    eligible.Length,
                    raw,
                    plan,
                    [],
                    []));
            }

            var provider = _providers.FirstOrDefault(candidate => string.Equals(
                candidate.Name,
                request.Provider,
                StringComparison.OrdinalIgnoreCase));
            if (provider is null)
            {
                return Result<SolidReviewResult, RtError>.Ok(FailedWithoutProvider(
                    request,
                    lease.SourceRevision,
                    raw,
                    plan,
                    eligible.Length));
            }

            var assessments = new List<SolidReviewAssessment>(eligible.Length);
            var failures = new List<SolidReviewFailureItem>();
            foreach (var candidate in eligible)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var providerResult = await provider.ReviewAsync(
                    new SemanticReviewProviderRequest(
                        candidate.Plan.Principle,
                        candidate.Plan.Symbol,
                        candidate.Plan.FilePath,
                        candidate.Plan.Line,
                        candidate.SanitizedSource,
                        candidate.Plan.RawEvidence,
                        candidate.Plan.EvidenceComplete,
                        lease.SourceRevision,
                        candidate.Plan.SourceEvidenceHash,
                        request.Model,
                        SemanticReviewQuestionSets.SolidV1),
                    cancellationToken);

                providerResult.Match(
                    response => assessments.Add(ToAssessment(candidate.Plan, lease.SourceRevision, response)),
                    failure => failures.Add(ToFailure(candidate.Plan.CandidateId, failure)));
            }

            var outcome = failures.Count == 0
                ? "Complete"
                : assessments.Count == 0 && eligible.Length > 0
                    ? "Failed"
                    : "Partial";
            return Result<SolidReviewResult, RtError>.Ok(new SolidReviewResult(
                "rt.solid-review.v1",
                outcome,
                false,
                provider.Name,
                request.Model,
                SemanticReviewQuestionSets.SolidV1,
                lease.SourceRevision,
                eligible.Length,
                raw,
                plan,
                assessments,
                failures));
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            return Result<SolidReviewResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, exception.Message));
        }
    }

    private static async Task<SolidResult> AnalyzeRawAsync(
        Microsoft.CodeAnalysis.Solution solution,
        SolidReviewRequest request,
        CancellationToken cancellationToken)
    {
        var principle = request.Principle.ToUpperInvariant();
        var result = new SolidResult();
        if (principle is "SRP" or "ALL")
        {
            result = result with
            {
                SRP = await SolidOperation.AnalyzeSrpAsync(
                    solution,
                    request.MinMethods,
                    request.MinDeps)
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (principle is "ISP" or "ALL")
            result = result with { ISP = await SolidOperation.AnalyzeIspAsync(solution, request.MinMembers) };

        cancellationToken.ThrowIfCancellationRequested();
        if (principle is "DIP" or "ALL")
            result = result with { DIP = await SolidOperation.AnalyzeDipAsync(solution) };

        cancellationToken.ThrowIfCancellationRequested();
        if (principle is "OCP" or "ALL")
        {
            result = result with
            {
                OCP = await SolidOperation.AnalyzeOcpAsync(solution, request.MinBranches)
            };
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (principle is "LSP" or "ALL")
            result = result with { LSP = await SolidOperation.AnalyzeLspAsync(solution) };

        return result;
    }

    private static SolidReviewAssessment ToAssessment(
        SolidReviewPlanItem plan,
        string repositoryRevision,
        SemanticReviewProviderResponse response)
    {
        var classification = SolidReviewPolicy.ApplyEvidenceGuard(
            plan.Principle,
            response.Classification,
            plan.EvidenceComplete);
        return new SolidReviewAssessment(
            plan.CandidateId,
            plan.Principle,
            plan.Symbol,
            plan.FilePath,
            plan.Line,
            classification,
            SolidReviewPolicy.ToAdvisory(classification),
            response.Probabilities,
            response.Confidence,
            plan.EvidenceComplete,
            response.Provider,
            response.RequestedModel,
            response.ResolvedModel,
            response.QuestionSetVersion,
            response.RequestId,
            repositoryRevision,
            plan.SourceEvidenceHash,
            plan.RawEvidence);
    }

    private static SolidReviewFailureItem ToFailure(
        string? candidateId,
        SemanticReviewFailure failure) => new(
        candidateId,
        failure.Kind.ToString(),
        failure.Message,
        failure.Retryable,
        failure.StatusCode);

    private static SolidReviewResult FailedWithoutProvider(
        SolidReviewRequest request,
        string repositoryRevision,
        SolidResult raw,
        IReadOnlyList<SolidReviewPlanItem> plan,
        int estimatedRequests) => new(
        "rt.solid-review.v1",
        "Failed",
        false,
        request.Provider,
        request.Model,
        SemanticReviewQuestionSets.SolidV1,
        repositoryRevision,
        estimatedRequests,
        raw,
        plan,
        [],
        [
            ToFailure(
                null,
                new SemanticReviewFailure(
                    SemanticReviewFailureKind.UnknownProvider,
                    $"Semantic review provider '{request.Provider}' is not registered."))
        ]);
}

internal static class SolidReviewCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "solid-review",
        false,
        true,
        "analyze",
        WhenToUse:
        "Use after rt analyze solid when deterministic SOLID candidates need bounded advisory semantic ranking. Preview is the default; source leaves the machine only with --transmit-source and repository trust.",
        Examples:
        [
            "rt analyze solid-review --principle all --solution RoslynTools.slnx --trust-repository --json",
            "rt analyze solid-review --principle DIP --transmit-source --solution RoslynTools.slnx --trust-repository --json"
        ],
        KnownIssues:
        [
            "ISP candidates remain uncertain when exact consumer/member usage cannot be resolved."
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SolidReviewRequest),
        typeof(SolidReviewResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        [],
        product: new CommandProductMetadata(
            CapabilityMaturity.Experimental,
            CommandEffects.RepositoryRead | CommandEffects.ExternalWrite,
            ConditionallyMutable: false,
            CommandIdempotency.Unknown,
            CommandAudience.Public,
            TrustRequirement.ExplicitTrustedRepository,
            WorkspaceRequirement.Semantic,
            [".NET SDK", "Loadable C# solution or project", "TYPESAFE_API_KEY for live review"],
            CommandCost.High,
            CancellationSupport.Cooperative,
            new CommandResourceLimits(30_000, 100, 1_048_576),
            Info.KnownIssues ?? [],
            Info.Examples));
}
