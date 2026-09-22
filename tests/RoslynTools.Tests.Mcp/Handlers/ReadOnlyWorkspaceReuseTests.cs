using System.Text;
using System.Text.Json;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.DependencyInjection;

using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Tests;

using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

[Collection("mcp-bridge")]
public sealed class ReadOnlyWorkspaceReuseTests
{
    [Fact]
    public async Task sequential_semantic_tools_open_one_solution_and_share_one_revision()
    {
        using var fixture = SemanticFixture.Create("SharedApp");
        using var daemon = DaemonRuntime.UseExperimentOverride(false);
        using var open = fixture.OverrideSolutionOpen();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(fixture.Root));
        var tools = provider.GetRequiredService<ToolRegistry>();

        var outline = await InvokeAsync(tools, "outline", new
        {
            target = fixture.SourcePath,
            solution = fixture.SolutionPath
        });
        var search = await InvokeAsync(tools, "search_symbols", new
        {
            query = "Widget",
            solution = fixture.SolutionPath
        });
        var describe = await InvokeAsync(tools, "describe", new
        {
            symbol = fixture.Symbol,
            solution = fixture.SolutionPath
        });
        var source = await InvokeAsync(tools, "source", new
        {
            symbol = fixture.Symbol,
            solution = fixture.SolutionPath
        });
        var relationships = await InvokeAsync(tools, "relationships", new
        {
            symbol = fixture.Symbol,
            mode = "References",
            solution = fixture.SolutionPath
        });

