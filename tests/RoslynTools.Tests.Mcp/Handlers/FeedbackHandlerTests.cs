using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Feedback;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services.GitHub;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class FeedbackHandlerTests
{
    [Fact]
    public void feedback_tools_are_registered_for_mcp_discovery()
    {
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();

        var tools = registry.GetTools();

        tools.Select(tool => tool.name).ShouldContain("feedback_add");
        tools.Select(tool => tool.name).ShouldContain("feedback_list");
        tools.Select(tool => tool.name).ShouldContain("feedback_process");
        tools.Select(tool => tool.name).ShouldContain("feedback_report");

        var add = tools.Single(tool => tool.name == "feedback_add");
        var required = (string[])add.inputSchema["required"];
        required.ShouldBe(["title", "category", "description"], ignoreOrder: true);
        var properties =
            (Dictionary<string, Dictionary<string, object>>)add.inputSchema["properties"];
        properties.Keys.ShouldBe(
            ["title", "category", "description", "sampleCode", "expectedResult", "commandLine", "command"],
            ignoreOrder: true);
        // FeedbackCategory is a real enum, not free text — its allowed values must be
        // discoverable in the schema itself, in the same lower-case form the wire format uses.
        ((string[])properties["category"]["enum"]).ShouldBe(
            ["bug", "feature", "question", "other"], ignoreOrder: true);

        var list = tools.Single(tool => tool.name == "feedback_list");
        list.inputSchema.ShouldNotContainKey("required");
        var listProperties =
            (Dictionary<string, Dictionary<string, object>>)list.inputSchema["properties"];
        listProperties.Keys.ShouldBe(["status"]);

        var process = tools.Single(tool => tool.name == "feedback_process");
        var processRequired = (string[])process.inputSchema["required"];
        processRequired.ShouldBe(["id", "resolution"], ignoreOrder: true);

        var report = tools.Single(tool => tool.name == "feedback_report");
        var reportRequired = (string[])report.inputSchema["required"];
        reportRequired.ShouldBe(["id"]);
        var reportProperties =
            (Dictionary<string, Dictionary<string, object>>)report.inputSchema["properties"];
        // "dry-run" (kebab), not "dryRun" — must match the key McpCommandAdapter.ApplySafetyPolicy
        // injects for DryRunByDefault descriptors (see [JsonPropertyName] on FeedbackReportRequest).
        reportProperties.Keys.ShouldBe(["id", "yes", "attachTo", "dry-run"], ignoreOrder: true);

        // The non-automation guidance is the load-bearing part of this tool's description —
        // it is the only place an MCP-calling agent sees it before deciding to invoke.
        report.description.ShouldContain("Do NOT call this automatically", Case.Sensitive);
    }

    [Fact]
    public async Task feedback_add_records_every_input_and_returns_the_allocated_id()
    {
        FeedbackAddRequest? recorded = null;
        var operation = new FeedbackAddOperation(request =>
        {
            recorded = request;
            return new FeedbackEntry
            {
                Id = "FB-374",
                Type = "user-feedback",
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Title = request.Title!,
                Category = request.Category!.Value,
                Description = request.Description!,
                SampleCode = request.SampleCode,
                ExpectedResult = request.ExpectedResult,
                CommandLine = request.CommandLine,
                Command = request.Command
            };
        });
        var result = await operation.ExecuteAsync(new FeedbackAddRequest(
            "MCP exposure gap",
            FeedbackCategory.Feature,
            "Expose feedback through MCP",
            "sample",
            "structured result",
            "feedback_add",
            "feedback add"));

        recorded.ShouldNotBeNull();
        recorded.Title.ShouldBe("MCP exposure gap");
        recorded.Category.ShouldBe(FeedbackCategory.Feature);
        recorded.Description.ShouldBe("Expose feedback through MCP");
        recorded.SampleCode.ShouldBe("sample");
        recorded.ExpectedResult.ShouldBe("structured result");
        recorded.CommandLine.ShouldBe("feedback_add");
        recorded.Command.ShouldBe("feedback add");

        result.Id.ShouldBe("FB-374");
        result.Status.ShouldBe("pending");
    }

    [Theory]
    [InlineData(null, "desc")]
    [InlineData("", "desc")]
    [InlineData("Title", null)]
    public async Task feedback_add_rejects_missing_mandatory_fields(string? title, string? description)
    {
        var operation = new FeedbackAddOperation(_ =>
            throw new InvalidOperationException("the store must not be reached for invalid input"));

        await Should.ThrowAsync<ArgumentException>(() => operation.ExecuteAsync(
            new FeedbackAddRequest(title, FeedbackCategory.Feature, description)));
    }

    [Fact]
    public async Task feedback_add_rejects_a_missing_category()
    {
        var operation = new FeedbackAddOperation(_ =>
            throw new InvalidOperationException("the store must not be reached for invalid input"));

        await Should.ThrowAsync<ArgumentException>(() => operation.ExecuteAsync(
            new FeedbackAddRequest("Title", Category: null, Description: "desc")));
    }

    [Fact]
    public async Task feedback_list_returns_only_entries_matching_the_requested_status()
    {
        var entries = new List<FeedbackEntry>
        {
            new() { Id = "FB-001", Description = "pending", Status = "pending" },
            new() { Id = "FB-002", Description = "done", Status = "processed" }
        };
        var operation = new FeedbackListOperation(status => entries
            .Where(entry => string.Equals(entry.Status, status, StringComparison.OrdinalIgnoreCase))
            .ToList());
        var result = await operation.ExecuteAsync(new FeedbackListRequest("processed"));

        result.Count.ShouldBe(1);
        result[0].Id.ShouldBe("FB-002");
        result[0].Status.ShouldBe("processed");
    }

    [Fact]
    public async Task feedback_process_marks_the_requested_item_processed()
    {
        string? processedId = null;
        string? recordedResolution = null;
        var operation = new FeedbackProcessOperation((id, resolution) =>
        {
            processedId = id;
            recordedResolution = resolution;
            return new FeedbackEntry
            {
                Id = id,
                Status = "processed",
                ProcessedAt = "2026-07-30T08:00:00.0000000+00:00",
                Resolution = resolution
            };
        });

        var result = await operation.ExecuteAsync(
            new FeedbackProcessRequest("FB-001", "Regression test added."));

        processedId.ShouldBe("FB-001");
        recordedResolution.ShouldBe("Regression test added.");
        result.Id.ShouldBe("FB-001");
        result.Status.ShouldBe("processed");
        result.Resolution.ShouldBe("Regression test added.");
        result.ProcessedAt.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task feedback_report_returns_not_found_for_an_unknown_id()
    {
        var operation = new FeedbackReportOperation(
            _ => null,
            new FakeGitHubIssueClient(),
            (_, _, _) => throw new InvalidOperationException("must not reach markReported"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-999"));

        result.IsErr.ShouldBeTrue();
        result.Match(_ => "", error => error.Kind.ToString()).ShouldBe(nameof(RtErrorKind.NotFound));
    }

    [Fact]
    public async Task feedback_report_surfaces_a_clean_error_when_gh_is_unavailable()
    {
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            new FakeGitHubIssueClient { Available = false },
            (_, _, _) => throw new InvalidOperationException("must not reach markReported"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001"));

        result.IsErr.ShouldBeTrue();
        result.Match(_ => "", error => error.Kind.ToString()).ShouldBe(nameof(RtErrorKind.ExternalToolFailure));
    }

    [Fact]
    public async Task feedback_report_returns_already_reported_without_calling_gh_when_previously_filed()
    {
        var gitHub = new FakeGitHubIssueClient { Available = false }; // must not even need gh here
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(issueNumber: 55, issueUrl: "https://example/55"),
            gitHub,
            (_, _, _) => throw new InvalidOperationException("must not report a second time"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001", Yes: true));

        var value = result.Match(v => v, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("already-reported");
        value.IssueNumber.ShouldBe(55);
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task feedback_report_rejects_an_entry_with_no_title()
    {
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(title: ""),
            new FakeGitHubIssueClient(),
            (_, _, _) => throw new InvalidOperationException("must not report a titleless entry"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001", Yes: true));

        result.IsErr.ShouldBeTrue();
        result.Match(_ => "", error => error.Kind.ToString()).ShouldBe(nameof(RtErrorKind.InvalidInput));
    }

    private static readonly JsonSerializerOptions McpRequestOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public void feedback_report_request_dry_run_binds_from_the_kebab_case_key_the_mcp_safety_net_injects()
    {
        // Regression: McpCommandAdapter.ApplySafetyPolicy force-injects "dry-run" (kebab-case)
        // for DryRunByDefault descriptors under a camelCase JsonSerializerOptions. Without
        // [JsonPropertyName("dry-run")] on FeedbackReportRequest.DryRun, that key silently
        // fails to bind and an MCP caller who omits --dry-run files a real issue by default.
        var request = JsonSerializer.Deserialize<FeedbackReportRequest>(
            """{"id":"FB-001","dry-run":true}""", McpRequestOptions);

        request.ShouldNotBeNull();
        request.DryRun.ShouldBeTrue();
    }

    [Fact]
    public async Task feedback_report_returns_candidates_without_filing_when_duplicates_are_found()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            SearchResults =
            [
                new GitHubIssueSummary(
                    42, "[bug] [inspect describe] describe fails on generics",
                    "https://example/42", "same underlying bug", "OPEN")
            ]
        };
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            gitHub,
            (_, _, _) => throw new InvalidOperationException("must not create when duplicates are unreviewed"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001", DryRun: false));

        result.IsOk.ShouldBeTrue();
        var value = result.Match(v => v, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("duplicates-found");
        value.IssueNumber.ShouldBeNull();
        value.PossibleDuplicates.ShouldHaveSingleItem();
        value.PossibleDuplicates[0].Number.ShouldBe(42);
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task feedback_report_yes_files_a_new_issue_despite_a_found_duplicate()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            SearchResults =
                [new GitHubIssueSummary(42, "[bug] [inspect describe] similar", "https://example/42", null, "OPEN")],
            CreatedIssue = new GitHubIssueSummary(77, "new", "https://example/77", null, "OPEN")
        };
        FeedbackEntry? reported = null;
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            gitHub,
            (id, number, url) =>
            {
                reported = new FeedbackEntry { Id = id, GitHubIssueNumber = number, GitHubIssueUrl = url };
                return reported;
            });

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001", Yes: true, DryRun: false));

        var value = result.Match(v => v, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("reported");
        value.IssueNumber.ShouldBe(77);
        gitHub.CreateIssueCallCount.ShouldBe(1);
        reported.ShouldNotBeNull();
        reported.GitHubIssueNumber.ShouldBe(77);
    }

    [Fact]
    public async Task feedback_report_attach_to_links_the_named_issue_without_searching()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            IssueToReturn = new GitHubIssueSummary(9, "existing", "https://example/9", null, "OPEN")
        };
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            gitHub,
            (id, number, url) => new FeedbackEntry { Id = id, GitHubIssueNumber = number, GitHubIssueUrl = url });

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001", AttachTo: 9, DryRun: false));

        var value = result.Match(v => v, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("attached");
        value.IssueNumber.ShouldBe(9);
        gitHub.LastSearchQuery.ShouldBeNull("--attach-to already knows the target issue; it must not search");
    }

    [Fact]
    public async Task feedback_report_dry_run_never_creates_or_attaches_anything()
    {
        var gitHub = new FakeGitHubIssueClient();
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            gitHub,
            (_, _, _) => throw new InvalidOperationException("dry-run must not persist anything"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001", DryRun: true));

        var value = result.Match(v => v, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("dry-run");
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    private static FeedbackEntry SampleEntry(
        string title = "describe fails on generics",
        int? issueNumber = null,
        string? issueUrl = null) => new()
        {
            Id = "FB-001",
            Title = title,
            Category = FeedbackCategory.Bug,
            Command = "inspect describe",
            Description = "repro steps",
            GitHubIssueNumber = issueNumber,
            GitHubIssueUrl = issueUrl
        };

    private sealed class FakeGitHubIssueClient : IGitHubIssueClient
    {
        public bool Available { get; init; } = true;
        public IReadOnlyList<GitHubIssueSummary> SearchResults { get; init; } = [];
        public GitHubIssueSummary? IssueToReturn { get; init; }
        public GitHubIssueSummary? CreatedIssue { get; init; }
        public string? LastSearchQuery { get; private set; }
        public int CreateIssueCallCount { get; private set; }

        public bool IsAvailable() => Available;

        public IReadOnlyList<GitHubIssueSummary> Search(string query, int limit)
        {
            LastSearchQuery = query;
            return SearchResults;
        }

        public GitHubIssueSummary GetIssue(int number) =>
            IssueToReturn ?? throw new InvalidOperationException($"no issue #{number} configured");

        public GitHubIssueSummary CreateIssue(string title, string body)
        {
            CreateIssueCallCount++;
            return CreatedIssue ?? new GitHubIssueSummary(999, title, "https://example/999", body, "OPEN");
        }
    }
}
