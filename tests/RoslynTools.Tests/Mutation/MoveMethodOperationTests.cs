using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MoveMethodOperationTests
{
    [Fact]
    public async Task apply_rewrites_call_sites_and_returns_structured_metadata()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new MoveMethodRequest(
            "App.Source.Add",
            "App.Target",
            DryRun: false))).Unwrap();

        operation.Definition.Name.ShouldBe("move_method");
        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.MethodName.ShouldBe("Add");
        execution.Value.CallSiteCount.ShouldBe(1);
        execution.Value.CreatedTargetFile.ShouldBeFalse();
        execution.ChangedFiles.Count.ShouldBe(3);
        writer.NewSolution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
    }

    [Fact]
    public async Task dry_run_reports_changes_without_crossing_the_writer_boundary()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new MoveMethodRequest(
            "App.Source.Add",
            "App.Target",
            DryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.Count.ShouldBe(3);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task create_file_previews_the_new_target_document()
    {
        using var project = new AdhocProjectBuilder("MoveApp");
        project.AddSource("Source.cs", "namespace App; public static class Source { public static int Add(int x) => x; }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new MoveMethodRequest(
            "App.Source.Add",
            "App.NewTarget",
            CreateFile: true,
            DryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.CreatedTargetFile.ShouldBeTrue();
        execution.Value.CreatedTargetFileName.ShouldBe("NewTarget.cs");
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("NewTarget.cs", StringComparison.Ordinal));
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task rejection_returns_a_typed_error_without_writing()
    {
        using var project = new AdhocProjectBuilder("MoveApp");
        project.AddSource("Source.cs", "namespace App; public class Source { public int Add(int x) => x; }");
        project.AddSource("Target.cs", "namespace App; public class Target { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new MoveMethodRequest("App.Source.Add", "App.Target"));

        var error = GetError(result);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("not static", Case.Insensitive);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task cancellation_before_write_cannot_apply_the_move()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new MoveMethodRequest("App.Source.Add", "App.Target"),
            cancellation.Token));

        writer.NewSolution.ShouldBeNull();
    }

    private static AdhocProjectBuilder CreateProject()
    {
        var project = new AdhocProjectBuilder("MoveApp");
        project.AddSource("Source.cs", "namespace App; public static class Source { public static int Add(int x) => x; }");
        project.AddSource("Target.cs", "namespace App; public static class Target { }");
        project.AddSource("Caller.cs", "namespace App; public class Caller { public int Run() => Source.Add(1); }");
        return project;
    }

    private static MoveMethodOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new MoveMethodOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new MoveMethodService(),
            writer);
    }

    private static RtError GetError(Result<MutationExecutionResult<MoveMethodResult>, RtError> result)
    {
        return result.Match(
            _ => throw new ShouldAssertException("Expected move-method to fail."),
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
