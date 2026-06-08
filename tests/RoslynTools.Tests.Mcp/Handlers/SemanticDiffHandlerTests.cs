using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Handlers;
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
    [Fact]
    public void definition_exposes_the_semantic_diff_tool_name_and_optional_args()
    {
        var handler = NewHandler(new RecordingGitClient());

        handler.ToolName.ShouldBe("semantic_diff");

        var def = handler.Definition;
        def.name.ShouldBe("semantic_diff");
        def.description.ShouldNotBeNullOrWhiteSpace();

        // No required fields — default is working-tree-vs-HEAD in the cwd.
        def.inputSchema.ShouldNotContainKey("required");

        var properties = (Dictionary<string, Dictionary<string, object>>)def.inputSchema["properties"];
        properties.Keys.ShouldBe(
            new[] { "from", "to", "ignoreAccessibility", "detectRenames", "directory" },
            ignoreOrder: true);
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

        var result = await NewHandler(git).HandleAsync(EmptyArgs(), CancellationToken.None);

        result.isError.ShouldBeFalse();
        using var payload = JsonDocument.Parse(result.content);
        payload.RootElement.ValueKind.ShouldBe(JsonValueKind.Array);
        payload.RootElement.GetArrayLength().ShouldBeGreaterThan(0);

        var changed = payload.RootElement.EnumerateArray()
            .First(d => d.GetProperty("status").GetString() == "Changed");
        changed.GetProperty("fqn").GetString().ShouldContain("Gadget");
        changed.GetProperty("kind").GetString().ShouldNotBeNullOrEmpty();
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
        var result = await NewHandler(git).HandleAsync(EmptyArgs(), CancellationToken.None);

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

        var ignored = await NewHandler(git).HandleAsync(
            Args("""{"ignoreAccessibility":true}"""), CancellationToken.None);
        var notIgnored = await NewHandler(git).HandleAsync(
            Args("""{"ignoreAccessibility":false}"""), CancellationToken.None);

        ignored.isError.ShouldBeFalse();
        notIgnored.isError.ShouldBeFalse();

        using var ignoredDoc = JsonDocument.Parse(ignored.content);
        using var notIgnoredDoc = JsonDocument.Parse(notIgnored.content);

        ignoredDoc.RootElement.EnumerateArray()
            .ShouldNotContain(d => d.GetProperty("fqn").GetString()!.Contains("M"));
        notIgnoredDoc.RootElement.EnumerateArray()
            .ShouldContain(d => d.GetProperty("fqn").GetString()!.Contains("M"));
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

        var detected = await NewHandler(git).HandleAsync(
            Args("""{"detectRenames":true}"""), CancellationToken.None);
        var notDetected = await NewHandler(git).HandleAsync(
            Args("""{"detectRenames":false}"""), CancellationToken.None);

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

        var result = await NewHandler(git).HandleAsync(EmptyArgs(), CancellationToken.None);

        using var doc = JsonDocument.Parse(result.content);
        doc.RootElement.EnumerateArray()
            .Select(d => d.GetProperty("status").GetString())
            .ShouldContain("Renamed");
    }

    [Fact]
    public async Task not_a_git_repository_returns_an_error_result_without_throwing()
    {
        var git = new RecordingGitClient { ThrowNotARepository = true };

        var result = await NewHandler(git).HandleAsync(EmptyArgs(), CancellationToken.None);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("not a git repository");
    }

    [Fact]
    public async Task invalid_ref_returns_an_error_result_without_throwing()
    {
        var git = new RecordingGitClient { InvalidRefs = { "nope" } };

        var result = await NewHandler(git).HandleAsync(
            Args("""{"from":"nope"}"""), CancellationToken.None);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain("invalid git ref");
    }

    [Fact]
    public void semantic_diff_handler_is_registered_as_an_imcphandler()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());

        using var provider = services.BuildServiceProvider();
        var toolNames = provider.GetServices<IMcpHandler>().Select(h => h.ToolName).ToList();

        toolNames.ShouldContain("semantic_diff");
    }

    private static SemanticDiffHandler NewHandler(IGitClient git) =>
        new(new GitSemanticDiffSourceProvider(git), new SemanticDiffService());

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

        public Dictionary<(string From, string To), IReadOnlyList<string>> ChangedPathsBetween { get; } = new();

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

        public IReadOnlyList<string> GetChangedPathsBetween(string fromRef, string toRef) =>
            ChangedPathsBetween.TryGetValue((fromRef, toRef), out var paths) ? paths : [];

        public IReadOnlyList<string> GetWorkingTreeChangedPaths() => WorkingTreeChangedPaths;

        public string? ReadFileAtRef(string reference, string path) =>
            FilesAtRef.TryGetValue($"{reference}:{path}", out var text) ? text : null;

        public string? ReadWorkingTreeFile(string path) =>
            WorkingTreeFiles.TryGetValue(path, out var text) ? text : null;
    }
}
