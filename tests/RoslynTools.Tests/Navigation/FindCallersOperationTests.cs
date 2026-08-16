using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindCallersOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_solution_and_returns_typed_callers()
    {
        using var project = new AdhocProjectBuilder("CallerApp");
        project.AddSource("Service.cs", """
            public class Service
            {
                public void Work() { }
            }
            """);
        project.AddSource("Program.cs", """
            public class Program
            {
                public void Run()
                {
                    new Service().Work();
                }
            }
            """);

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var sessionFactory = new FixedWorkspaceSessionFactory(solution);
            var operation = new FindCallersOperation(sessionFactory, new FindCallersService());

            var result = await operation.ExecuteAsync(
                new FindCallersRequest("Work", "requested.sln"));

            operation.CommandName.ShouldBe("find_callers");
            sessionFactory.LoadedSolutionPath.ShouldBe("requested.sln");
            result.IsOk.ShouldBeTrue();
            result.Match(callers => callers.Callers.ShouldHaveSingleItem().ContainingMethod,
                error => throw new InvalidOperationException(error.Message))
                .ShouldBe("Run");
        }
    }

    [Fact]
    public async Task ExecuteAsync_honors_include_tests_request_flag()
    {
        using var project = new AdhocProjectBuilder("CallerApp.Tests");
        project.AddSource("Service.cs", "public class Service { public void Work() { } }");
        project.AddSource("Test.cs", "public class Test { public void Run() => new Service().Work(); }");

        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new FindCallersOperation(
                new FixedWorkspaceSessionFactory(solution), new FindCallersService());

            var result = await operation.ExecuteAsync(new FindCallersRequest("Work"));

            result.Match(callers => callers.Callers.ShouldBeEmpty(),
                error => throw new InvalidOperationException(error.Message));
        }
    }

    private sealed class FixedWorkspaceSessionFactory(Solution solution) : IWorkspaceSessionFactory
    {
        public string? LoadedSolutionPath { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath)
        {
            LoadedSolutionPath = solutionPath;
            return Task.FromResult(solution);
        }

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            throw new NotSupportedException();
    }
}
