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
            var envelope = result.Match(value => value, error => throw new InvalidOperationException(error.Message));
            envelope.SchemaVersion.ShouldBe("rt.result.v1");
            envelope.Operation.ShouldBe("outline");
            envelope.Outcome.ShouldBe("Succeeded");
            var summary = envelope.Data.Summaries!.ShouldHaveSingleItem();
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
            var envelope = result.Match(value => value, error => throw new InvalidOperationException(error.Message));
            envelope.Data.Types!
                .ShouldContain(type => type.Name == "Order");
            envelope.Paging.TotalItems.ShouldBe(1);
            envelope.Paging.Truncated.ShouldBeFalse();
            operation.Definition.ResponseType.ShouldBe(typeof(RtResultEnvelope<OutlineResult>));
        }
    }

    [Fact]
    public async Task partial_workspace_is_visible_and_reduces_confidence()
    {
        using var project = new AdhocProjectBuilder("PartialOutlineApp");
        project.AddSource(
            "Broken.cs",
            "namespace PartialOutlineApp; public class Broken { MissingType Value = null!; }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(
                new FixedSessionFactory(workspace, solution),
                new OutlineService());

            var result = await operation.ExecuteAsync(
                new OutlineRequest(Path.Combine(project.ProjectDir, "Broken.cs")));
            var envelope = result.Unwrap();

            envelope.Outcome.ShouldBe("Partial");
            envelope.Workspace.State.ShouldBe("Partial");
            envelope.Confidence.Level.ShouldBe("Limited");
            envelope.Diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains(
                "CS0246",
                StringComparison.Ordinal));
            envelope.Limitations.ShouldNotBeEmpty();
        }
    }

    [Fact]
    public async Task output_is_bounded_and_reports_truthful_paging_metadata()
    {
        using var project = new AdhocProjectBuilder("LargeOutlineApp");
        var declarations = string.Join(
            Environment.NewLine,
            Enumerable.Range(1, 105).Select(index => $"public class Type{index} {{ }}"));
        project.AddSource("Many.cs", declarations);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new OutlineOperation(
                new FixedSessionFactory(workspace, solution),
                new OutlineService());

            var result = await operation.ExecuteAsync(
                new OutlineRequest(Path.Combine(project.ProjectDir, "Many.cs")));
            var envelope = result.Unwrap();

            envelope.Paging.TotalItems.ShouldBe(105);
            envelope.Paging.ReturnedItems.ShouldBe(100);
            envelope.Paging.Truncated.ShouldBeTrue();
            envelope.Paging.ContinuationToken.ShouldBeNull();
            envelope.Data.Types!.Count.ShouldBe(100);
            envelope.Limitations.ShouldContain(limitation => limitation.Contains(
                "item limit",
                StringComparison.Ordinal));
        }
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);
        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) => Task.FromResult((workspace, solution));
    }
}
