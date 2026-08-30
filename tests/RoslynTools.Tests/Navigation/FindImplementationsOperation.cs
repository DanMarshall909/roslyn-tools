using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindImplementationsOperationTests
{
    [Fact]
    public async Task resolves_fully_qualified_type_and_returns_legacy_result_shape()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Workers.cs", """
                                        namespace App;
                                        public interface IWorker { void Work(); }
                                        public sealed class Worker : IWorker { public void Work() { } }
                                        """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var factory = new CapturingSessionFactory(solution);
            var operation = new FindImplementationsOperation(
                factory,
                new FindImplementationsService(),
                new SymbolResolutionService());

            var result = await operation.ExecuteAsync(new FindImplementationsRequest(
                "App.IWorker", "requested.slnx"));
            var response = result.Unwrap();

            operation.Definition.Name.ShouldBe("find_impl");
            factory.RequestedSolution.ShouldBe("requested.slnx");
            response.Symbol.ShouldBe("App.IWorker");
            response.SymbolKind.ShouldBe("Interface");
            response.ImplementationCount.ShouldBe(1);
            response.Implementations[0].Fqn.ShouldBe("App.Worker");
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

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult((solution.Workspace, solution));
        }
    }
}
