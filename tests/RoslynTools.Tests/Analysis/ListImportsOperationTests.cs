using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class ListImportsOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_requested_solution_and_returns_typed_imports()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", "global using System;\nusing static System.Math;\nusing Alias = System.String;\nclass C { }");
        var (workspace, solution) = project.Build();
        var sessionFactory = new CapturingWorkspaceSessionFactory(workspace, solution);
        var operation = new ListImportsOperation(sessionFactory, new ImportDirectiveService());

        var result = await operation.ExecuteAsync(new ListImportsRequest("requested.slnx", "all"));

        operation.Definition.Name.ShouldBe("list_imports");
        sessionFactory.RequestedSolution.ShouldBe("requested.slnx");
        var response = result.Match(value => value, error => throw new ShouldAssertException(error.Message));
        response.Imports.Count.ShouldBe(3);
        response.Imports.Select(importDirective => importDirective.Kind)
            .ShouldBe(["global", "static", "alias"]);
        response.Imports.Single(importDirective => importDirective.IsGlobal).Name.ShouldBe("System");
        response.Imports.Single(importDirective => importDirective.IsStatic).Name.ShouldBe("System.Math");
    }

    [Fact]
    public async Task ExecuteAsync_applies_kind_filter()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", "global using System;\nusing static System.Math;\nusing Alias = System.String;\nclass C { }");
        var (workspace, solution) = project.Build();
        var operation = new ListImportsOperation(
            new CapturingWorkspaceSessionFactory(workspace, solution),
            new ImportDirectiveService());

        var result = await operation.ExecuteAsync(new ListImportsRequest(ImportKind: "global"));

        result.Match(value => value, error => throw new ShouldAssertException(error.Message))
            .Imports.ShouldHaveSingleItem().Name.ShouldBe("System");
    }

    private sealed class CapturingWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public string? RequestedSolution { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult((workspace, solution));
        }
    }
}
