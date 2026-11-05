using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services.SemanticReview;

internal sealed record CodeSmellsRequest(
    string? Solution = null,
    string? Config = null,
    string[]? RuleIds = null,
    string Provider = "typesafe",
    string Model = "jev-latest",
    int MaxCandidates = 20,
    bool DryRun = false,
    bool TransmitSource = false);

internal sealed record CodeSmellLocation(string FilePath, string Symbol, int StartLine, int? EndLine);
internal sealed record CodeSmellRaw(string CandidateId, string RuleId, CodeSmellLocation Location, string Detail);
internal sealed record CodeSmellPlan(string CandidateId, string RuleId, string Disposition, string? Reason, int SourceBytes, bool EvidenceComplete);
internal sealed record CodeSmellFailure(string? CandidateId, string? RuleId, string Kind, string Message, bool Retryable, int? StatusCode);
internal sealed record CodeSmellsResult(
    string SchemaVersion, string Outcome, bool DryRun, string CatalogVersion, string CatalogHash,
    string RepositoryRevision, int EstimatedRequestCount, JsonArray Rules,
    IReadOnlyList<CodeSmellRaw> Raw, IReadOnlyList<CodeSmellPlan> Plan,
    IReadOnlyList<JsonObject> Findings, IReadOnlyList<CodeSmellFailure> Failures,
    IReadOnlyList<string> Limitations, string Provider, string RequestedModel, string QuestionSetVersion,
    JsonNode ResponseSchema, JsonObject Catalog);

