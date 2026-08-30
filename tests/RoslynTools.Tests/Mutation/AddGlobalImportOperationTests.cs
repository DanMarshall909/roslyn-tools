using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class AddGlobalImportOperationTests
{
    [Fact]
    public async Task dry_run_reports_the_changed_file_without_writing()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("GlobalUsings.cs", "global using System;");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new AddGlobalImportRequest(
            Path.Combine(project.ProjectDir, "GlobalUsings.cs"),
            "System.Text",
            DryRun: true));
        var execution = result.Unwrap();

        operation.Definition.Name.ShouldBe("add_global_import");
        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.Changed.ShouldBeTrue();
        execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("GlobalUsings.cs");
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task apply_crosses_the_writer_boundary_once()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("GlobalUsings.cs", "global using System;");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new AddGlobalImportRequest(
            Path.Combine(project.ProjectDir, "GlobalUsings.cs"),
            "System.Text"));
        var execution = result.Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        writer.NewSolution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
    }

    [Fact]
    public async Task existing_global_using_returns_a_no_op_without_writing()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("GlobalUsings.cs", "global using System.Text;");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new AddGlobalImportRequest(
            Path.Combine(project.ProjectDir, "GlobalUsings.cs"),
            "System.Text"));
        var execution = result.Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.Changed.ShouldBeFalse();
        execution.Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("already exists");
        writer.NewSolution.ShouldBeNull();
    }

    private static AddGlobalImportOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new AddGlobalImportOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new ImportDirectiveService(
                new RtConsoleLogger<ImportDirectiveService>(),
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
