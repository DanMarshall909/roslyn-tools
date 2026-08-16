using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MakeSealedOperationTests
{
    [Fact]
    public async Task ExecuteAsync_returns_a_dry_run_mutation_envelope_with_changed_file()
    {
        using var project = new AdhocProjectBuilder("SealApp");
        project.AddSource("Types.cs", "class Standalone { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new MakeSealedOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new MakeSealedService(writer),
            writer);

        var result = await operation.ExecuteAsync(
            new MakeSealedRequest("Standalone", "ignored", DryRun: true));

        operation.CommandName.ShouldBe("make-sealed");
        result.Status.ShouldBe(MutationExecutionStatus.DryRun);
        result.Value!.Outcome.ShouldBe(MakeSealedOutcome.Sealed);
        result.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("Types.cs");
        writer.NewSolution.ShouldNotBeNull();
    }

    [Fact]
    public async Task SealAsync_returns_the_domain_result_before_adapter_rendering()
    {
        using var project = new AdhocProjectBuilder("SealApp");
        project.AddSource("Types.cs", "class Standalone { }");
        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var service = new MakeSealedService(writer);

        var result = await service.SealAsync(solution, "Standalone", dryRun: true);

        result.Outcome.ShouldBe(MakeSealedOutcome.Sealed);
        result.Message.ShouldBeNull();
        writer.NewSolution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(true);
    }

    [Fact]
    public async Task SealAsync_returns_a_no_op_domain_result_without_writing()
    {
        using var project = new AdhocProjectBuilder("SealApp");
        project.AddSource("Types.cs", "sealed class AlreadySealed { }");
        var (_, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var service = new MakeSealedService(writer);

        var result = await service.SealAsync(solution, "AlreadySealed", dryRun: false);

        result.Outcome.ShouldBe(MakeSealedOutcome.AlreadySealed);
        result.Message.ShouldNotBeNull();
        result.Message!.ShouldContain("already sealed", Case.Insensitive);
        writer.NewSolution.ShouldBeNull();
    }

    private sealed class FixedWorkspaceSessionFactory(
        Microsoft.CodeAnalysis.Workspace workspace,
        Microsoft.CodeAnalysis.Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(string? solutionPath) =>
            Task.FromResult(solution);

        public Task<(Microsoft.CodeAnalysis.Workspace Workspace, Microsoft.CodeAnalysis.Solution Solution)>
            LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
