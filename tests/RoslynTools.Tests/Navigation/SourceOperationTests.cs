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
            var operation = new SourceOperation(factory, new InspectService());

            var result = await operation.ExecuteAsync(new SourceRequest(
                "App.Calculator.Add", "requested.slnx", "method"));

            operation.CommandName.ShouldBe("source");
            factory.RequestedSolution.ShouldBe("requested.slnx");
            var entry = result.ShouldHaveSingleItem();
            entry["fqn"].ShouldBe("App.Calculator.Add(int, int)");
            entry["source"].ShouldBe("""
                /// <summary>Adds two values.</summary>
                public int Add(int left, int right) => left + right;
                """);
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
