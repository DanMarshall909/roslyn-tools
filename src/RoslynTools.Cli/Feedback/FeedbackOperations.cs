using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Services.GitHub;

namespace RoslynTools.Feedback;

internal sealed record FeedbackAddRequest(
    [property: JsonRequired] string? Title = null,
    [property: JsonRequired] FeedbackCategory? Category = null,
    [property: JsonRequired] string? Description = null,
    string? SampleCode = null,
    string? ExpectedResult = null,
    string? CommandLine = null,
    string? Command = null);

internal sealed record FeedbackAddResult(string Id, string Status);

internal sealed class FeedbackAddOperation
    : CommandApplicationPort<FeedbackAddRequest, FeedbackAddResult>, IRtService
{
    private readonly Func<FeedbackAddRequest, FeedbackEntry> _add;

    public FeedbackAddOperation()
        : this(request => FeedbackStore.AddUserFeedback(
            request.Title!,
            request.Category!.Value,
            request.Description!,
            request.SampleCode,
            request.ExpectedResult,
            request.CommandLine,
            request.Command))
    {
    }

    internal FeedbackAddOperation(Func<FeedbackAddRequest, FeedbackEntry> add)
    {
        _add = add;
    }

    public override string CommandName => "feedback_add";

    public override Task<FeedbackAddResult> ExecuteAsync(
        FeedbackAddRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Title))
            throw new ArgumentException("Title is required.");
        if (request.Category is null)
            throw new ArgumentException(
                $"Category is required and must be one of: {string.Join(", ", FeedbackCategories.AllNames)}.");
        if (string.IsNullOrWhiteSpace(request.Description))
            throw new ArgumentException("Description is required.");

        var entry = _add(request);
        return Task.FromResult(new FeedbackAddResult(entry.Id, entry.Status));
    }
}

internal sealed record FeedbackListRequest(string? Status = "pending");

internal sealed class FeedbackListOperation
    : CommandApplicationPort<FeedbackListRequest, List<FeedbackEntry>>, IRtService
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
    : CommandApplicationPort<FeedbackProcessRequest, FeedbackProcessResult>, IRtService
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
    // JsonPropertyName("dry-run") matches the kebab-case key McpCommandAdapter.ApplySafetyPolicy
    // force-injects for DryRunByDefault descriptors (see RenameOperation for the same pattern).
    // Without it, the safety net's injected "dry-run": true silently fails to bind under the
    // camelCase policy and an MCP caller that omits the flag entirely files a real issue.
    [property: JsonPropertyName("dry-run")] bool DryRun = true);

/// <summary>A candidate existing GitHub issue that may be a duplicate of the report being filed.</summary>
internal sealed record FeedbackReportIssueCandidate(int Number, string Title, string Url, double Score);

/// <summary>
///     <c>Status</c> is one of: <c>reported</c> (a new issue was created), <c>attached</c>
///     (linked to an existing issue via <c>--attach-to</c>), <c>duplicates-found</c> (nothing
///     was created — <see cref="PossibleDuplicates" /> must be reviewed first, then re-run with
///     <c>--yes</c> or <c>--attach-to</c>), <c>already-reported</c> (this entry was filed or
///     linked by an earlier call; nothing new was created), or <c>dry-run</c>.
/// </summary>
internal sealed record FeedbackReportResult(
    string Id,
    string Status,
    int? IssueNumber,
    string? IssueUrl,
    IReadOnlyList<FeedbackReportIssueCandidate> PossibleDuplicates);

