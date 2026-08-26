using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class AddDependencyOperationTests
{
    [Fact]
    public async Task PrepareAsync_returns_an_ambiguous_target_without_setting_process_exit_code()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         namespace App.Sales { public class AgentService { } }
                                         namespace App.Support { public class AgentService { } }
                                         """);

        var (_, solution) = project.Build();
        var originalExitCode = Environment.ExitCode;
        try
        {
            Environment.ExitCode = 0;
            var result = await new AddDependencyService(
                    NullLogger<AddDependencyService>.Instance,
                    new CapturingSolutionChangeWriter())
                .PrepareAsync(solution, "AgentService", "IEmailService", null);

            result.Outcome.ShouldBe(AddDependencyOutcome.TargetAmbiguous);
            result.Changed.ShouldBeFalse();
            result.Message.ShouldNotBeNull();
            result.Message!.ShouldContain("Pass a fully qualified type name.");
            Environment.ExitCode.ShouldBe(0);
        }
        finally
        {
            Environment.ExitCode = originalExitCode;
        }
    }

    [Fact]
    public async Task ExecuteAsync_returns_a_dry_run_mutation_envelope_with_changed_file()
    {
        using var project = new AdhocProjectBuilder("DependencyApp");
        project.AddSource("AgentService.cs", """
                                             interface IEmailService { }
                                             class AgentService { }
                                             """);
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDependencyOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);

        var result = await operation.ExecuteAsync(
            new AddDependencyRequest("AgentService", "IEmailService", DryRun: true));

        result.Status.ShouldBe(MutationExecutionStatus.DryRun);
        result.Value!.Outcome.ShouldBe(AddDependencyOutcome.Applied);
        result.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("AgentService.cs");
        writer.DryRun.ShouldBe(true);
    }

    [Fact]
    public async Task InvokeAsync_returns_failure_for_an_ambiguous_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         namespace App.Sales { public class AgentService { } }
                                         namespace App.Support { public class AgentService { } }
                                         """);
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDependencyOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);

        var result = await ((ICommandApplicationPort)operation).InvokeAsync(
            new AddDependencyRequest("AgentService", "IEmailService"));

        result.IsError.ShouldBeTrue();
        result.Value.ShouldBeNull();
        result.Error!.ShouldContain("ambiguous", Case.Insensitive);
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
