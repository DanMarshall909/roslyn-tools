using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class SourceOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_requested_solution_and_preserves_source_result_shape()
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
            var operation = new SourceOperation(factory, new InspectService(), new SymbolResolutionService());

            var result = await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add", "requested.slnx", "method"));

            operation.CommandName.ShouldBe("source");
            factory.RequestedSolution.ShouldBe("requested.slnx");
            var entry = result.ShouldHaveSingleItem();
            entry.FullyQualifiedName.ShouldBe("App.Calculator.Add(int, int)");
            entry.Text.ShouldBe("""
                /// <summary>Adds two values.</summary>
                public int Add(int left, int right) => left + right;
                """);
        }
    }

    [Fact]
    public void constructor_requires_symbol_resolution_service()
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
            var operation = new SourceOperation(factory, new InspectService(), new SymbolResolutionService());

            await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
                new SourceRequest("App.Missing", "requested.slnx"),
                new CancellationToken(canceled: true)));

            factory.RequestedSolution.ShouldBeNull();
        }
    }

    private sealed class CapturingSessionFactory(Solution solution) : IWorkspaceSessionFactory
    {
        public string? RequestedSolution { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult(solution);
        }

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            throw new NotSupportedException();
    }
}