/// <summary>
///     Files (or links) a local feedback entry as a public GitHub issue via the <c>gh</c> CLI.
///     Unlike <see cref="FeedbackAddOperation" />, this is an explicit, human-directed action —
///     never call it automatically. See the <c>feedback_report</c> MCP descriptor's
///     <c>WhenToUse</c> text (<see cref="RoslynTools.CommandDescriptors.CommandDescriptorRegistry" />)
///     for the enforced wording.
/// </summary>
internal sealed class FeedbackReportOperation : ICommandApplicationPort, IRtService
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

    public string CommandName => "feedback_report";
    public Type RequestType => typeof(FeedbackReportRequest);

    internal Task<Result<FeedbackReportResult, RtError>> ExecuteAsync(
        FeedbackReportRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Id))
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Feedback id is required.")));

        var entry = _getById(request.Id);
        if (entry is null)
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Err(
                RtError.NotFound($"Feedback item '{request.Id}' was not found.")));

        // Idempotency guard: re-running --yes (or --attach-to) against an entry that was
        // already filed must not file a genuine duplicate of its own earlier report. This
        // needs no gh call at all — the prior result is already known locally.
        if (entry.GitHubIssueNumber is { } existingNumber)
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Ok(new FeedbackReportResult(
                entry.Id, "already-reported", existingNumber, entry.GitHubIssueUrl, [])));

        // A blank title means this entry predates the mandatory --title/--category schema
        // (e.g. hand-edited or captured by an older rt build) — refuse rather than silently
        // filing a blank/mis-categorized public issue titled "[bug] ".
        if (string.IsNullOrWhiteSpace(entry.Title))
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput,
                    $"Feedback item '{entry.Id}' has no title, so it predates the required schema and "
                    + "cannot be reported as-is. Use `rt feedback process` to resolve it directly, or "
                    + "`rt feedback add` to re-capture it with --title/--category.")));

        if (!_gitHub.IsAvailable())
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Err(
                RtError.ExternalToolFailure(
                    "GitHub CLI (gh) is not installed or not authenticated. Run `gh auth login`, then "
                    + $"retry `rt feedback report --id {entry.Id}`. The item remains recorded locally.")));

        try
        {
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Ok(RunReport(entry, request)));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(Result<FeedbackReportResult, RtError>.Err(
                RtError.ExternalToolFailure(ex.Message)));
        }
    }

    async Task<CommandPortResult> ICommandApplicationPort.InvokeAsync(
        object request,
        CancellationToken cancellationToken)
    {
        if (request is not FeedbackReportRequest typedRequest)
            throw new ArgumentException("Invalid feedback report request.", nameof(request));

        var result = await ExecuteAsync(typedRequest, cancellationToken);
        return result.Match(
            CommandPortResult.Success,
            error => CommandPortResult.Failure(error.Message));
    }

    private FeedbackReportResult RunReport(FeedbackEntry entry, FeedbackReportRequest request)
    {
        if (request.AttachTo.HasValue)
        {
            var target = _gitHub.GetIssue(request.AttachTo.Value);
            if (request.DryRun)
                return new FeedbackReportResult(entry.Id, "dry-run", target.Number, target.Url, []);

            var linked = _markReported(entry.Id, target.Number, target.Url);
            return new FeedbackReportResult(linked.Id, "attached", target.Number, target.Url, []);
        }

        var query = BuildDedupeQuery(entry);
        var candidates = _gitHub.Search(query, SearchLimit);
        var ranked = IssueDuplicateScorer
            .Score(candidates, entry.Category.ToText(), entry.Command, entry.Title, MaxCandidates)
            .Select(r => new FeedbackReportIssueCandidate(r.Issue.Number, r.Issue.Title, r.Issue.Url, r.Score))
            .ToList();

        // A dry run always just reports what it found, regardless of --yes.
        if (request.DryRun)
            return new FeedbackReportResult(entry.Id, "dry-run", null, null, ranked);

        // Stack-Overflow-style gate: surface possible duplicates first and create nothing
        // until the caller has seen them and explicitly overrides with --yes.
        if (ranked.Count > 0 && !request.Yes)
            return new FeedbackReportResult(entry.Id, "duplicates-found", null, null, ranked);

        var created = _gitHub.CreateIssue(BuildIssueTitle(entry), RenderIssueBody(entry));
        var reported = _markReported(entry.Id, created.Number, created.Url);
        return new FeedbackReportResult(reported.Id, "reported", created.Number, created.Url, ranked);
    }

    /// <summary>
    ///     Category and command are folded into the query (and, via
    ///     <see cref="BuildIssueTitle" />, into every filed issue's title) precisely so
    ///     <see cref="IssueDuplicateScorer" /> can weight them without depending on GitHub
    ///     labels existing ahead of time.
    /// </summary>
    private static string BuildDedupeQuery(FeedbackEntry entry) =>
        string.Join(' ', new[] { entry.Category.ToText(), entry.Command, entry.Title }
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    private static string BuildIssueTitle(FeedbackEntry entry) =>
        string.IsNullOrWhiteSpace(entry.Command)
            ? $"[{entry.Category.ToText()}] {entry.Title}"
            : $"[{entry.Category.ToText()}] [{entry.Command}] {entry.Title}";

    /// <summary>
    ///     Deliberately omits <see cref="FeedbackEntry.Cwd" />, <see cref="FeedbackEntry.SolutionPath" />,
    ///     and the raw <see cref="FeedbackEntry.StackTrace" /> — machine-specific absolute paths and
    ///     traces do not belong in a public issue body (see docs/feedback-triage.md).
    /// </summary>
    private static string RenderIssueBody(FeedbackEntry entry)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**Category:** {entry.Category.ToText()}");
        if (!string.IsNullOrWhiteSpace(entry.Command))
            sb.AppendLine(CultureInfo.InvariantCulture, $"**Command:** `{entry.Command}`");
        sb.AppendLine();
        sb.AppendLine("**Description**").AppendLine();
        sb.AppendLine(entry.Description);

        AppendSection(sb, "Command line", entry.CommandLine, "text");
        AppendSection(sb, "Sample code", entry.SampleCode, "csharp");
        AppendSection(sb, "Expected result", entry.ExpectedResult, null);
        AppendSection(sb, "Actual result", entry.ActualResult, null);

        sb.AppendLine();
        sb.AppendLine("**Environment**");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- rt version: {entry.RtVersion ?? "unknown"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- .NET version: {entry.DotnetVersion ?? "unknown"}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"- OS: {entry.OsPlatform ?? "unknown"}");
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"_Originally captured locally as `{entry.Id}`._");
        return sb.ToString();
    }

    private static void AppendSection(StringBuilder sb, string heading, string? content, string? codeFence)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;
        sb.AppendLine();
        sb.AppendLine(CultureInfo.InvariantCulture, $"**{heading}**");
        if (codeFence is null)
        {
            sb.AppendLine(content);
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"```{codeFence}");
            sb.AppendLine(content);
            sb.AppendLine("```");
        }
    }
}
