using System.CommandLine;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class NarrowDependencyOperationTests
{
    [Fact]
    public async Task apply_creates_the_interface_narrows_compatible_consumers_and_returns_the_full_report()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(
            new NarrowDependencyRequest("IWideFacade", ["Send"], "ISender", DryRun: false))).Unwrap();

        operation.Definition.Name.ShouldBe("narrow_dependency");
        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        var value = execution.Value!;
        value.InterfaceCreated.ShouldBeTrue();
        value.InterfaceReused.ShouldBeFalse();
        value.NarrowedConsumers.ShouldBe(["MyApp.SenderOnlyConsumer"]);
        value.SkippedConsumers.ShouldBe([
            new NarrowDependencySkippedConsumer(
                "MyApp.MixedConsumer",
                "uses members outside the selected subset: Receive")]);
        value.DiRegistrationFollowUp.ShouldNotBeNull();
        value.DiRegistrationFollowUp.ShouldContain("DI registration", Case.Insensitive);
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("ISender.cs", StringComparison.Ordinal));
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("Consumers.cs", StringComparison.Ordinal));
        execution.Diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("New interface 'ISender'", StringComparison.Ordinal));
        execution.Diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("DI registration", StringComparison.OrdinalIgnoreCase));
        writer.NewSolution.ShouldNotBeNull();

        var interfaceText = await TestSolutionDocuments.GetTextAsync(writer.NewSolution!, "ISender.cs");
        interfaceText.ShouldContain("Send", Case.Sensitive);
        interfaceText.ShouldNotContain("Receive", Case.Sensitive);

        var consumerText = await TestSolutionDocuments.GetTextAsync(writer.NewSolution!, "Consumers.cs");
        consumerText.ShouldContain("ISender", Case.Sensitive);
        consumerText.ShouldContain("IWideFacade", Case.Sensitive);
    }

    [Fact]
    public async Task dry_run_reports_the_new_document_and_leaves_the_writer_unreached()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(
            new NarrowDependencyRequest("IWideFacade", ["Send"], "ISender", DryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.ShouldContain(path => path.EndsWith("ISender.cs", StringComparison.Ordinal));
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task reuse_returns_a_structured_no_op_when_no_consumers_need_narrowing()
    {
        using var project = new AdhocProjectBuilder("ReuseApp");
        project.AddSource("IWideFacade.cs", "namespace MyApp; public interface IWideFacade { void Send(); }");
        project.AddSource("ISender.cs", "namespace MyApp; public interface ISender { void Send(); }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var execution = (await operation.ExecuteAsync(
            new NarrowDependencyRequest("IWideFacade", ["Send"], "ISender"))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.InterfaceCreated.ShouldBeFalse();
        execution.Value.InterfaceReused.ShouldBeTrue();
        execution.Value.NarrowedConsumers.ShouldBeEmpty();
        execution.Diagnostics.ShouldHaveSingleItem().Message.ShouldContain("Reusing existing interface 'ISender'", Case.Sensitive);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task invalid_member_returns_a_typed_error_without_writing()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(
            new NarrowDependencyRequest("IWideFacade", ["Missing"], "ISender"));

        var error = GetError(result);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("Missing", Case.Sensitive);
        error.Context!["interface"].ShouldBe("IWideFacade");
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task non_interface_type_returns_a_typed_error_without_writing()
    {
        using var project = new AdhocProjectBuilder("TypeApp");
        project.AddSource("Widget.cs", "namespace MyApp; public class Widget { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(
            new NarrowDependencyRequest("Widget", ["Run"], "IWidget"));

        var error = GetError(result);
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("not an interface", Case.Insensitive);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task cancellation_before_dispatch_cannot_write()
    {
        var writer = new CapturingSolutionChangeWriter();
        var operation = new NarrowDependencyOperation(
            new ThrowingWorkspaceSessionFactory(),
            new NarrowDependencyService(),
            writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new NarrowDependencyRequest("IWideFacade", ["Send"], "ISender"),
            cancellation.Token));

        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task daemon_rejects_the_in_process_mutation_port()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);
        var dispatcher = new DaemonCommandDispatcher([operation]);
        using var arguments = JsonDocument.Parse(
            """{"interface":"IWideFacade","members":["Send"],"new-name":"ISender","dry-run":true}""");

        var response = await dispatcher.ExecuteAsync(new DaemonRpcRequest(
            DaemonRpcProtocol.CurrentVersion,
            "narrow-dependency-preview",
            DaemonRpcProtocol.ExecuteCommandMethod,
            NarrowDependencyCommandDefinition.Definition.Name,
            arguments.RootElement.Clone()));

        response.Error.ShouldNotBeNull();
        response.Error.Code.ShouldBe(DaemonRpcErrorCodes.CommandNotFound);
        response.Error.Message.ShouldContain("in-process only", Case.Sensitive);
        response.Result.ShouldBeNull();
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public void cli_retains_the_interface_argument_and_repeatable_members_option()
    {
        using var services = new RtServicesFixture();
        var command = services.Resolve<NarrowDependencyCommand>().Build();

        command.Arguments.ShouldContain(argument => argument.Name == "interface");
        command.Options.ShouldContain(option => option.Name == "members");
        command.Options.ShouldContain(option => option.Name == "new-name");
        command.Options.ShouldContain(option => option.Name == "solution");
        command.Options.ShouldContain(option => option.Name == "dry-run");
    }

    [Fact]
    public async Task cli_expands_comma_separated_members_before_dispatching_the_port()
    {
        using var project = BuildWideFacadeProject();
        var (workspace, solution) = project.Build();
        using var services = new RtServicesFixture();

        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var exitCode = await services.Resolve<NarrowDependencyCommand>().Build().InvokeAsync(
            [
                "IWideFacade",
                "--members", "Send,Receive",
                "--new-name", "IMessageFacade",
                "--solution", "ignored",
                "--dry-run", "false"
            ]);
            exitCode.ShouldBe(0);
        }

        var interfaceSource = await project.ReadSourceAsync("IMessageFacade.cs");
        interfaceSource.ShouldContain("Send", Case.Sensitive);
        interfaceSource.ShouldContain("Receive", Case.Sensitive);
    }

    private static NarrowDependencyOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer) => new(
        new FixedWorkspaceSessionFactory(workspace, solution),
        new NarrowDependencyService(new ConsumerNarrowerService(), new SymbolResolutionService()),
        writer);

    private static RtError GetError(Result<MutationExecutionResult<NarrowDependencyResult>, RtError> result) =>
        result.Match(
            _ => throw new ShouldAssertException("Expected dependency narrowing to fail."),
            error => error);

    private static AdhocProjectBuilder BuildWideFacadeProject()
    {
        var project = new AdhocProjectBuilder("WideFacadeApp");
        project.AddSource("IWideFacade.cs", """
                                            namespace MyApp;
                                            public interface IWideFacade { void Send(string message); void Receive(); }
                                            """);
        project.AddSource("Consumers.cs", """
                                          namespace MyApp;
                                          public sealed class SenderOnlyConsumer
                                          {
                                              private readonly IWideFacade _facade;
                                              public SenderOnlyConsumer(IWideFacade facade) => _facade = facade;
                                              public void Use() => _facade.Send("hello");
                                          }
                                          public sealed class MixedConsumer
                                          {
                                              private readonly IWideFacade _facade;
                                              public MixedConsumer(IWideFacade facade) => _facade = facade;
                                              public void Use() { _facade.Send("hello"); _facade.Receive(); }
                                          }
                                          """);
        return project;
    }

    private sealed class FixedWorkspaceSessionFactory(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);
        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) => Task.FromResult((workspace, solution));
    }

    private sealed class ThrowingWorkspaceSessionFactory : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => throw new InvalidOperationException("Workspace loading must not start after cancellation.");
        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) => throw new InvalidOperationException("Workspace loading must not start after cancellation.");
    }
}
