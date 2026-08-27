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

        var result = (await operation.ExecuteAsync(
            new AddDependencyRequest("AgentService", "IEmailService"))).Unwrap();

        result.Status.ShouldBe(MutationExecutionStatus.DryRun);
        result.Value!.Outcome.ShouldBe(AddDependencyOutcome.Applied);
        result.AffectedPaths.ShouldHaveSingleItem().ShouldEndWith("AgentService.cs");
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_applies_the_prepared_change_when_requested()
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

        var result = (await operation.ExecuteAsync(
            new AddDependencyRequest("AgentService", "IEmailService", DryRun: false))).Unwrap();

        result.Status.ShouldBe(MutationExecutionStatus.Applied);
        result.AffectedPaths.ShouldHaveSingleItem().ShouldEndWith("AgentService.cs");
        writer.Solution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
    }

    [Fact]
    public async Task ExecuteAsync_returns_an_informational_no_op_when_the_dependency_already_exists()
    {
        using var project = new AdhocProjectBuilder("DependencyApp");
        project.AddSource("AgentService.cs", """
                                             interface IEmailService { }
                                             class AgentService
                                             {
                                                 private readonly IEmailService _emailService;

                                                 public AgentService(IEmailService emailService)
                                                 {
                                                     _emailService = emailService;
                                                 }
                                             }
                                             """);
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDependencyOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);

        var result = (await operation.ExecuteAsync(
            new AddDependencyRequest("AgentService", "IEmailService"))).Unwrap();

        result.Status.ShouldBe(MutationExecutionStatus.NoOp);
        result.Value!.Outcome.ShouldBe(AddDependencyOutcome.AlreadyInjected);
        var diagnostic = result.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Severity.ShouldBe(MutationDiagnosticSeverity.Information);
        diagnostic.Message.ShouldBe(
            "Dependency 'IEmailService' is already injected into 'AgentService'.");
        writer.Solution.ShouldBeNull();
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
        result.Error!.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
        result.Error.Message.ShouldContain("ambiguous", Case.Insensitive);
    }

    [Fact]
    public async Task InvokeAsync_returns_failure_for_a_missing_target()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", "public class ExistingService { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDependencyOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);

        var result = await ((ICommandApplicationPort)operation).InvokeAsync(
            new AddDependencyRequest("MissingService", "IEmailService"));

        result.IsError.ShouldBeTrue();
        result.Value.ShouldBeNull();
        result.Error!.Kind.ShouldBe(RtErrorKind.NotFound);
        result.Error.Message.ShouldContain("was not found", Case.Insensitive);
        result.Error.Context.ShouldBeNull();
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeAsync_returns_failure_for_an_unsupported_constructor()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         interface IEmailService { }
                                         class AgentService
                                         {
                                             public AgentService() => DoWork();
                                             private static void DoWork() { }
                                         }
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
        result.Error!.Kind.ShouldBe(RtErrorKind.InvalidInput);
        result.Error.Message.ShouldContain("expression-bodied", Case.Insensitive);
        result.Error.Context.ShouldNotBeNull();
        result.Error.Context.ShouldHaveSingleItem().ShouldBe(
            new KeyValuePair<string, string>("targetType", "AgentService"));
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task InvokeAsync_returns_failure_for_an_empty_dependency()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", "public class AgentService { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDependencyOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);

        var result = await ((ICommandApplicationPort)operation).InvokeAsync(
            new AddDependencyRequest("AgentService", ""));

        result.IsError.ShouldBeTrue();
        result.Value.ShouldBeNull();
        result.Error!.Kind.ShouldBe(RtErrorKind.InvalidInput);
        result.Error.Message.ShouldContain("dependency", Case.Insensitive);
        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_honours_cancellation_before_loading_the_workspace()
    {
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDependencyOperation(
            new ThrowingWorkspaceSessionFactory(),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new AddDependencyRequest("AgentService", "IEmailService"),
            cancellation.Token));

        writer.Solution.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_honours_cancellation_after_loading_before_validation()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", "public class AgentService { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        using var cancellation = new CancellationTokenSource();
        var operation = new AddDependencyOperation(
            new CancellingWorkspaceSessionFactory(workspace, solution, cancellation),
            new AddDependencyService(NullLogger<AddDependencyService>.Instance, writer),
            writer);

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new AddDependencyRequest("", "IEmailService"),
            cancellation.Token));

        writer.Solution.ShouldBeNull();
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

    private sealed class ThrowingWorkspaceSessionFactory : IWorkspaceSessionFactory
    {
        public Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(string? solutionPath) =>
            throw new InvalidOperationException("Workspace loading must not start after cancellation.");

        public Task<(Microsoft.CodeAnalysis.Workspace Workspace, Microsoft.CodeAnalysis.Solution Solution)>
            LoadWorkspaceAsync(string? solutionPath) =>
            throw new InvalidOperationException("Workspace loading must not start after cancellation.");
    }

    private sealed class CancellingWorkspaceSessionFactory(
        Microsoft.CodeAnalysis.Workspace workspace,
        Microsoft.CodeAnalysis.Solution solution,
        CancellationTokenSource cancellation) : IWorkspaceSessionFactory
    {
        public Task<Microsoft.CodeAnalysis.Solution> GetSolutionAsync(string? solutionPath) =>
            Task.FromResult(solution);

        public Task<(Microsoft.CodeAnalysis.Workspace Workspace, Microsoft.CodeAnalysis.Solution Solution)>
            LoadWorkspaceAsync(string? solutionPath)
        {
            cancellation.Cancel();
            return Task.FromResult((workspace, solution));
        }
    }
}
