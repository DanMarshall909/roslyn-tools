using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class ExtractMethodOperationTests
{
    [Fact]
    public async Task ExecuteAsync_applies_the_prepared_extraction_when_requested()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new ExtractMethodOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            writer);

        var execution = (await operation.ExecuteAsync(CreateRequest(project))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.Changed.ShouldBeTrue();
        execution.AffectedPaths.ShouldHaveSingleItem().ShouldEndWith("Calculator.cs");
        writer.Solution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
    }

    [Fact]
    public async Task ExecuteAsync_previews_without_writing_when_dry_run_requested()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new ExtractMethodOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            writer);

        var execution = (await operation.ExecuteAsync(CreateRequest(project, dryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.Changed.ShouldBeTrue();
        execution.AffectedPaths.ShouldHaveSingleItem().ShouldEndWith("Calculator.cs");
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_returns_typed_invalid_range_before_loading_the_workspace()
    {
        var operation = new ExtractMethodOperation(
            new ThrowingWorkspaceSessionFactory(),
            new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new ExtractMethodRequest(
            "Calculator.cs",
            "Calculate",
            0,
            1));

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => null!, failure => failure);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("startLine", Case.Sensitive);
        error.Context!["lineRange"].ShouldBe("0-1");
    }

    [Fact]
    public async Task ExecuteAsync_returns_typed_not_found_for_a_missing_source_file()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new ExtractMethodOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            writer);

        var missingFile = Path.Combine(project.ProjectDir, "Missing.cs");
        var result = await operation.ExecuteAsync(new ExtractMethodRequest(
            missingFile,
            "Calculate",
            7,
            8,
            DryRun: true));

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => null!, failure => failure);
        error.Kind.ShouldBe(RtErrorKind.NotFound);
        error.Message.ShouldContain("File not found in solution", Case.Sensitive);
        error.Context!["file"].ShouldBe(missingFile);
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_cancellation_before_dispatch_cannot_write()
    {
        using var project = CreateProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new ExtractMethodOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            CreateRequest(project),
            cancellation.Token));

        writer.Solution.ShouldBeNull();
    }

    private static AdhocProjectBuilder CreateProject()
    {
        return new AdhocProjectBuilder("ExtractMethodPort")
            .AddSource("Calculator.cs", """
                                        namespace TestApp;

                                        public class Calculator
                                        {
                                            public void Process(int x, int y)
                                            {
                                                var sum = x + y;
                                                System.Console.WriteLine(sum);
                                            }
                                        }
                                        """);
    }

    private static ExtractMethodRequest CreateRequest(AdhocProjectBuilder project, bool dryRun = false) => new(
        Path.Combine(project.ProjectDir, "Calculator.cs"),
        "Calculate",
        7,
        8,
        DryRun: dryRun);

    private sealed class FixedWorkspaceSessionFactory(
        Microsoft.CodeAnalysis.Workspace workspace,
        Microsoft.CodeAnalysis.Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(string? solutionPath) =>
            Task.FromResult(solution);

        public Task<(Microsoft.CodeAnalysis.Workspace Workspace, Microsoft.CodeAnalysis.Solution Solution)>
            LoadWorkspaceAsync(string? solutionPath) => Task.FromResult((workspace, solution));
    }

    private sealed class ThrowingWorkspaceSessionFactory : IWorkspaceSessionFactory
    {
        public Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(string? solutionPath) =>
            throw new InvalidOperationException("Workspace loading must not start after request validation.");

        public Task<(Microsoft.CodeAnalysis.Workspace Workspace, Microsoft.CodeAnalysis.Solution Solution)>
            LoadWorkspaceAsync(string? solutionPath) =>
            throw new InvalidOperationException("Workspace loading must not start after request validation.");
    }
}