        outline.isError.ShouldBeFalse();
        search.isError.ShouldBeFalse();
        describe.isError.ShouldBeFalse();
        source.isError.ShouldBeFalse();
        relationships.isError.ShouldBeFalse();
        fixture.OpenCount.ShouldBe(1);
        Revision(outline).ShouldNotBeNullOrWhiteSpace();
        Revision(search).ShouldBe(Revision(outline));
        FlatRevision(describe).ShouldBe(Revision(outline));
        FlatRevision(source).ShouldBe(Revision(outline));
        Revision(relationships).ShouldBe(Revision(outline));
    }

    [Fact]
    public async Task concurrent_cold_semantic_tools_coalesce_one_solution_open()
    {
        using var fixture = SemanticFixture.Create("ConcurrentApp");
        using var daemon = DaemonRuntime.UseExperimentOverride(false);
        var releaseOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var open = fixture.OverrideSolutionOpen(async () => await releaseOpen.Task);
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(fixture.Root));
        var tools = provider.GetRequiredService<ToolRegistry>();

        var outlineTask = InvokeAsync(tools, "outline", new
        {
            target = fixture.SourcePath,
            solution = fixture.SolutionPath
        });
        await fixture.FirstOpenStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var searchTask = InvokeAsync(tools, "search_symbols", new
        {
            query = "Widget",
            solution = fixture.SolutionPath
        });

        releaseOpen.SetResult();
        var results = await Task.WhenAll(outlineTask, searchTask);

        results.ShouldAllBe(result => !result.isError);
        fixture.OpenCount.ShouldBe(1);
        Revision(results[0]).ShouldBe(Revision(results[1]));
    }

    [Fact]
    public async Task relevant_source_change_refreshes_the_snapshot_without_a_git_revision_change()
    {
        using var fixture = SemanticFixture.Create("RefreshApp");
        using var daemon = DaemonRuntime.UseExperimentOverride(false);
        using var open = fixture.OverrideSolutionOpen();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(fixture.Root));
        var tools = provider.GetRequiredService<ToolRegistry>();

        var first = await InvokeAsync(tools, "search_symbols", new
        {
            query = "Widget",
            solution = fixture.SolutionPath
        });
        await File.WriteAllTextAsync(
            fixture.SourcePath,
            "namespace RefreshApp; public sealed class Widget { public int Changed => 2; }",
            Encoding.UTF8);
        var second = await InvokeAsync(tools, "search_symbols", new
        {
            query = "Widget",
            solution = fixture.SolutionPath
        });

        first.isError.ShouldBeFalse();
        second.isError.ShouldBeFalse();
        fixture.OpenCount.ShouldBe(2);
        Revision(second).ShouldNotBe(Revision(first));
    }

    [Fact]
    public async Task two_solutions_under_one_trusted_root_never_share_a_snapshot()
    {
        using var fixture = SemanticFixture.Create("FirstApp");
        using var other = fixture.CreateSiblingSolution("SecondApp");
        using var daemon = DaemonRuntime.UseExperimentOverride(false);
        using var open = SemanticFixture.OverrideSolutionOpen(fixture, other);
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(fixture.Root));
        var tools = provider.GetRequiredService<ToolRegistry>();

        var first = await InvokeAsync(tools, "search_symbols", new
        {
            query = "Widget",
            solution = fixture.SolutionPath
        });
        var second = await InvokeAsync(tools, "search_symbols", new
        {
            query = "Widget",
            solution = other.SolutionPath
        });

        first.isError.ShouldBeFalse();
        second.isError.ShouldBeFalse();
        fixture.OpenCount.ShouldBe(1);
        other.OpenCount.ShouldBe(1);
        Revision(first).ShouldNotBe(Revision(second));
    }

    [Fact]
    public async Task relative_solution_resolves_under_explicit_trusted_root_not_process_directory()
    {
        using var fixture = SemanticFixture.Create("RelativeApp");
        using var unrelated = new TemporaryDirectory();
        using var daemon = DaemonRuntime.UseExperimentOverride(false);
        using var open = fixture.OverrideSolutionOpen();
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(fixture.Root));
        var tools = provider.GetRequiredService<ToolRegistry>();
        var originalDirectory = Directory.GetCurrentDirectory();

        try
        {
            Directory.SetCurrentDirectory(unrelated.Path);
            var result = await InvokeAsync(tools, "outline", new
            {
                target = fixture.SourcePath,
                solution = Path.GetRelativePath(fixture.Root, fixture.SolutionPath)
            });

            result.isError.ShouldBeFalse();
            fixture.OpenCount.ShouldBe(1);
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Fact]
    public async Task relative_solution_cannot_escape_into_another_checkout()
    {
        using var trusted = SemanticFixture.Create("TrustedApp");
        using var outside = SemanticFixture.Create("OutsideApp");
        using var daemon = DaemonRuntime.UseExperimentOverride(false);
        using var open = SemanticFixture.OverrideSolutionOpen(trusted, outside);
        using var provider = McpTestHost.CreateProvider(new McpHostOptions(trusted.Root));

        var result = await InvokeAsync(
            provider.GetRequiredService<ToolRegistry>(),
            "outline",
            new
            {
                target = outside.SourcePath,
                solution = Path.GetRelativePath(trusted.Root, outside.SolutionPath)
            });

        result.isError.ShouldBeTrue();
        trusted.OpenCount.ShouldBe(0);
        outside.OpenCount.ShouldBe(0);
    }

    private static async Task<ToolResult> InvokeAsync(
        ToolRegistry tools,
        string tool,
        object arguments)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(arguments));
        return await tools.InvokeAsync(tool, document.RootElement);
    }

    private static string? Revision(ToolResult result)
    {
        using var document = JsonDocument.Parse(result.content);
        return document.RootElement
            .GetProperty("source")
            .GetProperty("revision")
            .GetString();
    }

    private static string? FlatRevision(ToolResult result)
    {
        using var document = JsonDocument.Parse(result.content);
        return document.RootElement.GetProperty("sourceRevision").GetString();
    }

    private sealed class SemanticFixture : IDisposable
    {
        private readonly TemporaryDirectory? _ownedRoot;
        private readonly Workspace _workspace;
        private readonly Solution _template;
        private int _openCount;

        private SemanticFixture(
            string root,
            string applicationName,
            TemporaryDirectory? ownedRoot)
        {
            Root = root;
            Symbol = $"{applicationName}.Widget";
            _ownedRoot = ownedRoot;
            var projectRoot = Path.Combine(root, applicationName);
            Directory.CreateDirectory(projectRoot);
            SolutionPath = Path.Combine(projectRoot, $"{applicationName}.slnx");
            SourcePath = Path.Combine(projectRoot, "Widget.cs");
            File.WriteAllText(SolutionPath, "<Solution />", Encoding.UTF8);
            File.WriteAllText(
                SourcePath,
                $"namespace {applicationName}; public sealed class Widget {{ public int Value => {applicationName.Length}; }}",
                Encoding.UTF8);
            var builder = new AdhocProjectBuilder(applicationName)
                .AddSource("Widget.cs", File.ReadAllText(SourcePath));
            (_workspace, var solution) = builder.Build();
            var document = solution.Projects.Single().Documents.Single();
            _template = solution
                .WithDocumentFilePath(document.Id, SourcePath)
                .WithDocumentText(
                    document.Id,
                    SourceText.From(File.ReadAllText(SourcePath), Encoding.UTF8));
            builder.Dispose();
            FirstOpenStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        internal string Root { get; }
        internal string Symbol { get; }
        internal string SolutionPath { get; }
        internal string SourcePath { get; }
        internal int OpenCount => Volatile.Read(ref _openCount);
        internal TaskCompletionSource FirstOpenStarted { get; }

        internal static SemanticFixture Create(string applicationName)
        {
            var root = new TemporaryDirectory();
            return new SemanticFixture(root.Path, applicationName, root);
        }

        internal SemanticFixture CreateSiblingSolution(string applicationName) =>
            new(Root, applicationName, ownedRoot: null);

        internal DelegateDisposable OverrideSolutionOpen(Func<Task>? beforeOpen = null) =>
            OverrideSolutionOpen([this], beforeOpen);

        internal static DelegateDisposable OverrideSolutionOpen(params SemanticFixture[] fixtures) =>
            OverrideSolutionOpen(fixtures, beforeOpen: null);

        private static DelegateDisposable OverrideSolutionOpen(
            IReadOnlyList<SemanticFixture> fixtures,
            Func<Task>? beforeOpen)
        {
            WorkspaceLoader.OpenSolutionOverride.Value = async (_, solutionPath) =>
            {
                var fixture = fixtures.Single(candidate => string.Equals(
                    candidate.SolutionPath,
                    solutionPath,
                    StringComparison.Ordinal));
                Interlocked.Increment(ref fixture._openCount);
                fixture.FirstOpenStarted.TrySetResult();
                if (beforeOpen is not null)
                    await beforeOpen();
                var text = await File.ReadAllTextAsync(fixture.SourcePath);
                var document = fixture._template.Projects.Single().Documents.Single();
                return fixture._template.WithDocumentText(
                    document.Id,
                    SourceText.From(text, Encoding.UTF8));
            };
            return new DelegateDisposable(() => WorkspaceLoader.OpenSolutionOverride.Value = null);
        }

        public void Dispose()
        {
            _workspace.Dispose();
            _ownedRoot?.Dispose();
        }
    }

    private sealed class DelegateDisposable(Action dispose) : IDisposable
    {
        private Action? _dispose = dispose;

        public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"rt-read-cache-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
