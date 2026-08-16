using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class CircularDepsOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_the_requested_solution_and_returns_typed_cycles()
    {
        using var project = new AdhocProjectBuilder("CycleApp");
        project.AddSource("A.cs", "namespace CycleApp.A { using CycleApp.B; class A { } }");
        project.AddSource("B.cs", "namespace CycleApp.B { using CycleApp.A; class B { } }");
        var (workspace, solution) = project.Build();
        var sessionFactory = new FixedWorkspaceSessionFactory(workspace, solution);
        var operation = new CircularDepsOperation(sessionFactory, new CircularDepsService());

        var result = await operation.ExecuteAsync(
            new CircularDepsRequest(Solution: "requested.sln", Scope: "CycleApp"));

        operation.CommandName.ShouldBe("circular-deps");
        sessionFactory.LoadedSolutionPath.ShouldBe("requested.sln");
        result.HasCycles.ShouldBeTrue();
        result.Cycles.ShouldHaveSingleItem().Path.ShouldBe("CycleApp.A -> CycleApp.B -> CycleApp.A");
    }

    [Fact]
    public async Task ExecuteAsync_returns_a_typed_empty_result_when_the_scope_has_no_cycles()
    {
        using var project = new AdhocProjectBuilder("Mixed");
        project.AddSource("A.cs", "namespace CycleApp.A { using CycleApp.B; class A { } }");
        project.AddSource("B.cs", "namespace CycleApp.B { using CycleApp.A; class B { } }");
        project.AddSource("Clean.cs", "namespace Clean { class Value { } }");
        var (workspace, solution) = project.Build();
        var operation = new CircularDepsOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new CircularDepsService());

        var result = await operation.ExecuteAsync(new CircularDepsRequest(Scope: "Clean"));

        result.HasCycles.ShouldBeFalse();
        result.Cycles.ShouldBeEmpty();
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public string? LoadedSolutionPath { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath) =>
            Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            LoadedSolutionPath = solutionPath;
            return Task.FromResult((workspace, solution));
        }
    }
}
