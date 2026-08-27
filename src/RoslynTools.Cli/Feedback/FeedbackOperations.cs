using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Services.GitHub;

namespace RoslynTools.Feedback;

internal sealed record FeedbackAddRequest(
    [property: JsonRequired] string? Description = null,
    string? SampleCode = null,
    string? ExpectedResult = null,
    string? CommandLine = null);

internal sealed record FeedbackAddResult(string Id, string Status);

internal sealed class FeedbackAddOperation
    : LegacyCommandApplicationPort<FeedbackAddRequest, FeedbackAddResult>, IRtService
{
    private readonly Func<string, string?, string?, string?, FeedbackEntry> _add;

    public FeedbackAddOperation()
        : this(FeedbackStore.AddUserFeedback)
    {
    }

    internal FeedbackAddOperation(
        Func<string, string?, string?, string?, FeedbackEntry> add)
    {
        _add = add;
    }

    public override string CommandName => "feedback_add";

    public override Task<FeedbackAddResult> ExecuteAsync(
        FeedbackAddRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Description is required.");

        var entry = _add(
            request.Description,
            request.SampleCode,
            request.ExpectedResult,
            request.CommandLine);
        return Task.FromResult(new FeedbackAddResult(entry.Id, entry.Status));
    }
}

internal sealed record FeedbackListRequest(string? Status = "pending");

internal sealed class FeedbackListOperation
    : LegacyCommandApplicationPort<FeedbackListRequest, List<FeedbackEntry>>, IRtService
{
    private readonly Func<string, List<FeedbackEntry>> _list;

    public FeedbackListOperation()
        : this(FeedbackStore.ListByStatus)
    {
    }

    internal FeedbackListOperation(Func<string, List<FeedbackEntry>> list)
    {
        _list = list;
    }

    public override string CommandName => "feedback_list";

    public override Task<List<FeedbackEntry>> ExecuteAsync(
        FeedbackListRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_list(request.Status ?? "pending"));
    }
}

internal sealed record FeedbackProcessRequest(
    [property: JsonRequired] string? Id = null,
    [property: JsonRequired] string? Resolution = null);

internal sealed record FeedbackProcessResult(
    string Id,
    string Status,
    string ProcessedAt,
    string Resolution);

internal sealed class FeedbackProcessOperation
    : LegacyCommandApplicationPort<FeedbackProcessRequest, FeedbackProcessResult>, IRtService
{
    private readonly Func<string, string, FeedbackEntry> _process;

    public FeedbackProcessOperation()
        : this(FeedbackStore.MarkProcessed)
    {
    }

    internal FeedbackProcessOperation(Func<string, string, FeedbackEntry> process)
    {
        _process = process;
    }

    public override string CommandName => "feedback_process";

    public override Task<FeedbackProcessResult> ExecuteAsync(
        FeedbackProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Id))
            throw new ArgumentException("Feedback ID is required.");
        if (string.IsNullOrWhiteSpace(request.Resolution))
            throw new ArgumentException("Resolution is required.");

        var entry = _process(request.Id, request.Resolution);
        return Task.FromResult(new FeedbackProcessResult(
            entry.Id,
            entry.Status,
            entry.ProcessedAt!,
            entry.Resolution!));
    }
}

