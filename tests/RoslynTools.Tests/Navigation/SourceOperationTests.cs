using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class SourceOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_requested_solution_and_returns_symbol_content()
    {
        using var project = new AdhocProjectBuilder("SourceApp");
        project.AddSource("Calculator.cs", """
            namespace App;
            public class Calculator
            {
                /// <summary>Adds two values.</summary>
                public int Add(int left, int right) => left + right;
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var factory = new CapturingSessionFactory(solution);
            var operation = new SourceOperation(factory, new InspectService(), new SymbolCandidateService());

            var result = await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add", "requested.slnx", "method"));
            var source = result.Unwrap();

            operation.Definition.Name.ShouldBe("source");
            factory.RequestedSolution.ShouldBe("requested.slnx");
            source.Subject.ShouldBe("App.Calculator.Add(int, int)");
            var entry = source.Declarations.ShouldHaveSingleItem();
            entry.FullyQualifiedName.ShouldBe("App.Calculator.Add(int, int)");
            entry.Text.ReplaceLineEndings("\n").ShouldBe("""
                /// <summary>Adds two values.</summary>
                public int Add(int left, int right) => left + right;
                """.ReplaceLineEndings("\n"));
            source.RelatedSymbols.ShouldBeEmpty();
            source.RelatedSymbolsTruncated.ShouldBeFalse();
        }
    }

    [Fact]
    public async Task ExecuteAsync_lists_related_symbols_without_including_their_content_by_default()
    {
        using var project = CreateRelatedSourceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var source = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add", Kind: "method"))).Unwrap();

            var related = source.RelatedSymbols.ShouldHaveSingleItem();
            related.FullyQualifiedName.ShouldBe("App.IFormatter.Format(int)");
            related.Kind.ShouldBe("Method");
            related.Declarations.ShouldBeNull();
        }
    }

    [Fact]
    public async Task ExecuteAsync_includes_one_hop_related_symbol_content_when_requested()
    {
        using var project = CreateRelatedSourceProject();
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var source = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add",
                Kind: "method",
                IncludeRelated: true))).Unwrap();

            var declaration = source.RelatedSymbols.ShouldHaveSingleItem()
                .Declarations.ShouldNotBeNull()
                .ShouldHaveSingleItem();
            declaration.Text.ShouldBe("string Format(int value);");
        }
    }

    [Fact]
    public async Task ExecuteAsync_bounds_related_symbols_and_reports_truncation()
    {
        using var project = new AdhocProjectBuilder("SourceApp");
        project.AddSource("Calculator.cs", """
            namespace App;
            public static class First { public static int Get() => 1; }
            public static class Second { public static int Get() => 2; }
            public sealed class Calculator
            {
                public int Add() => First.Get() + Second.Get();
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new SourceOperation(
                new CapturingSessionFactory(solution),
                new InspectService(),
                new SymbolCandidateService());

            var source = (await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add",
                Kind: "method",
                RelatedLimit: 1))).Unwrap();

            source.RelatedSymbols.ShouldHaveSingleItem()
                .FullyQualifiedName.ShouldBe("App.First.Get()");
            source.RelatedSymbolsTruncated.ShouldBeTrue();
        }
    }

    [Fact]
    public void constructor_requires_symbol_candidate_service()
    {
        Should.Throw<ArgumentNullException>(() =>
            new SourceOperation(
                new CapturingSessionFactory(null!),
                new InspectService(),
                null!));
    }

    [Fact]
    public async Task ExecuteAsync_honors_cancellation_before_loading_solution()
    {
        using var project = new AdhocProjectBuilder("SourceApp");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var factory = new CapturingSessionFactory(solution);
            var operation = new SourceOperation(factory, new InspectService(), new SymbolCandidateService());

            await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
                new SourceRequest("App.Missing", "requested.slnx"),
                new CancellationToken(canceled: true)));

            factory.RequestedSolution.ShouldBeNull();
        }
    }

    private static AdhocProjectBuilder CreateRelatedSourceProject() =>
        new AdhocProjectBuilder("SourceApp")
            .AddSource("Calculator.cs", """
                namespace App;
                public interface IFormatter
                {
                    string Format(int value);
                }

                public sealed class Calculator(IFormatter formatter)
                {
                    public string Add(int left, int right) => formatter.Format(left + right);
                }
                """);

    private sealed class CapturingSessionFactory(Solution solution) : IWorkspaceSessionFactory
    {
        public string? RequestedSolution { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult(solution);
        }

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult((solution.Workspace, solution));
        }
    }
}
