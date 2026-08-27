using System.Globalization;
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
    private static readonly JsonSerializerOptions McpJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
        required.ShouldBe(["description"]);
        var properties =
            (Dictionary<string, Dictionary<string, object>>)add.inputSchema["properties"];
        properties.Keys.ShouldBe(
            ["description", "sampleCode", "expectedResult", "commandLine"],
            ignoreOrder: true);

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
        reportProperties.Keys.ShouldBe(["id", "yes", "attachTo", "dry-run"], ignoreOrder: true);
    }

    [Fact]
    public async Task feedback_add_records_every_input_and_returns_the_allocated_id()
    {
        FeedbackEntry? recorded = null;
        var operation = new FeedbackAddOperation((description, sampleCode, expectedResult, commandLine) =>
        {
            recorded = new FeedbackEntry
            {
                Id = "FB-374",
                Type = "user-feedback",
                Timestamp = DateTimeOffset.UtcNow.ToString("O"),
                Description = description,
                SampleCode = sampleCode,
                ExpectedResult = expectedResult,
                CommandLine = commandLine
            };
            return recorded;
        });
        var result = await operation.ExecuteAsync(new FeedbackAddRequest(
            "Expose feedback through MCP",
            "sample",
            "structured result",
            "feedback_add"));

        recorded.ShouldNotBeNull();
        recorded.Type.ShouldBe("user-feedback");
        recorded.Status.ShouldBe("pending");
        recorded.Description.ShouldBe("Expose feedback through MCP");
        recorded.SampleCode.ShouldBe("sample");
        recorded.ExpectedResult.ShouldBe("structured result");
        recorded.CommandLine.ShouldBe("feedback_add");
        DateTimeOffset.Parse(recorded.Timestamp, CultureInfo.InvariantCulture)
            .ShouldBeLessThanOrEqualTo(DateTimeOffset.UtcNow);

        result.Id.ShouldBe("FB-374");
        result.Status.ShouldBe("pending");
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
            (_, _, _) => throw new InvalidOperationException("must not persist"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-999"));

        result.IsErr.ShouldBeTrue();
        result.Match(_ => (RtErrorKind?)null, error => error.Kind).ShouldBe(RtErrorKind.NotFound);
    }

    [Fact]
    public async Task feedback_report_surfaces_an_actionable_error_when_gh_is_unavailable()
    {
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            new FakeGitHubIssueClient { Available = false },
            (_, _, _) => throw new InvalidOperationException("must not persist"));

        var result = await operation.ExecuteAsync(new FeedbackReportRequest("FB-001"));

        var error = result.Match(_ => throw new InvalidOperationException(), value => value);
        error.Kind.ShouldBe(RtErrorKind.ExternalToolFailure);
        error.Message.ShouldContain("gh auth login");
    }

    [Fact]
    public async Task feedback_report_requires_explicit_yes_even_when_search_finds_no_duplicates()
    {
        var gitHub = new FakeGitHubIssueClient();
        var operation = CreateReportOperation(gitHub);

        var result = await operation.ExecuteAsync(
            new FeedbackReportRequest("FB-001", DryRun: false));

        var value = result.Match(success => success, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("confirmation-required");
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task feedback_report_returns_ranked_candidates_without_filing()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            SearchResults =
            [
                new GitHubIssueSummary(8, "Unrelated documentation typo", "https://example/8", null, "OPEN"),
                new GitHubIssueSummary(
                    42,
                    "Feedback: describe fails on generic methods",
                    "https://example/42",
                    "inspect describe returns the wrong symbol",
                    "OPEN")
            ]
        };
        var operation = CreateReportOperation(gitHub);

        var result = await operation.ExecuteAsync(
            new FeedbackReportRequest("FB-001", DryRun: false));

        var value = result.Match(success => success, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("duplicates-found");
        value.PossibleDuplicates.ShouldNotBeEmpty();
        value.PossibleDuplicates[0].Number.ShouldBe(42);
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task feedback_report_yes_files_and_records_a_new_issue()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            CreatedIssue = new GitHubIssueSummary(
                77,
                "Feedback: describe fails on generic methods",
                "https://example/77",
                null,
                "OPEN")
        };
        FeedbackEntry? recorded = null;
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            gitHub,
            (id, number, url) => recorded = new FeedbackEntry
            {
                Id = id,
                GitHubIssueNumber = number,
                GitHubIssueUrl = url
            });

        var result = await operation.ExecuteAsync(
            new FeedbackReportRequest("FB-001", Yes: true, DryRun: false));

        var value = result.Match(success => success, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("reported");
        value.IssueNumber.ShouldBe(77);
        gitHub.CreateIssueCallCount.ShouldBe(1);
        gitHub.CreatedTitle.ShouldBe("Feedback: describe fails on generic methods");
        gitHub.CreatedBody!.ShouldNotContain(Directory.GetCurrentDirectory());
        recorded.ShouldNotBeNull();
        recorded.GitHubIssueNumber.ShouldBe(77);
    }

    [Fact]
    public async Task feedback_report_attach_to_links_without_searching_or_creating()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            IssueToReturn = new GitHubIssueSummary(9, "existing", "https://example/9", null, "OPEN")
        };
        var operation = CreateReportOperation(gitHub);

        var result = await operation.ExecuteAsync(
            new FeedbackReportRequest("FB-001", AttachTo: 9, DryRun: false));

        var value = result.Match(success => success, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("attached");
        value.IssueNumber.ShouldBe(9);
        gitHub.LastSearchQuery.ShouldBeNull();
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task feedback_report_dry_run_never_persists_even_with_yes_or_attach_to()
    {
        var gitHub = new FakeGitHubIssueClient
        {
            IssueToReturn = new GitHubIssueSummary(9, "existing", "https://example/9", null, "OPEN")
        };
        var persisted = false;
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(),
            gitHub,
            (_, _, _) =>
            {
                persisted = true;
                return SampleEntry();
            });

        var result = await operation.ExecuteAsync(
            new FeedbackReportRequest("FB-001", Yes: true, AttachTo: 9, DryRun: true));

        var value = result.Match(success => success, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("dry-run");
        persisted.ShouldBeFalse();
        gitHub.CreateIssueCallCount.ShouldBe(0);
    }

    [Fact]
    public async Task feedback_report_is_idempotent_after_an_issue_was_recorded()
    {
        var gitHub = new FakeGitHubIssueClient { Available = false };
        var operation = new FeedbackReportOperation(
            _ => SampleEntry(issueNumber: 55, issueUrl: "https://example/55"),
            gitHub,
            (_, _, _) => throw new InvalidOperationException("must not persist twice"));

        var result = await operation.ExecuteAsync(
            new FeedbackReportRequest("FB-001", Yes: true, DryRun: false));

        var value = result.Match(success => success, _ => throw new InvalidOperationException());
        value.Status.ShouldBe("already-reported");
        value.IssueNumber.ShouldBe(55);
        gitHub.AvailabilityChecks.ShouldBe(0);
    }

    [Fact]
    public void feedback_report_request_binds_the_mcp_safety_key()
    {
        var request = JsonSerializer.Deserialize<FeedbackReportRequest>(
            """{"id":"FB-001","dry-run":true}""",
            McpJsonOptions);

        request.ShouldNotBeNull();
        request.DryRun.ShouldBeTrue();
    }

    private static FeedbackReportOperation CreateReportOperation(FakeGitHubIssueClient gitHub) =>
        new(
            _ => SampleEntry(),
            gitHub,
            (id, number, url) => new FeedbackEntry
            {
                Id = id,
                GitHubIssueNumber = number,
                GitHubIssueUrl = url
            });

    private static FeedbackEntry SampleEntry(int? issueNumber = null, string? issueUrl = null) => new()
    {
        Id = "FB-001",
        Type = "user-feedback",
        Description = "describe fails on generic methods",
        CommandLine = "rt inspect describe Example.Type.Method",
        Cwd = Directory.GetCurrentDirectory(),
        GitHubIssueNumber = issueNumber,
        GitHubIssueUrl = issueUrl
    };

    private sealed class FakeGitHubIssueClient : IGitHubIssueClient
    {
        public bool Available { get; init; } = true;
        public IReadOnlyList<GitHubIssueSummary> SearchResults { get; init; } = [];
        public GitHubIssueSummary? IssueToReturn { get; init; }
        public GitHubIssueSummary? CreatedIssue { get; init; }
        public int AvailabilityChecks { get; private set; }
        public int CreateIssueCallCount { get; private set; }
        public string? LastSearchQuery { get; private set; }
        public string? CreatedTitle { get; private set; }
        public string? CreatedBody { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            AvailabilityChecks++;
            return Task.FromResult(Available);
        }

        public Task<IReadOnlyList<GitHubIssueSummary>> SearchAsync(
            string query,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastSearchQuery = query;
            return Task.FromResult(SearchResults);
        }

        public Task<GitHubIssueSummary> GetIssueAsync(
            int number,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(IssueToReturn
                            ?? throw new InvalidOperationException($"no issue #{number} configured"));

        public Task<GitHubIssueSummary> CreateIssueAsync(
            string title,
            string body,
            CancellationToken cancellationToken = default)
        {
            CreateIssueCallCount++;
            CreatedTitle = title;
            CreatedBody = body;
            return Task.FromResult(CreatedIssue
                                   ?? new GitHubIssueSummary(
                                       999,
                                       title,
                                       "https://example/999",
                                       body,
                                       "OPEN"));
        }
    }
}
