using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record CiCheckRequest(
    string? Solution = null,
    [property: CommandPropertyDescription("Checked-in CI policy path relative to the solution directory")]
    string Config = ".rt/ci.json");

internal sealed record CiFinding(string RuleId, string File, int Line, string Message);
internal sealed record CiRuleResult(string RuleId, int FindingCount);
internal sealed record CiCheckResult(
    string ConfigPath,
    int CheckedProjectCount,
    bool Passed,
    int FindingCount,
    IReadOnlyList<CiRuleResult> Rules,
    IReadOnlyList<CiFinding> Findings);

internal sealed class CiCheckOperation(IWorkspaceSessionFactory sessions)
    : CommandApplicationPort<CiCheckRequest, CiCheckResult>, IRtService
{
    private const string ArchitectureRuleId = "architecture";
    private const string ComplexityRuleId = "maintainability.complexity";
    private const string LargeTypeRuleId = "maintainability.large-type";
    private const string LongMethodRuleId = "maintainability.long-method";
    private const string TooManyDependenciesRuleId = "maintainability.too-many-dependencies";

    private static readonly string[] SupportedRuleIds =
    [
        ArchitectureRuleId,
        ComplexityRuleId,
        LargeTypeRuleId,
        LongMethodRuleId,
        TooManyDependenciesRuleId
    ];

    private readonly IWorkspaceSessionFactory _sessions = sessions;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public override ApplicationCommandDefinition Definition => CiCheckCommandDefinition.Definition;

    public override async Task<Result<CiCheckResult, RtError>> ExecuteAsync(
        CiCheckRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Config))
            return Failure("CI configuration path cannot be empty.");

        try
        {
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
            using (workspace)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (workspace is MSBuildWorkspace msbuild
                    && msbuild.Diagnostics.Any(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure))
                    return Failure("CI checks require a complete workspace. Resolve the MSBuild workspace failures and retry with --show-workspace-warnings.");
                var root = Path.GetDirectoryName(Path.GetFullPath(
                    solution.FilePath ?? request.Solution
                    ?? throw new InvalidOperationException("CI checks require a solution path.")))!;
                var configPath = ResolvePath(root, request.Config);
                await using var stream = File.OpenRead(configPath);
                var policy = await JsonSerializer.DeserializeAsync<CiPolicy>(stream, JsonOptions, cancellationToken);
                var error = Validate(policy);
                if (error is not null)
                    return Failure(error);

                var projects = solution.Projects.ToArray();
                if (projects.Length == 0)
                    return Failure("CI checks require at least one loaded C# project.");
                foreach (var project in projects)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (project.Language != LanguageNames.CSharp || !project.Documents.Any())
                        return Failure($"CI checks require C# projects with source documents; '{project.Name}' cannot be checked.");
                    var compilation = await project.GetCompilationAsync(cancellationToken);
                    var diagnostic = compilation?.GetDiagnostics(cancellationToken)
                        .FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
                    if (compilation is null || diagnostic is not null)
                        return Failure($"CI checks could not complete for '{project.Name}': {diagnostic?.ToString() ?? "compilation unavailable"}");
                }

                var configuredSmells = await AnalyzeConfiguredSmellsAsync(
                    solution,
                    policy!.Rules!,
                    cancellationToken);
                var findings = new List<CiFinding>();
                var rules = new List<CiRuleResult>();
                foreach (var rule in policy.Rules!)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var before = findings.Count;
                    switch (rule!.Id)
                    {
                        case ArchitectureRuleId:
                        {
                            var architecture = await ArchitectureCheckService.CheckAsync(
                                solution,
                                new ArchitectureCheckRequest(request.Solution, rule.Config!),
                                cancellationToken);
                            RtError? architectureError = null;
                            architecture.Match(
                                value => findings.AddRange(value.Violations.Select(violation => new CiFinding(
                                    "architecture." + violation.Rule,
                                    violation.File,
                                    violation.Line,
                                    $"{violation.Project}: {violation.Dependency} ({violation.BaselineState})"))),
                                failure => architectureError = failure);
                            if (architectureError is not null)
                                return Result<CiCheckResult, RtError>.Err(architectureError);
                            break;
                        }
                        case ComplexityRuleId:
                        {
                            var maximum = rule.MaxCyclomatic!.Value;
                            var report = await ComplexityAnalyzer.AnalyzeAsync(
                                solution,
                                cancellationToken: cancellationToken);
                            findings.AddRange(report.Methods
                                .Where(method => method.Cyclomatic > maximum)
                                .Select(method => new CiFinding(
                                    rule.Id,
                                    RelativePath(root, method.File),
                                    method.Line,
                                    $"{method.Method}: cyclomatic complexity {method.Cyclomatic} exceeds maximum {maximum}.")));
                            break;
                        }
                        case LargeTypeRuleId:
                            AddSmellFindings(
                                findings,
                                configuredSmells,
                                "LargeClass",
                                rule.Id,
                                root,
                                rule.MaxLines!.Value);
                            break;
                        case LongMethodRuleId:
                            AddSmellFindings(
                                findings,
                                configuredSmells,
                                "LongMethod",
                                rule.Id,
                                root,
                                rule.MaxLines!.Value);
                            break;
                        case TooManyDependenciesRuleId:
                            AddSmellFindings(
                                findings,
                                configuredSmells,
                                "TooManyDependencies",
                                rule.Id,
                                root,
                                rule.MaxParameters!.Value);
                            break;
                    }
                    rules.Add(new CiRuleResult(rule.Id!, findings.Count - before));
                }

                var ordered = findings.OrderBy(item => item.RuleId, StringComparer.Ordinal)
                    .ThenBy(item => item.File, StringComparer.Ordinal)
                    .ThenBy(item => item.Line)
                    .ThenBy(item => item.Message, StringComparer.Ordinal).ToArray();
                return Result<CiCheckResult, RtError>.Ok(new CiCheckResult(
                    Path.GetRelativePath(root, configPath).Replace('\\', '/'),
                    projects.Length,
                    ordered.Length == 0,
                    ordered.Length,
                    rules.OrderBy(rule => rule.RuleId, StringComparer.Ordinal).ToArray(),
                    ordered));
            }
        }
        catch (FileNotFoundException exception)
        {
            return Result<CiCheckResult, RtError>.Err(RtError.NotFound(exception.Message));
        }
        catch (Exception exception) when (exception is not RepositoryTrustRequiredException
                                          && exception is IOException or UnauthorizedAccessException
                                              or JsonException or InvalidOperationException or ArgumentException)
        {
            return Failure(exception.Message);
        }
    }

    private static async Task<IReadOnlyList<SmellResult>> AnalyzeConfiguredSmellsAsync(
        Solution solution,
        IReadOnlyCollection<CiRule?> rules,
        CancellationToken cancellationToken)
    {
        var largeType = rules.FirstOrDefault(rule => rule?.Id == LargeTypeRuleId);
        var longMethod = rules.FirstOrDefault(rule => rule?.Id == LongMethodRuleId);
        var tooManyDependencies = rules.FirstOrDefault(
            rule => rule?.Id == TooManyDependenciesRuleId);
        if (largeType is null && longMethod is null && tooManyDependencies is null)
            return [];

        cancellationToken.ThrowIfCancellationRequested();
        var results = await SmellAnalyzer.AnalyzeAsync(
            solution,
            largeType?.MaxLines ?? int.MaxValue,
            longMethod?.MaxLines ?? int.MaxValue,
            tooManyDependencies?.MaxParameters ?? int.MaxValue,
            scope: null);
        cancellationToken.ThrowIfCancellationRequested();
        return results;
    }

    private static void AddSmellFindings(
        List<CiFinding> findings,
        IEnumerable<SmellResult> smells,
        string smellType,
        string ruleId,
        string root,
        int maximum)
    {
        findings.AddRange(smells
            .Where(smell => smell.SmellType == smellType)
            .Select(smell => new CiFinding(
                ruleId,
                RelativePath(root, smell.FilePath),
                smell.Line,
                $"{SmellSubject(smell)}: {NormalizeDetail(smell.Detail)} exceeds maximum {maximum}.")));
    }

    private static string SmellSubject(SmellResult smell) => smell.MemberName is null
        ? smell.TypeName
        : $"{smell.TypeName}.{smell.MemberName}";

    private static string NormalizeDetail(string detail) =>
        detail.Length >= 2 && detail[0] == '(' && detail[^1] == ')'
            ? detail[1..^1]
            : detail;

    private static string RelativePath(string root, string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : Path.GetRelativePath(root, path).Replace('\\', '/');

    private static Result<CiCheckResult, RtError> Failure(string message) =>
        Result<CiCheckResult, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message));

    private static string? Validate(CiPolicy? policy)
    {
        if (policy?.Version != 1)
            return "CI policy version must be 1.";
        if (policy.Rules is not { Length: > 0 })
            return "CI policy must contain at least one rule.";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in policy.Rules)
        {
            if (rule?.Id is null || !SupportedRuleIds.Contains(rule.Id, StringComparer.Ordinal))
                return $"Unknown CI rule '{rule?.Id}'. Supported rules: {string.Join(", ", SupportedRuleIds)}.";
            if (!seen.Add(rule.Id))
                return $"Duplicate CI rule '{rule.Id}'.";

            switch (rule.Id)
            {
                case ArchitectureRuleId:
                    if (string.IsNullOrWhiteSpace(rule.Config)
                        || rule.MaxCyclomatic is not null
                        || rule.MaxLines is not null
                        || rule.MaxParameters is not null)
                        return "The architecture rule requires config and does not accept maintainability thresholds.";
                    break;
                case ComplexityRuleId:
                    if (rule.MaxCyclomatic is null or < 1
                        || rule.Config is not null
                        || rule.MaxLines is not null
                        || rule.MaxParameters is not null)
                        return "The maintainability.complexity rule requires maxCyclomatic >= 1 and does not accept other settings.";
                    break;
                case LargeTypeRuleId:
                case LongMethodRuleId:
                    if (rule.MaxLines is null or < 1
                        || rule.Config is not null
                        || rule.MaxCyclomatic is not null
                        || rule.MaxParameters is not null)
                        return $"The {rule.Id} rule requires maxLines >= 1 and does not accept other settings.";
                    break;
                case TooManyDependenciesRuleId:
                    if (rule.MaxParameters is null or < 1
                        || rule.Config is not null
                        || rule.MaxCyclomatic is not null
                        || rule.MaxLines is not null)
                        return "The maintainability.too-many-dependencies rule requires maxParameters >= 1 and does not accept other settings.";
                    break;
            }
        }
        return null;
    }

    private static string ResolvePath(string root, string value)
    {
        var fullPath = Path.GetFullPath(value, root);
        var relative = Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                             || Path.IsPathFullyQualified(relative))
            throw new InvalidOperationException($"CI configuration '{value}' is outside solution directory '{root}'.");
        return fullPath;
    }

    private sealed record CiPolicy(int Version, CiRule?[]? Rules);
    private sealed record CiRule(
        string? Id,
        string? Config,
        int? MaxCyclomatic,
        int? MaxLines,
        int? MaxParameters);
}

internal static class CiCheckCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "check",
        false,
        true,
        "ci",
        "Use this when a CI pipeline must enforce checked-in architecture and maintainability rules and fail on violations or incomplete checks.",
        ["rt ci check --solution App.slnx --config .rt/ci.json --json --trust-repository"],
        KnownIssues:
        [
            "Supported rule families are architecture, maintainability.complexity, maintainability.large-type, maintainability.long-method, and maintainability.too-many-dependencies. Baselines remain visible and fail enforcement. Maintainability rules reuse the existing analyzers and their generated-code behavior."
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Yaml, OutputFormat.Llm]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(CiCheckRequest),
        typeof(CiCheckResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReadOnly,
            effects: CommandEffects.RepositoryRead | CommandEffects.RepositoryCodeExecution,
            workspaceRequirement: WorkspaceRequirement.CompleteSemantic));
}
