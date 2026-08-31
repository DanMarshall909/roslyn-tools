using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class AddStaticImportOperationTests
{
    [Fact]
    public async Task dry_run_reports_the_target_without_writing()
    {
        using var project = new AdhocProjectBuilder("StaticImportsApp");
        project.AddSource("Program.cs", "class Program { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new AddStaticImportRequest(
            Path.Combine(project.ProjectDir, "Program.cs"),
            "System.Console",
            DryRun: true))).Unwrap();

        operation.Definition.Name.ShouldBe("add_static_import");
        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.Changed.ShouldBeTrue();
        execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("Program.cs");
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task existing_import_without_simplification_is_a_no_op()
    {
        using var project = new AdhocProjectBuilder("StaticImportsApp");
        project.AddSource("Program.cs", "using static System.Console; class Program { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new AddStaticImportRequest(
            Path.Combine(project.ProjectDir, "Program.cs"),
            "System.Console"))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("already exists");
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task existing_import_can_still_apply_simplification_edits()
    {
        using var project = new AdhocProjectBuilder("StaticImportsApp");
        project.AddSource(
            "Program.cs",
            "using static System.Console; class Program { void Run() { System.Console.WriteLine(\"x\"); } }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(new AddStaticImportRequest(
            Path.Combine(project.ProjectDir, "Program.cs"),
            "System.Console",
            DryRun: false,
            Simplify: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        var text = await writer.NewSolution!.Projects.Single().Documents.Single().GetTextAsync();
        text.ToString().ShouldContain("WriteLine(\"x\")", Case.Sensitive);
        text.ToString().ShouldNotContain("System.Console.WriteLine", Case.Sensitive);
    }

    private static AddStaticImportOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new AddStaticImportOperation(
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
