using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class TransformParametersOperationTests
{
    [Fact]
    public async Task new_type_preview_reports_changes_without_writing()
    {
        using var project = new AdhocProjectBuilder("TransformApp");
        project.AddSource(
            "Service.cs",
            "namespace TransformApp; class Service { void Run(string name, int count) { } }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new TransformParametersRequest(
            "Service.Run",
            NewType: "RunArgs",
            DryRun: true))).Unwrap();

        operation.Definition.Name.ShouldBe("transform_parameters");
        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.ReusedExistingType.ShouldBeFalse();
        execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("Service.cs");
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task existing_type_mode_applies_through_the_writer()
    {
        using var project = new AdhocProjectBuilder("TransformApp");
        project.AddSource(
            "Service.cs",
            "namespace TransformApp; record RunArgs(string Name, int Count); " +
            "class Service { void Run(string name, int count) { } }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new TransformParametersRequest(
            "Service.Run",
            ExistingType: "TransformApp.RunArgs"))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.ReusedExistingType.ShouldBeTrue();
        writer.NewSolution.ShouldNotBeNull();
    }

    [Fact]
    public async Task modes_are_mutually_exclusive_before_workspace_loading()
    {
        using var project = new AdhocProjectBuilder("TransformApp");
        project.AddSource("Service.cs", "class Service { void Run(string name) { } }");
        var (workspace, solution) = project.Build();
        var operation = CreateOperation(
            workspace,
            solution,
            new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new TransformParametersRequest(
            "Service.Run",
            NewType: "RunArgs",
            ExistingType: "TransformApp.RunArgs"));

        result.IsErr.ShouldBeTrue();
        result.Match(_ => string.Empty, error => error.Message)
            .ShouldContain("mutually exclusive");
    }

    private static TransformParametersOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new TransformParametersOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new TransformParametersService(
                new RtConsoleLogger<TransformParametersService>(),
                writer),
            writer);
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
