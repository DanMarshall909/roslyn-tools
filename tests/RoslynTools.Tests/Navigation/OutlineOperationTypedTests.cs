using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class OutlineOperationTypedTests
{
    [Fact]
    public async Task summary_mode_preserves_typed_entries()
    {
        using var project = new AdhocProjectBuilder("OutlineApp");
        project.AddSource("Order.cs", """
            namespace OutlineApp;
            /// <summary>an order</summary>
            public class Order { public void Cancel() { } }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(new FixedSessionFactory(workspace, solution), new OutlineService());
            var result = await operation.ExecuteAsync(new OutlineRequest(Path.Combine(project.ProjectDir, "Order.cs"), Summary: true));
            var summary = result.Match(value => value, error => throw new InvalidOperationException(error.Message)).Summaries!.ShouldHaveSingleItem();
            summary.Fqn.ShouldContain("Order");
            summary.Kind.ShouldBe("Class");
            summary.LineFrom.ShouldBeGreaterThan(0);
            summary.LineTo.ShouldBeGreaterThanOrEqualTo(summary.LineFrom);
            summary.Description.ShouldBe("an order");
        }
    }

    [Fact]
    public async Task normal_mode_preserves_outline_type_entries_and_definition_shape()
    {
        using var project = new AdhocProjectBuilder("OutlineApp");
        project.AddSource("Order.cs", "namespace OutlineApp; public class Order { public void Cancel() { } }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(new FixedSessionFactory(workspace, solution), new OutlineService());
            var result = await operation.ExecuteAsync(new OutlineRequest(Path.Combine(project.ProjectDir, "Order.cs")));
            result.Match(value => value, error => throw new InvalidOperationException(error.Message)).Types!
                .ShouldContain(type => type.Name == "Order");
            operation.Definition.ResponseType.ShouldBe(typeof(OutlineResult));
        }
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);
        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) => Task.FromResult((workspace, solution));
    }
}