internal sealed class CodeSmellsOperation(IReadOnlyWorkspaceSessions sessions, IEnumerable<ISemanticReviewProvider> providers)
    : CommandApplicationPort<CodeSmellsRequest, CodeSmellsResult>, IRtService
{
    public override ApplicationCommandDefinition Definition => CodeSmellsCommandDefinition.Definition;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private sealed record Candidate(JsonNode Rule, CodeSmellRaw Raw, string Source, Dictionary<string, string> Evidence, bool Complete, string? Skip = null);

    public override async Task<Result<CodeSmellsResult, RtError>> ExecuteAsync(CodeSmellsRequest request, CancellationToken cancellationToken = default)
    {
        string path = request.Config ?? "code-smells.json";
        try
        {
            if (request.MaxCandidates is < 1 or > 100 || request.DryRun && request.TransmitSource
                || string.IsNullOrWhiteSpace(request.Provider) || string.IsNullOrWhiteSpace(request.Model))
                throw new ArgumentException("Require maxCandidates 1–100, a provider/model, and mutually exclusive dryRun/transmitSource.");
            var selectedSolution = SolutionPathResolver.Resolve(request.Solution);
            var repository = RepositoryRoot(selectedSolution);
            path = Path.GetFullPath(request.Config ?? "code-smells.json", repository);
            var catalog = await CodeSmellCatalog.ReadAsync(repository, request.Config, cancellationToken);
            var rules = catalog.Rules.Select(rule => rule!).ToArray();
            if (request.RuleIds is not null && request.RuleIds.Any(id => !rules.Any(rule => Text(rule, "id") == id)))
                throw new ArgumentException("Unknown selected rule ID.");
            using var lease = await sessions.AcquireAsync(selectedSolution, cancellationToken);
            var complete = lease.CompletenessEvidence?.State == WorkspaceCompletenessState.Complete;
            var limitations = complete ? Array.Empty<string>() : new[] { "Workspace completeness is unproven; all assessments remain uncertain." };
            var candidates = new List<Candidate>();
            foreach (var rule in rules.Where(rule => rule["enabled"]!.GetValue<bool>()
                && (request.RuleIds is null || request.RuleIds.Contains(Text(rule, "id")))))
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.AddRange(await DetectAsync(lease.Solution, rule, repository, cancellationToken));
            }
            var ordered = candidates.Select(c => c with { Complete = c.Complete && complete }).OrderBy(c => Text(c.Rule, "id"), StringComparer.Ordinal)
                .ThenBy(c => c.Raw.Location.FilePath, StringComparer.Ordinal)
                .ThenBy(c => c.Raw.Location.Symbol, StringComparer.Ordinal)
                .ThenBy(c => c.Raw.Location.StartLine).ToArray();
            var plans = new List<CodeSmellPlan>();
            var findings = new List<JsonObject>();
            var failures = new List<CodeSmellFailure>();
            var count = 0;
            var provider = providers.FirstOrDefault(p => p.Name.Equals(request.Provider, StringComparison.OrdinalIgnoreCase));
            var questionVersion = $"code-smells:{catalog.Version}:{catalog.Hash}";
            foreach (var candidate in ordered)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = Encoding.UTF8.GetByteCount(candidate.Source);
                var reason = candidate.Skip;
                var disposition = reason is not null ? "skipped" : bytes > 16384 || Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(candidate.Evidence)) > 16384 ? "skipped" : count >= request.MaxCandidates ? "beyond_limit" : "eligible";
                if (disposition == "skipped" && reason is null) reason = "Source or evidence exceeds 16 KiB.";
                if (disposition == "beyond_limit") reason = "Global candidate limit reached.";
                plans.Add(new(candidate.Raw.CandidateId, candidate.Raw.RuleId, disposition, reason, bytes, candidate.Complete));
                if (disposition != "eligible") continue;
                count++;
                if (!request.TransmitSource) continue;
                var exceptions = candidate.Rule["review"]!["acceptedExceptions"]!.AsArray()
                    .ToDictionary(e => Text(e!, "id"), e => Text(e!, "description"), StringComparer.Ordinal);
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Source + JsonSerializer.Serialize(candidate.Evidence)))).ToLowerInvariant();
                var providerRequest = new SemanticReviewProviderRequest(
                    Text(candidate.Rule["detection"]!, "selector"), candidate.Raw.Location.Symbol,
                    candidate.Raw.Location.FilePath, candidate.Raw.Location.StartLine, candidate.Source,
                    candidate.Evidence, candidate.Complete, lease.SourceRevision, hash, request.Model, questionVersion,
                    Text(candidate.Rule["review"]!, "question"), Text(candidate.Rule, "description"), exceptions,
                    candidate.Rule["review"]!["evidenceGuidance"]!.AsArray().Select(e => e!.GetValue<string>()).ToArray());
                var response = provider is null
                    ? Result<SemanticReviewProviderResponse, SemanticReviewFailure>.Err(new(SemanticReviewFailureKind.UnknownProvider, "Provider is not registered."))
                    : await provider.ReviewAsync(providerRequest, cancellationToken);
                response.Match(value =>
                {
                    if (ValidResponse(value, exceptions.Keys))
                        findings.Add(Finding(candidate, value, catalog.Document["reviewPolicy"]!, hash, lease.SourceRevision));
                    else
                        failures.Add(new(candidate.Raw.CandidateId, candidate.Raw.RuleId, "MalformedResponse",
                            "Provider response does not match the effective rule choices or probability contract.", false, null));
                }, failure => failures.Add(new(candidate.Raw.CandidateId, candidate.Raw.RuleId, failure.Kind.ToString(), failure.Message, failure.Retryable, failure.StatusCode)));
            }
            var outcome = !request.TransmitSource ? "Planned" : failures.Count == 0 ? "Complete" : findings.Count == 0 ? "Failed" : "Partial";
            return Result<CodeSmellsResult, RtError>.Ok(new("rt.code-smells.v1", outcome, !request.TransmitSource,
                catalog.Version, catalog.Hash, lease.SourceRevision, count, catalog.Rules,
                ordered.Select(c => c.Raw).ToArray(), plans, findings, failures, limitations, request.Provider, request.Model,
                questionVersion, catalog.Document["responseSchema"]!.DeepClone(), (JsonObject)catalog.Document.DeepClone()));
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or FormatException or OverflowException)
        {
            return Result<CodeSmellsResult, RtError>.Err(new(RtErrorKind.InvalidInput, $"Catalogue '{path}': {exception.Message}"));
        }
    }

    private static string RepositoryRoot(string solution)
    {
        var selected = Path.GetFullPath(solution);
        var directory = new DirectoryInfo(Directory.Exists(selected) ? selected : Path.GetDirectoryName(selected)!);
        // Virtual workspace selections have no on-disk solution from which to establish Git ownership.
        if (!File.Exists(selected) && !Directory.Exists(selected)) return directory.FullName;
        for (var current = directory; current is not null; current = current.Parent)
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) || File.Exists(Path.Combine(current.FullName, ".git"))) return current.FullName;
        return directory.FullName;
    }

    private static string Text(JsonNode node, string property) => node[property]!.GetValue<string>();
    private static int Threshold(JsonNode rule, string key, int fallback) => rule["detection"]!["thresholds"]?[key]?.GetValue<int>() ?? fallback;

    private static async Task<IReadOnlyList<Candidate>> DetectAsync(Solution solution, JsonNode rule, string repository, CancellationToken token)
    {
        var selector = Text(rule["detection"]!, "selector");
        var id = Text(rule, "id");
        var result = new List<Candidate>();
        if (selector is "SRP" or "ISP" or "DIP" or "OCP" or "LSP")
        {
            var raw = selector switch
            {
                "SRP" => new SolidResult { SRP = await SolidOperation.AnalyzeSrpAsync(solution, Threshold(rule, "minMethods", 10), Threshold(rule, "minDeps", 5)) },
                "ISP" => new SolidResult { ISP = await SolidOperation.AnalyzeIspAsync(solution, Threshold(rule, "minMembers", 7)) },
                "DIP" => new SolidResult { DIP = await SolidOperation.AnalyzeDipAsync(solution) },
                "OCP" => new SolidResult { OCP = await SolidOperation.AnalyzeOcpAsync(solution, Threshold(rule, "minBranches", 2)) },
                _ => new SolidResult { LSP = await SolidOperation.AnalyzeLspAsync(solution) }
            };
            var prepared = await SolidReviewCandidateBuilder.BuildAsync(solution, raw, int.MaxValue, token);
            foreach (var item in prepared)
            {
                var p = item.Plan;
                result.Add(new(rule, new(id + ":" + p.CandidateId, id, new(p.FilePath, p.Symbol, p.Line, null), selector),
                    item.SanitizedSource, new(p.RawEvidence), p.EvidenceComplete, p.Disposition == "eligible" ? null : p.Reason ?? p.Disposition));
            }
            return result;
        }
        var ignore = RtIgnore.ForSolution(solution);
        foreach (var document in solution.Projects.SelectMany(p => p.Documents))
        {
            token.ThrowIfCancellationRequested();
            if (ignore.IsIgnored(document.FilePath)) continue;
            var root = await document.GetSyntaxRootAsync(token);
            if (root is null) continue;
            if (root.GetLeadingTrivia().ToFullString().Contains("<auto-generated", StringComparison.OrdinalIgnoreCase)
                || document.FilePath?.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) == true
                || document.FilePath?.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) == true) continue;
            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>().Where(t => t is ClassDeclarationSyntax or RecordDeclarationSyntax))
            {
                IEnumerable<SmellResult> detected = selector switch
                {
                    "LargeClass" => LargeClassRule.Check(type, document.FilePath, Threshold(rule, "maxClassLines", 300)) is { } large ? [large] : [],
                    "LongMethod" => LongMethodRule.Check(type, document.FilePath, Threshold(rule, "maxMethodLines", 40)),
                    "TooManyDependencies" => TooManyDependenciesRule.Check(type, document.FilePath, Threshold(rule, "maxParams", 7)) is { } deps ? [deps] : [],
                    "class" => [new(selector, type.Identifier.Text, null, document.FilePath, type.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "Bounded semantic declaration")],
                    "method" => type.Members.OfType<MethodDeclarationSyntax>().Select(m => new SmellResult(selector, type.Identifier.Text, m.Identifier.Text, document.FilePath, m.GetLocation().GetLineSpan().StartLinePosition.Line + 1, "Bounded semantic declaration")),
                    _ => []
                };
                foreach (var item in detected)
                {
                    SyntaxNode declaration = item.MemberName is null ? type : type.Members.OfType<MethodDeclarationSyntax>().FirstOrDefault(m => m.GetLocation().GetLineSpan().StartLinePosition.Line + 1 == item.Line) ?? (SyntaxNode)type;
                    var location = new CodeSmellLocation(Path.GetRelativePath(repository, document.FilePath ?? "unknown.cs").Replace('\\', '/'), item.TypeName + (item.MemberName is null ? "" : "." + item.MemberName), item.Line, declaration.GetLocation().GetLineSpan().EndLinePosition.Line + 1);
                    var candidateId = $"{id}:{location.FilePath}:{location.Symbol}:{item.Line}";
                    result.Add(new(rule, new(candidateId, id, location, item.Detail), CandidateSourceSanitizer.Sanitize(declaration.ToFullString()), new() { ["detail"] = item.Detail, ["selector"] = selector }, true));
                }
            }
        }
        return result;
    }

    private static bool ValidResponse(SemanticReviewProviderResponse response, IEnumerable<string> exceptionIds)
    {
        static bool Probability(double value) => double.IsFinite(value) && value is >= 0 and <= 1;
        var allowed = exceptionIds.Append("no_accepted_exception").ToHashSet(StringComparer.Ordinal);
        return SemanticReviewClassifications.All.Contains(response.Classification)
            && response.Probabilities.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(SemanticReviewClassifications.All)
            && response.Probabilities.Values.All(Probability)
            && Probability(response.Confidence) && Probability(response.MeaningfulDesignProblem)
            && Probability(response.AcceptedException) && Probability(response.FalsePositiveReasonConfidence)
            && allowed.Contains(response.FalsePositiveReason)
            && response.FalsePositiveReasonProbabilities.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(allowed)
            && response.FalsePositiveReasonProbabilities.Values.All(Probability)
            && !string.IsNullOrWhiteSpace(response.RequestId) && !string.IsNullOrWhiteSpace(response.ResolvedModel);
    }

    private static JsonObject Finding(Candidate candidate, SemanticReviewProviderResponse value, JsonNode policy, string hash, string repositoryRevision)
    {
        var decision = SolidReviewPolicy.Evaluate(value.Classification, value.Probabilities.GetValueOrDefault(value.Classification),
            candidate.Complete, value.MeaningfulDesignProblem, value.AcceptedException, value.FalsePositiveReason,
            value.FalsePositiveReasonProbabilities.GetValueOrDefault(value.FalsePositiveReason),
            policy["minimumAutomaticProbability"]!.GetValue<double>(), policy["maximumContradictoryProbability"]!.GetValue<double>());
        return JsonSerializer.SerializeToNode(new
        {
            candidateId = candidate.Raw.CandidateId, ruleId = candidate.Raw.RuleId,
            ruleName = Text(candidate.Rule, "name"), severity = Text(candidate.Rule, "severity"), location = candidate.Raw.Location,
            classification = decision.Classification, summary = Text(candidate.Rule, "description"),
            evidence = new[] { new { description = candidate.Raw.Detail, source = "roslyn" } },
            evidenceComplete = candidate.Complete, confidence = value.Confidence, probabilities = value.Probabilities,
            acceptedException = decision.FalsePositiveReason, policyReasons = decision.Reasons,
            remediation = candidate.Rule["remediation"]!.DeepClone(),
            provenance = new { provider = value.Provider, requestedModel = value.RequestedModel, resolvedModel = value.ResolvedModel, requestId = value.RequestId, questionSetVersion = value.QuestionSetVersion, sourceEvidenceHash = hash, repositoryRevision },
            providerClassification = value.Classification, meaningfulDesignProblem = value.MeaningfulDesignProblem,
            acceptedExceptionProbability = value.AcceptedException, providerFalsePositiveReason = value.FalsePositiveReason,
            falsePositiveReasonProbabilities = value.FalsePositiveReasonProbabilities, falsePositiveReasonConfidence = value.FalsePositiveReasonConfidence,
            rawEvidence = candidate.Evidence
        }, Json)!.AsObject();
    }
}

internal static class CodeSmellsCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new("code-smells", false, true, "analyze",
        WhenToUse: "Use instead of Read for bounded catalogue-driven advisory review. Preview is default; live review requires --transmit-source and repository trust.",
        Examples: ["rt analyze code-smells --solution RoslynTools.slnx --trust-repository --json"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
    internal static ApplicationCommandDefinition Definition { get; } = new(Info, typeof(CodeSmellsRequest), typeof(CodeSmellsResult), true,
        CommandSafety.ReadOnly, CommandExecutionPlacement.InProcess, [], product: SolidReviewCommandDefinition.Definition.Product);
}
