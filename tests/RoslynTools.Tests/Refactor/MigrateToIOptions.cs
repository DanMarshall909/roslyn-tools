using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Refactor;

public sealed class MigrateToIOptions
{
    [Fact]
    public async Task apply_generates_poco_registration_and_structured_discovery_metadata()
    {
        using var fixture = BuildFixture();
        var operation = CreateOperation(fixture.Workspace, fixture.Solution, new SolutionChangeWriter());

        var execution = (await operation.ExecuteAsync(Request())).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.GeneratedType.ShouldBe("MyFeatureOptions");
        execution.Value.GeneratedPath.ShouldBe(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        execution.Value.RegistrationSite.FilePath.ShouldBe(Path.Combine(fixture.ProjectDir, "Startup.cs"));
        execution.Value.RegistrationSite.ServicesExpression.ShouldBe("services");
        execution.Value.DiscoveredConfigSources.ShouldBe([Path.Combine(fixture.ProjectDir, "appsettings.json")]);
        execution.Value.ReadSiteWorklist.ShouldContain(Path.Combine(fixture.ProjectDir, "Consumers", "FeatureReader.cs"));
        execution.ChangedFiles.ShouldBe([
            Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"),
            Path.Combine(fixture.ProjectDir, "Startup.cs")], ignoreOrder: true);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        options.ShouldContain("namespace HostApp;", Case.Sensitive);
        options.ShouldContain("internal sealed class MyFeatureOptions", Case.Sensitive);
        options.ShouldContain("public bool Enabled { get; init; }", Case.Sensitive);
        options.ShouldContain("public string Endpoint { get; init; } = string.Empty;", Case.Sensitive);
        options.ShouldContain("public int RetryCount { get; init; }", Case.Sensitive);

        var startup = await fixture.ReadSourceAsync("Startup.cs");
        startup.ShouldContain(
            "services.Configure<MyFeatureOptions>(configuration.GetSection(\"MyFeature\"));",
            Case.Sensitive);
    }

    [Fact]
    public async Task builder_services_registration_preserves_full_service_and_configuration_expressions()
    {
        using var fixture = BuildFixture(builderStyleRegistration: true);
        var operation = CreateOperation(fixture.Workspace, fixture.Solution, new SolutionChangeWriter());

        var execution = (await operation.ExecuteAsync(Request())).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        execution.Value!.RegistrationSite.ServicesExpression.ShouldBe("builder.Services");
        execution.Value.RegistrationSite.ConfigurationExpression.ShouldBe("builder.Configuration");
        var startup = await fixture.ReadSourceAsync("Startup.cs");
        startup.ShouldContain("using HostApp;", Case.Sensitive);
        startup.ShouldContain(
            "builder.Services.Configure<MyFeatureOptions>(builder.Configuration.GetSection(\"MyFeature\"));",
            Case.Sensitive);
    }

    [Fact]
    public async Task dry_run_previews_the_solution_change_without_writing()
    {
        using var fixture = BuildFixture();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(fixture.Workspace, fixture.Solution, writer);

        var execution = (await operation.ExecuteAsync(Request(dryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.ShouldContain(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task already_applied_migration_returns_a_no_op_with_metadata()
    {
        using var fixture = BuildFixture();
        var service = new MigrateToIOptionsService();
        var prepared = (await service.PrepareAsync(fixture.Solution, "MyFeature", "MyFeatureOptions")).Unwrap();
        await new SolutionChangeWriter().ApplyChangesAsync(fixture.Solution, prepared.Solution, dryRun: false);
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(fixture.Workspace, fixture.Workspace.CurrentSolution, writer);

        var execution = (await operation.ExecuteAsync(Request())).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.AlreadyApplied.ShouldBeTrue();
        execution.Diagnostics.ShouldContain(diagnostic => diagnostic.Message.Contains("already applied", StringComparison.Ordinal));
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task multiple_registration_sites_selects_first_and_returns_ordered_diagnostics()
    {
        using var fixture = BuildFixture(includeSecondRegistration: true);
        var operation = CreateOperation(fixture.Workspace, fixture.Solution, new CapturingSolutionChangeWriter());

        var execution = (await operation.ExecuteAsync(Request(dryRun: true))).Unwrap();

        execution.Value!.RegistrationSite.FilePath.ShouldBe(Path.Combine(fixture.ProjectDir, "OtherRegistration.cs"));
        execution.Diagnostics[0].Message.ShouldBe("Multiple DI registration sites found; using first candidate.");
        var candidateDiagnostics = execution.Diagnostics.Skip(1).Select(diagnostic => diagnostic.Message).ToArray();
        candidateDiagnostics.ShouldBe(candidateDiagnostics.OrderBy(message => message, StringComparer.Ordinal));
        candidateDiagnostics.ShouldContain(message => message.Contains("OtherRegistration.cs:", StringComparison.Ordinal));
        candidateDiagnostics.ShouldContain(message => message.Contains("Startup.cs:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task missing_registration_and_name_collision_return_actionable_typed_errors()
    {
        using var noRegistration = BuildFixture(includeRegistration: false);
        var noRegistrationOperation = CreateOperation(
            noRegistration.Workspace,
            noRegistration.Solution,
            new CapturingSolutionChangeWriter());

        var missingResult = await noRegistrationOperation.ExecuteAsync(Request());

        missingResult.IsErr.ShouldBeTrue();
        missingResult.Match(
            _ => throw new InvalidOperationException("Expected a missing registration failure."),
            error =>
            {
                error.Kind.ShouldBe(RtErrorKind.NotFound);
                error.Message.ShouldContain("No DI registration site", Case.Sensitive);
            });
        File.Exists(Path.Combine(noRegistration.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();

        using var collision = BuildFixture();
        var collisionPath = Path.Combine(collision.ProjectDir, "MyFeatureOptions.cs");
        await File.WriteAllTextAsync(collisionPath, "namespace HostApp; internal sealed class MyFeatureOptions { }");
        var collisionOperation = CreateOperation(collision.Workspace, collision.Solution, new CapturingSolutionChangeWriter());

        var collisionResult = await collisionOperation.ExecuteAsync(Request());

        collisionResult.IsErr.ShouldBeTrue();
        collisionResult.Match(
            _ => throw new InvalidOperationException("Expected a type collision failure."),
            error =>
            {
                error.Kind.ShouldBe(RtErrorKind.InvalidInput);
                error.Context!["filePath"].ShouldBe(collisionPath);
            });
    }

    [Theory]
    [InlineData("", "MyFeatureOptions", "--section is required.")]
    [InlineData(":MyFeature", "MyFeatureOptions", "--section is malformed")]
    [InlineData("MyFeature", "", "--name is required.")]
    [InlineData("MyFeature", "myFeatureOptions", "--name must be PascalCase.")]
    public async Task validation_fails_before_workspace_loading(string section, string name, string expectedMessage)
    {
        var operation = new MigrateToIOptionsOperation(
            new ThrowingSessionFactory(),
            new MigrateToIOptionsService(),
            new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new MigrateToIOptionsRequest(section, name));

        result.IsErr.ShouldBeTrue();
        result.Match(
            _ => throw new InvalidOperationException("Expected a validation failure."),
            error =>
            {
                error.Kind.ShouldBe(RtErrorKind.InvalidInput);
                error.Message.ShouldContain(expectedMessage, Case.Sensitive);
            });
    }

    [Fact]
    public async Task cancellation_before_execution_never_crosses_the_solution_writer_boundary()
    {
        using var fixture = BuildFixture();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(fixture.Workspace, fixture.Solution, writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(Request(), cancellation.Token));

        writer.NewSolution.ShouldBeNull();
        File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();
    }

    [Fact]
    public async Task cli_returns_a_nonzero_exit_code_for_a_typed_validation_failure()
    {
        var operation = new MigrateToIOptionsOperation(
            new ThrowingSessionFactory(),
            new MigrateToIOptionsService(),
            new CapturingSolutionChangeWriter());
        var command = new MigrateToIOptionsCommand(
            new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()),
            operation);

        var exitCode = await command.Build().InvokeAsync(
            ["--section", ":MyFeature", "--name", "MyFeatureOptions"]);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public void command_metadata_is_backed_by_the_typed_definition()
    {
        MigrateToIOptionsCommand.Info.ShouldBeSameAs(MigrateToIOptionsCommandDefinition.Info);
        MigrateToIOptionsCommandDefinition.Definition.Name.ShouldBe("migrate_to_ioptions");
        MigrateToIOptionsCommandDefinition.Definition.Safety.ShouldBe(CommandSafety.Mutation);
        MigrateToIOptionsCommandDefinition.Definition.RequestType.ShouldBe(typeof(MigrateToIOptionsRequest));
        App.Commands.ShouldContain(command => command.Info.Name == "migrate-to-ioptions");
    }

    private static MigrateToIOptionsRequest Request(bool dryRun = false) =>
        new("MyFeature", "MyFeatureOptions", "ignored.slnx", dryRun);

    private static MigrateToIOptionsOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        ISolutionChangeWriter writer) => new(
        new FixedWorkspaceSessionFactory(workspace, solution),
        new MigrateToIOptionsService(),
        writer);

    private static TestFixture BuildFixture(
        bool includeRegistration = true,
        bool includeSecondRegistration = false,
        bool builderStyleRegistration = false)
    {
        var fixture = new TestFixture("HostApp");
        fixture.AddSource("Startup.cs", includeRegistration
            ? builderStyleRegistration
                ? """
                  var builder = WebApplication.CreateBuilder(args);
                  builder.Services.AddLogging();
                  var endpoint = builder.Configuration.GetValue<string>("MyFeature:Endpoint");
                  """
                : """
                  namespace HostApp;

                  public sealed class Startup
                  {
                      public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
                      {
                          services.AddLogging();
                      }
                  }
                  """
            : """
              namespace HostApp;

              public sealed class Startup
              {
                  public void ConfigureServices()
                  {
                  }
              }
              """);
        fixture.AddSource("Consumers/FeatureReader.cs", """
            namespace HostApp;

            public sealed class FeatureReader
            {
                public string Endpoint(IConfiguration configuration)
                {
                    var retryCount = configuration.GetValue<int>("MyFeature:RetryCount");
                    return configuration.GetValue<string>("MyFeature:Endpoint");
                }
            }
            """);
        if (includeSecondRegistration)
            fixture.AddSource("OtherRegistration.cs", """
                namespace HostApp;

                public sealed class OtherRegistration
                {
                    public void Add(IServiceCollection services, IConfiguration configuration)
                    {
                        services.AddOptions();
                    }
                }
                """);

        fixture.Build();
        File.WriteAllText(Path.Combine(fixture.ProjectDir, "appsettings.json"), """
            {
              "MyFeature": {
                "Endpoint": "https://example.test",
                "Enabled": true
              }
            }
            """);
        return fixture;
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }

    private sealed class ThrowingSessionFactory : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) =>
            throw new InvalidOperationException("The workspace should not be loaded for invalid input.");

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            throw new InvalidOperationException("The workspace should not be loaded for invalid input.");
    }

    private sealed class UnavailableRpcClient : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            System.Text.Json.JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) => Task.FromResult(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
    }

    private sealed class UnavailableLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly AdhocProjectBuilder _builder;

        public TestFixture(string name) => _builder = new AdhocProjectBuilder(name);

        public string ProjectDir => _builder.ProjectDir;
        public Workspace Workspace { get; private set; } = null!;
        public Solution Solution { get; private set; } = null!;

        public void AddSource(string relativePath, string content) => _builder.AddSource(relativePath, content);

        public void Build() => (Workspace, Solution) = _builder.Build();

        public Task<string> ReadSourceAsync(string relativePath) => _builder.ReadSourceAsync(relativePath);

        public void Dispose() => _builder.Dispose();
    }
}
