using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class ExtractInterfaceOperationTests
{
    [Fact]
    public async Task apply_writes_the_interface_and_returns_structured_metadata()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new ExtractInterfaceRequest("Widget"))).Unwrap();

        operation.Definition.Name.ShouldBe("extract_interface");
        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.Interface.ShouldBe("IWidget");
        execution.Value.MemberCount.ShouldBe(1);
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("IWidget.cs", StringComparison.Ordinal));
        writer.NewSolution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
    }

    [Fact]
    public async Task dry_run_reports_the_new_document_without_writing()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new ExtractInterfaceRequest(
            "Widget",
            DryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("Widget.cs", StringComparison.Ordinal));
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("IWidget.cs", StringComparison.Ordinal));
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task returns_a_typed_not_found_error_for_an_unknown_class()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var operation = CreateOperation(workspace, solution, new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new ExtractInterfaceRequest("Missing"));

        var error = GetError(result);
        error.Kind.ShouldBe(RtErrorKind.NotFound);
        error.Message.ShouldContain("No symbol named 'Missing'", Case.Sensitive);
    }

    [Fact]
    public async Task returns_a_typed_rejection_for_a_non_class_type()
    {
        using var project = new AdhocProjectBuilder("InterfaceApp");
        project.AddSource("Point.cs", "namespace TestApp; public struct Point { public int X; }");
        var (workspace, solution) = project.Build();
        var operation = CreateOperation(workspace, solution, new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new ExtractInterfaceRequest("Point"));

        var error = GetError(result);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("must be a class", Case.Sensitive);
    }

    [Fact]
    public async Task cancellation_before_dispatch_cannot_write_the_interface()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new ExtractInterfaceRequest("Widget"),
            cancellation.Token));

        writer.NewSolution.ShouldBeNull();
    }

    private static AdhocProjectBuilder CreateProject()
    {
        var project = new AdhocProjectBuilder("InterfaceApp");
        project.AddSource("Widget.cs", """
                                        namespace TestApp;
                                        public class Widget
                                        {
                                            public void Run() { }
                                            private void Hidden() { }
                                        }
                                        """);
        return project;
    }

    private static ExtractInterfaceOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new ExtractInterfaceOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new ExtractInterfaceService(new SymbolResolutionService()),
            writer);
    }

    private static RtError GetError(Result<MutationExecutionResult<ExtractInterfaceResult>, RtError> result)
    {
        return result.Match(
            _ => throw new ShouldAssertException("Expected interface extraction to fail."),
            error => error);
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
