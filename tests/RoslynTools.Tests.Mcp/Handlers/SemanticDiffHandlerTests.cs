using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services.SemanticDiff;
using RoslynTools.Services.SemanticDiff.Git;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

/// <summary>
///     Tests for the native <c>semantic_diff</c> MCP handler — the thin adapter that resolves the
///     same <see cref="GitSemanticDiffSourceProvider" /> + <see cref="SemanticDiffService" /> the CLI
///     uses and returns the engine's surfaced deltas as structured JSON. The handler is constructed
///     directly with a provider over a <see cref="RecordingGitClient" /> so no real repository is
///     touched and the before/after snapshots are deterministic (mirrors the CLI command tests).
/// </summary>
public sealed class SemanticDiffHandlerTests
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] ExpectedPropertyNames =
        ["files", "from", "to", "ignoreAccessibility", "detectRenames", "directory"];

    [Fact]
    public void definition_exposes_the_semantic_diff_tool_name_and_optional_args()
    {
        using var provider = McpTestHost.CreateProvider();
        var def = provider.GetRequiredService<ToolRegistry>()
            .GetTools()
            .Single(tool => tool.name == "semantic_diff");
        def.name.ShouldBe("semantic_diff");
        def.description.ShouldNotBeNullOrWhiteSpace();

        // No required fields — default is working-tree-vs-HEAD in the cwd.
        def.inputSchema.ShouldNotContainKey("required");

        var properties = (Dictionary<string, Dictionary<string, object>>)def.inputSchema["properties"];
        properties.Keys.ShouldBe(ExpectedPropertyNames, ignoreOrder: true);
    }

    [Fact]
    public async Task a_changed_method_returns_a_json_array_of_surfaced_deltas()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void M() { var x = 1; } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void M() { var x = 2; } }" },
        };

        var result = await InvokeAsync(git, EmptyArgs());

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        payload.RootElement.GetArrayLength().ShouldBeGreaterThan(0);

        var changed = payload.RootElement.EnumerateArray()
            .First(d => d.GetProperty("status").GetString() == "Changed");
        changed.GetProperty("fqn").GetString().ShouldNotBeNull().ShouldContain("Gadget");
        changed.GetProperty("kind").GetString().ShouldNotBeNullOrEmpty();

        // The Gadget class itself is Unchanged on the same path (only its method body changed),
        // so the surfaced-only filter must exclude it — proving SemanticDiffJson applies IsSurfaced.
        payload.RootElement.EnumerateArray()
            .ShouldNotContain(d =>
                d.GetProperty("status").GetString() == "Unchanged"
                && d.GetProperty("oldPath").GetString() == d.GetProperty("newPath").GetString());
        payload.RootElement.EnumerateArray()
            .ShouldNotContain(d => d.GetProperty("fqn").GetString() == "Gadget");
    }

    [Fact]
    public async Task handler_json_is_byte_identical_to_the_cli_json_for_the_same_diff()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void M() { var x = 1; } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void M() { var x = 2; } public int Added() => 1; }" },
        };

        // The handler's JSON.
        var result = await InvokeAsync(git, EmptyArgs());

        // The CLI's JSON: same provider + engine, projected through SemanticDiffOutput.RenderJson.
        var provider = new GitSemanticDiffSourceProvider(git);
        var (before, after) = provider.Load(null, null);
        var engineResult = new SemanticDiffService().Compare(
            new SemanticDiffRequest(before, after, SemanticDiffOptions.Default));
        var cliOutput = new SemanticDiffOutput(engineResult.Deltas, Text: "");
        using var cliWriter = new StringWriter();
        cliOutput.RenderJson(cliWriter);

        result.content.ShouldBe(cliWriter.ToString());
    }

    [Fact]
    public async Task ignore_accessibility_arg_flows_into_the_engine_options()
    {
        // public -> private differs only in accessibility. With ignoreAccessibility=true the method
        // is Unchanged (not surfaced); without it, the method is Changed (surfaced).
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { public void M() { } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { private void M() { } }" },
        };

        var ignored = await InvokeAsync(
            git, Args("""{"ignoreAccessibility":true}"""));
        var notIgnored = await InvokeAsync(
            git, Args("""{"ignoreAccessibility":false}"""));

        ignored.isError.ShouldBeFalse();
        notIgnored.isError.ShouldBeFalse();

        using var ignoredDoc = JsonDocument.Parse(ignored.content);
        using var notIgnoredDoc = JsonDocument.Parse(notIgnored.content);

        ignoredDoc.RootElement.EnumerateArray()
            .ShouldNotContain(d => d.GetProperty("fqn").GetString()!.Contains(".M", StringComparison.Ordinal));
        notIgnoredDoc.RootElement.EnumerateArray()
            .ShouldContain(d => d.GetProperty("fqn").GetString()!.Contains(".M", StringComparison.Ordinal));
    }

    [Fact]
    public async Task detect_renames_arg_flows_into_the_engine_options()
    {
        // A pure rename: with detectRenames=true it pairs into one Renamed entry; with false it
        // surfaces as a separate Added + Removed pair.
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void Old() { var x = 1; } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void New() { var x = 1; } }" },
        };

        var detected = await InvokeAsync(
            git, Args("""{"detectRenames":true}"""));
        var notDetected = await InvokeAsync(
            git, Args("""{"detectRenames":false}"""));

        using var detectedDoc = JsonDocument.Parse(detected.content);
        using var notDetectedDoc = JsonDocument.Parse(notDetected.content);

        var detectedStatuses = detectedDoc.RootElement.EnumerateArray()
            .Select(d => d.GetProperty("status").GetString()).ToList();
        var notDetectedStatuses = notDetectedDoc.RootElement.EnumerateArray()
            .Select(d => d.GetProperty("status").GetString()).ToList();

        detectedStatuses.ShouldContain("Renamed");
        notDetectedStatuses.ShouldContain("Added");
        notDetectedStatuses.ShouldContain("Removed");
        notDetectedStatuses.ShouldNotContain("Renamed");
    }

    [Fact]
    public async Task detect_renames_defaults_to_true_when_the_arg_is_absent()
    {
        var git = new RecordingGitClient
        {
            WorkingTreeChangedPaths = ["Gadget.cs"],
            FilesAtRef = { ["HEAD:Gadget.cs"] = "class Gadget { void Old() { var x = 1; } }" },
            WorkingTreeFiles = { ["Gadget.cs"] = "class Gadget { void New() { var x = 1; } }" },
        };

        var result = await InvokeAsync(git, EmptyArgs());

        using var doc = JsonDocument.Parse(result.content);
        doc.RootElement.EnumerateArray()
            .Select(d => d.GetProperty("status").GetString())
            .ShouldContain("Renamed");
    }

    [Fact]
    public async Task not_a_git_repository_returns_an_error_result_without_throwing()
    {
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var result = await InvokeAsync(git, EmptyArgs());

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("not a git repository");
    }

    [Fact]
    public async Task invalid_ref_returns_an_error_result_without_throwing()
    {
        var git = new RecordingGitClient { InvalidRefs = { "nope" } };

        var result = await InvokeAsync(git, Args("""{"from":"nope"}"""));

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("invalid git ref");
    }

    [Fact]
    public async Task a_directory_arg_targets_that_repository_and_a_non_repo_path_errors_cleanly()
    {
        // A directory that is not a git repository builds a fresh GitClient for that path; the
        // provider's EnsureRepository fails and surfaces as a clean error, not a thrown exception.
        var nonRepo = Path.Combine(Path.GetTempPath(), $"rt-sdiff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(nonRepo);
        try
        {
            var result = await InvokeAsync(
                new RecordingGitClient(),
                Args($$"""{"directory":{{JsonSerializer.Serialize(nonRepo)}}}"""));

            result.isError.ShouldBeTrue();
            result.content.ShouldContain("not a git repository");
        }
        finally
        {
            Directory.Delete(nonRepo, recursive: true);
        }
    }

    [Fact]
    public void semantic_diff_is_descriptor_backed()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<ToolRegistry>().GetTools()
            .Select(tool => tool.name)
            .ShouldContain("semantic_diff");
    }

    [Fact]
    public async Task tool_registry_dispatches_semantic_diff_to_the_native_handler()
    {
        // End-to-end through the real ToolRegistry against the live repo: HEAD..HEAD is a
        // deterministic no-op diff (independent of the working tree), so the native handler
        // returns an empty JSON array — proving the registry invokes the typed application port.
        using var provider = McpTestHost.CreateProvider();
        var registry = provider.GetRequiredService<ToolRegistry>();
        using var args = JsonDocument.Parse("""{"from":"HEAD","to":"HEAD"}""");

        var result = await registry.InvokeAsync("semantic_diff", args.RootElement);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        payload.RootElement.GetArrayLength().ShouldBe(0);
    }

    private static async Task<ToolResult> InvokeAsync(
        IGitClient git,
        JsonElement arguments)
    {
        var operation = new SemanticDiffOperation(
            new GitSemanticDiffSourceProvider(git),
            new FileSemanticDiffSourceProvider(new FileTextReader()),
            new SemanticDiffService(),
            new SemanticDiffTextRenderer());

        try
        {
            var request = JsonSerializer.Deserialize<SemanticDiffOperationRequest>(
                arguments.GetRawText(),
                RequestJsonOptions)!;
            var result = await ((ICommandApplicationPort)operation)
                .InvokeAsync(request, CancellationToken.None);
            var content = result.Value is SemanticDiffOperationResult semanticDiff
                ? SemanticDiffJson.Serialize(semanticDiff.Deltas)
                : JsonSerializer.Serialize(result.Value);
            return new ToolResult(result.IsError, result.Error?.Message ?? content);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return new ToolResult(true, ex.Message);
        }
    }

    private static JsonElement EmptyArgs() => Args("{}");

    private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>
    ///     In-memory <see cref="IGitClient" /> mirroring the CLI command tests' fake: validation
    ///     throws (not-a-repo / invalid-ref), content reads return null for an absent key.
    /// </summary>
    private sealed class RecordingGitClient : IGitClient
    {
        public bool ThrowNotARepository { get; init; }

        public HashSet<string> InvalidRefs { get; } = new(StringComparer.Ordinal);

        public IReadOnlyList<string> WorkingTreeChangedPaths { get; init; } = [];

        public Dictionary<string, string> FilesAtRef { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, string> WorkingTreeFiles { get; } = new(StringComparer.Ordinal);

        public void EnsureRepository()
        {
            if (ThrowNotARepository)
                throw new InvalidOperationException("not a git repository: <fake>");
        }

        public void EnsureRefExists(string reference)
        {
            if (InvalidRefs.Contains(reference))
                throw new ArgumentException($"invalid git ref: {reference}");
        }

        // No test exercises a successful ref-vs-ref diff; the invalid-ref test throws at
        // EnsureRefExists before this is reached, so an empty list is the honest default.
        public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef) => [];

        public IReadOnlyList<string> GetWorkingTreeChangedPaths() => WorkingTreeChangedPaths;

        public string? ReadFileAtRef(string reference, string path) =>
            FilesAtRef.TryGetValue($"{reference}:{path}", out var text) ? text : null;

        public string? ReadWorkingTreeFile(string path) =>
            WorkingTreeFiles.TryGetValue(path, out var text) ? text : null;
    }
}