internal sealed record FeedbackReportRequest(
    [property: JsonRequired] string? Id = null,
    bool Yes = false,
    int? AttachTo = null,
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

internal sealed record FeedbackReportIssueCandidate(
    int Number,
    string Title,
    string Url,
    double Score);

/// <summary>
///     Status is reported, attached, duplicates-found, confirmation-required,
///     already-reported, or dry-run.
/// </summary>
internal sealed record FeedbackReportResult(
    string Id,
    string Status,
    int? IssueNumber,
    string? IssueUrl,
    IReadOnlyList<FeedbackReportIssueCandidate> PossibleDuplicates);

/// <summary>Promotes one local feedback entry to a GitHub issue through a typed port.</summary>
internal sealed class FeedbackReportOperation : LegacyCommandApplicationPort, IRtService
{
    private const int SearchLimit = 20;
    private const int MaxCandidates = 5;

    private readonly Func<string, FeedbackEntry?> _getById;
    private readonly IGitHubIssueClient _gitHub;
    private readonly Func<string, int, string, FeedbackEntry> _markReported;

    public FeedbackReportOperation(IGitHubIssueClient gitHub)
        : this(FeedbackStore.TryGetById, gitHub, FeedbackStore.MarkReported)
    {
    }

    internal FeedbackReportOperation(
        Func<string, FeedbackEntry?> getById,
        IGitHubIssueClient gitHub,
        Func<string, int, string, FeedbackEntry> markReported)
    {
        _getById = getById;
        _gitHub = gitHub;
        _markReported = markReported;
    }

    public override string CommandName => "feedback_report";
    public override Type RequestType => typeof(FeedbackReportRequest);
    public override Type ResponseType => typeof(FeedbackReportResult);

    internal async Task<Result<FeedbackReportResult, RtError>> ExecuteAsync(
        FeedbackReportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Id))
            return Result<FeedbackReportResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Feedback id is required."));
        if (request.AttachTo <= 0)
            return Result<FeedbackReportResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Attached issue number must be positive."));
        if (!request.DryRun && request.Yes && request.AttachTo.HasValue)
            return Result<FeedbackReportResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Use either --yes or --attach-to, not both."));

        var entry = _getById(request.Id);
        if (entry is null)
            return Result<FeedbackReportResult, RtError>.Err(
                RtError.NotFound($"Feedback item '{request.Id}' was not found."));

        if (entry.GitHubIssueNumber is { } existingNumber)
            return Result<FeedbackReportResult, RtError>.Ok(new FeedbackReportResult(
                entry.Id,
                "already-reported",
                existingNumber,
                entry.GitHubIssueUrl,
                []));

        if (string.IsNullOrWhiteSpace(entry.Description))
            return Result<FeedbackReportResult, RtError>.Err(
                new RtError(
                    RtErrorKind.InvalidInput,
                    $"Feedback item '{entry.Id}' has no description and cannot be reported."));

        if (!await _gitHub.IsAvailableAsync(cancellationToken))
            return Result<FeedbackReportResult, RtError>.Err(
                RtError.ExternalToolFailure(
                    "GitHub CLI (gh) is not installed or authenticated. Run `gh auth login`, then retry. "
                    + "The feedback remains recorded locally."));

        try
        {
            var result = request.AttachTo.HasValue
                ? await AttachAsync(entry, request, cancellationToken)
                : await ReportAsync(entry, request, cancellationToken);
            return Result<FeedbackReportResult, RtError>.Ok(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidOperationException exception)
        {
            return Result<FeedbackReportResult, RtError>.Err(
                RtError.ExternalToolFailure(exception.Message));
        }
    }

    protected override async Task<CommandPortResult> InvokeLegacyAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not FeedbackReportRequest typedRequest)
            throw new ArgumentException("Invalid feedback report request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            CommandPortResult.Success,
            error => CommandPortResult.Failure(error, error.Message));
    }

    private async Task<FeedbackReportResult> AttachAsync(
        FeedbackEntry entry,
        FeedbackReportRequest request,
        CancellationToken cancellationToken)
    {
        var target = await _gitHub.GetIssueAsync(request.AttachTo!.Value, cancellationToken);
        if (request.DryRun)
            return new FeedbackReportResult(entry.Id, "dry-run", target.Number, target.Url, []);

        var linked = _markReported(entry.Id, target.Number, target.Url);
        return new FeedbackReportResult(linked.Id, "attached", target.Number, target.Url, []);
    }

    private async Task<FeedbackReportResult> ReportAsync(
        FeedbackEntry entry,
        FeedbackReportRequest request,
        CancellationToken cancellationToken)
    {
        var issues = await _gitHub.SearchAsync(
            BuildSearchQuery(entry),
            SearchLimit,
            cancellationToken);
        var candidates = IssueDuplicateScorer
            .Score(issues, entry.Description, entry.CommandLine, MaxCandidates)
            .Select(result => new FeedbackReportIssueCandidate(
                result.Issue.Number,
                result.Issue.Title,
                result.Issue.Url,
                result.Score))
            .ToList();

        if (request.DryRun)
            return new FeedbackReportResult(entry.Id, "dry-run", null, null, candidates);
        if (!request.Yes)
            return new FeedbackReportResult(
                entry.Id,
                candidates.Count > 0 ? "duplicates-found" : "confirmation-required",
                null,
                null,
                candidates);

        var created = await _gitHub.CreateIssueAsync(
            BuildIssueTitle(entry),
            RenderIssueBody(entry),
            cancellationToken);
        var reported = _markReported(entry.Id, created.Number, created.Url);
        return new FeedbackReportResult(
            reported.Id,
            "reported",
            created.Number,
            created.Url,
            candidates);
    }

    private static string BuildSearchQuery(FeedbackEntry entry) =>
        string.Join(' ', new[] { entry.Description, entry.CommandLine }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static string BuildIssueTitle(FeedbackEntry entry)
    {
        var firstLine = entry.Description
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault()?.Trim() ?? "rt feedback";
        var compact = string.Join(' ', firstLine
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (compact.Length > 100)
            compact = string.Concat(compact.AsSpan(0, 97), "...");
        return entry.Type == "exception" ? $"Bug: {compact}" : $"Feedback: {compact}";
    }

    private static string RenderIssueBody(FeedbackEntry entry)
    {
        var body = new StringBuilder();
        body.AppendLine(CultureInfo.InvariantCulture, $"**Type:** {entry.Type}");
        body.AppendLine();
        body.AppendLine("**Description**");
        body.AppendLine();
        body.AppendLine(entry.Description);
        AppendSection(body, "Command line", entry.CommandLine, "text");
        AppendSection(body, "Sample code", entry.SampleCode, "csharp");
        AppendSection(body, "Expected result", entry.ExpectedResult, null);
        AppendSection(body, "Actual result", entry.ActualResult, null);
        body.AppendLine();
        body.AppendLine("**Environment**");
        body.AppendLine(CultureInfo.InvariantCulture, $"- rt version: {entry.RtVersion ?? "unknown"}");
        body.AppendLine(CultureInfo.InvariantCulture, $"- .NET version: {entry.DotnetVersion ?? "unknown"}");
        body.AppendLine(CultureInfo.InvariantCulture, $"- OS: {entry.OsPlatform ?? "unknown"}");
        body.AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture, $"_Originally captured locally as `{entry.Id}`._");
        return body.ToString();
    }

    private static void AppendSection(
        StringBuilder body,
        string heading,
        string? content,
        string? codeFence)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        body.AppendLine();
        body.AppendLine(CultureInfo.InvariantCulture, $"**{heading}**");
        if (codeFence is null)
        {
            body.AppendLine(content);
            return;
        }

        body.AppendLine(CultureInfo.InvariantCulture, $"```{codeFence}");
        body.AppendLine(content);
        body.AppendLine("```");
    }
}
