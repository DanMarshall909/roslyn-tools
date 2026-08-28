using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class ExtractClassOperationTests
{
    [Fact]
    public async Task apply_moves_members_injects_the_dependency_and_returns_metadata()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new ExtractClassRequest(
            "Widget",
            "Format",
            "WidgetFormatter"))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.Type.ShouldBe("TestApp.Widget");
        execution.Value.Members.ShouldBe(["Format"]);
        execution.Value.NewType.ShouldBe("WidgetFormatter");
        execution.Value.IsStatic.ShouldBeFalse();
        execution.Value.NewFilePath.ShouldEndWith("WidgetFormatter.cs", Case.Sensitive);
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("Widget.cs", StringComparison.Ordinal));
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("WidgetFormatter.cs", StringComparison.Ordinal));
        writer.NewSolution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
    }

    [Fact]
    public async Task dry_run_reports_both_documents_without_writing()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new ExtractClassRequest(
            "Widget",
            "Format",
            "WidgetFormatter",
            DryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("Widget.cs", StringComparison.Ordinal));
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("WidgetFormatter.cs", StringComparison.Ordinal));
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task returns_typed_errors_for_missing_classes_and_members()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var operation = CreateOperation(workspace, solution, new CapturingSolutionChangeWriter());

        var missingClass = await operation.ExecuteAsync(new ExtractClassRequest(
            "Missing", "Format", "WidgetFormatter"));
        var missingMember = await operation.ExecuteAsync(new ExtractClassRequest(
            "Widget", "Missing", "WidgetFormatter"));

        var classError = GetError(missingClass);
        classError.Kind.ShouldBe(RtErrorKind.NotFound);
        classError.Message.ShouldBe("Class 'Missing' not found in solution.");

        var memberError = GetError(missingMember);
        memberError.Kind.ShouldBe(RtErrorKind.InvalidInput);
        memberError.Message.ShouldContain("None of the specified members (Missing) were found", Case.Sensitive);
    }

    [Fact]
    public async Task cancellation_before_dispatch_cannot_write_the_extraction()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new ExtractClassRequest("Widget", "Format", "WidgetFormatter"),
            cancellation.Token));

        writer.NewSolution.ShouldBeNull();
    }

    private static AdhocProjectBuilder CreateProject()
    {
        var project = new AdhocProjectBuilder("ExtractClassApp");
        project.AddSource("Widget.cs", """
                                       namespace TestApp;
                                       public class Widget
                                       {
                                           public void Format() { }
                                       }
                                       """);
        return project;
    }

    private static ExtractClassOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer) => new(
        new FixedWorkspaceSessionFactory(workspace, solution),
        new ExtractClassService(),
        writer);

    private static RtError GetError(Result<MutationExecutionResult<ExtractClassResult>, RtError> result) =>
        result.Match(
            _ => throw new ShouldAssertException("Expected class extraction to fail."),
            error => error);

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
