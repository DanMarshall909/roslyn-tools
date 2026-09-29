using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Daemon;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Handlers;

public sealed class McpCommandAdapterMsBuildTests
{
    [Fact]
    public async Task installation_only_doctor_bypasses_msbuild_registration_and_invokes_the_operation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        var binding = new RecordingBinding(_ => throw new InvalidOperationException("must not register"));
        using var provider = CreateProvider(
            services =>
            {
                services.AddSingleton<IDoctorEnvironmentProbe>(new HealthyDoctorEnvironmentProbe());
                services.AddSingleton<ICommandApplicationPort>(provider =>
                    provider.GetRequiredService<DoctorOperation>());
            });
        var adapter = CreateAdapter(provider, new McpHostOptions(), binding);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            directory = Path.GetFullPath(Path.GetTempPath()),
            installationOnly = true
        }));

        var result = await adapter.InvokeAsync("doctor", arguments.RootElement);

        result.isError.ShouldBeFalse();
        using var content = JsonDocument.Parse(result.content);
        content.RootElement.GetProperty("outcome").GetString().ShouldBe("Succeeded");
        binding.Directories.ShouldBeEmpty();
    }

    [Fact]
    public async Task persistent_host_registers_the_first_trusted_solution_and_reuses_the_same_instance()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: true);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var identity = Identity("sdk-a");
        var registrations = 0;
        var resolvedDirectories = new List<string>();
        var binding = new McpMsBuildBinding(directory =>
        {
            resolvedDirectories.Add(directory);
            return new MsBuildInstanceSelection(identity, () => registrations++);
        });
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(
            provider,
            new McpHostOptions(trustRegistry: registry),
            binding);

        var first = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);
        var linked = await InvokeDoctorAsync(adapter, repository.LinkedSolutionPath);

        first.isError.ShouldBeFalse();
        linked.isError.ShouldBeFalse();
        registrations.ShouldBe(1);
        port.InvocationCount.ShouldBe(2);
        resolvedDirectories.ShouldBe([
            repository.PrimaryPath,
            repository.LinkedPath
        ]);
    }

    [Fact]
    public async Task persistent_host_rejects_a_later_incompatible_instance_before_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var firstRepository = TestRepository.Create(withLinkedWorktree: false);
        using var secondRepository = TestRepository.Create(withLinkedWorktree: false);
        var registry = firstRepository.CreateRegistry(
            firstRepository.PrimaryPath,
            secondRepository.PrimaryPath);
        var firstIdentity = Identity("sdk-a");
        var secondIdentity = Identity("sdk-b");
        var binding = new McpMsBuildBinding(directory => new MsBuildInstanceSelection(
            string.Equals(directory, firstRepository.PrimaryPath, StringComparison.Ordinal)
                ? firstIdentity
                : secondIdentity,
            () => { }));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(
            provider,
            new McpHostOptions(trustRegistry: registry),
            binding);

        (await InvokeDoctorAsync(adapter, firstRepository.PrimarySolutionPath)).isError.ShouldBeFalse();
        var rejected = await InvokeDoctorAsync(adapter, secondRepository.PrimarySolutionPath);

        rejected.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(rejected.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.ExternalToolFailure));
        var context = error.RootElement.GetProperty("context");
        context.GetProperty("mcpRestartRequired").GetString().ShouldBe("true");
        context.GetProperty("registeredMsBuildPath").GetString().ShouldBe(firstIdentity.MSBuildPath);
        context.GetProperty("requestedMsBuildPath").GetString().ShouldBe(secondIdentity.MSBuildPath);
        var message = error.RootElement.GetProperty("message").GetString();
        message.ShouldNotBeNull();
        message.ShouldContain("MCP host");
        port.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task persistent_host_compares_against_an_explicitly_captured_process_identity()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var registeredIdentity = Identity("sdk-a");
        var registrations = 0;
        var binding = new McpMsBuildBinding(
            _ => new MsBuildInstanceSelection(Identity("sdk-b"), () => registrations++),
            registeredIdentity);
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(result.content);
        error.RootElement.GetProperty("context")
            .GetProperty("registeredMsBuildPath")
            .GetString()
            .ShouldBe(registeredIdentity.MSBuildPath);
        registrations.ShouldBe(0);
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task untrusted_solution_is_rejected_before_sdk_resolution_or_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var trustedRepository = TestRepository.Create(withLinkedWorktree: false);
        using var untrustedRepository = TestRepository.Create(withLinkedWorktree: false);
        var registry = trustedRepository.CreateRegistry(trustedRepository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(
            provider,
            new McpHostOptions(trustRegistry: registry),
            binding);

        var result = await InvokeDoctorAsync(adapter, untrustedRepository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(result.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.TrustRequired));
        var message = error.RootElement.GetProperty("message").GetString();
        message.ShouldNotBeNull();
        message.ShouldContain("not trusted");
        var context = error.RootElement.GetProperty("context");
        context.GetProperty("solution").GetString().ShouldBe(untrustedRepository.PrimarySolutionPath);
        var grantThrough = context.GetProperty("grantThrough").GetString();
        grantThrough.ShouldNotBeNull();
        grantThrough.ShouldContain("rt trust add");
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task unavailable_sdk_selection_fails_closed_before_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new McpMsBuildBinding(_ => null);
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(
            provider,
            new McpHostOptions(trustRegistry: registry),
            binding);

        var result = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.ExternalToolFailure));
        result.content.ShouldContain("mcpRestartRequired");
        result.content.ShouldContain("No compatible .NET SDK");
        result.content.ShouldContain("MCP host");
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task sdk_selection_exception_fails_closed_before_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new McpMsBuildBinding(_ => throw new IOException("broken SDK selection"));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.ExternalToolFailure));
        result.content.ShouldContain("broken SDK selection");
        result.content.ShouldContain("MCP host");
        result.content.ShouldContain("mcpRestartRequired");
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task invalid_operation_during_sdk_selection_fails_closed_before_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new McpMsBuildBinding(_ => throw new InvalidOperationException("invalid SDK state"));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.ExternalToolFailure));
        result.content.ShouldContain("invalid SDK state");
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task sdk_registration_exception_fails_closed_before_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new McpMsBuildBinding(_ => new MsBuildInstanceSelection(
            Identity("sdk-a"),
            () => throw new InvalidOperationException("registration denied")));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.ExternalToolFailure));
        result.content.ShouldContain("registration denied");
        result.content.ShouldContain("MCP host");
        result.content.ShouldContain("mcpRestartRequired");
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task false_binding_result_fails_closed_before_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(false));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeDoctorAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeTrue();
        using var error = JsonDocument.Parse(result.content);
        error.RootElement.GetProperty("kind").GetString().ShouldBe(nameof(RtErrorKind.ExternalToolFailure));
        var message = error.RootElement.GetProperty("message").GetString();
        message.ShouldNotBeNull();
        message.ShouldContain("did not confirm");
        var context = error.RootElement.GetProperty("context");
        context.GetProperty("mcpRestartRequired").GetString().ShouldBe("true");
        context.GetProperty("requestedDirectory").GetString().ShouldBe(repository.PrimaryPath);
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public void default_binding_accepts_the_authoritative_instance_captured_by_bootstrap()
    {
        var repositoryRoot = FindRepoRoot();
        MsBuildBootstrap.Register(repositoryRoot);
        var binding = new McpMsBuildBinding();

        binding.EnsureRegistered(repositoryRoot).Unwrap().ShouldBeTrue();
    }

    [Fact]
    public async Task malformed_solution_path_fails_closed_before_sdk_resolution_or_port_invocation()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(
            provider,
            new McpHostOptions(trustRegistry: registry),
            binding);

        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            directory = repository.PrimaryPath,
            solution = $"{repository.PrimaryPath}{Path.DirectorySeparatorChar}bad\0.slnx",
            installationOnly = false
        }));
        var result = await adapter.InvokeAsync("doctor", arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.InvalidInput));
        result.content.ShouldContain("invalid");
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public void doctor_directory_without_a_solution_remains_the_effective_directory_when_it_does_not_exist()
    {
        var requestedDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rt-mcp-missing-directory-{Guid.NewGuid():N}");
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            directory = requestedDirectory,
            installationOnly = false
        }));
        var request = new DoctorRequest(requestedDirectory);

        var resolved = McpCommandAdapter.ResolveWorkspaceTarget(arguments.RootElement, request).Unwrap();

        resolved.TrustPath.ShouldBe(requestedDirectory);
        resolved.Directory.ShouldBe(requestedDirectory);
    }

    [Fact]
    public async Task persistent_workspace_request_without_a_solution_never_binds_the_startup_directory()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new MissingSolutionProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(
            provider,
            new McpHostOptions(trustRegistry: registry),
            binding);
        using var arguments = JsonDocument.Parse("{}");

        var result = await adapter.InvokeAsync("doctor", arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.InvalidInput));
        result.content.ShouldContain("absolute solution");
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task persistent_generic_workspace_request_resolves_its_absolute_solution()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new MissingSolutionProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeWorkspaceProbeAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeFalse();
        binding.Directories.ShouldBe([repository.PrimaryPath]);
        port.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task persistent_generic_workspace_request_rejects_a_relative_solution_before_binding()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new MissingSolutionProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeWorkspaceProbeAsync(adapter, "App.slnx");

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.InvalidInput));
        result.content.ShouldContain("absolute solution");
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(0);
    }

    [Theory]
    [InlineData("{\"solution\":null}")]
    [InlineData("{\"solution\":42}")]
    public async Task persistent_generic_workspace_request_rejects_a_non_string_solution_before_binding(
        string json)
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new MissingSolutionProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);
        using var arguments = JsonDocument.Parse(json);

        var result = await adapter.InvokeAsync("doctor", arguments.RootElement);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.InvalidInput));
        result.content.ShouldContain("absolute solution");
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task persistent_solution_without_a_parent_directory_fails_before_binding()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new MissingSolutionProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);

        var result = await InvokeWorkspaceProbeAsync(adapter, Path.GetPathRoot(repository.PrimaryPath)!);

        result.isError.ShouldBeTrue();
        result.content.ShouldContain(nameof(RtErrorKind.InvalidInput));
        result.content.ShouldContain("parent directory");
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task relative_doctor_solution_resolves_under_its_explicit_directory()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var registry = repository.CreateRegistry(repository.PrimaryPath);
        var binding = new RecordingBinding(_ => Result<bool, RtError>.Ok(true));
        var port = new DoctorProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(trustRegistry: registry), binding);
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            directory = repository.PrimaryPath,
            solution = "App.slnx",
            installationOnly = false
        }));

        var result = await adapter.InvokeAsync("doctor", arguments.RootElement);

        result.isError.ShouldBeFalse();
        binding.Directories.ShouldBe([repository.PrimaryPath]);
        port.InvocationCount.ShouldBe(1);
    }

    [Fact]
    public async Task host_without_persistent_registry_retains_its_existing_in_process_path()
    {
        using var experiment = DaemonRuntime.UseExperimentOverride(false);
        using var repository = TestRepository.Create(withLinkedWorktree: false);
        var binding = new RecordingBinding(_ => throw new InvalidOperationException("must not use persistent binding"));
        var port = new MissingSolutionProbePort();
        using var provider = CreateProvider(services => services.AddSingleton<ICommandApplicationPort>(port));
        var adapter = CreateAdapter(provider, new McpHostOptions(), binding);

        var result = await InvokeWorkspaceProbeAsync(adapter, repository.PrimarySolutionPath);

        result.isError.ShouldBeFalse();
        binding.Directories.ShouldBeEmpty();
        port.InvocationCount.ShouldBe(1);
    }

    private static ServiceProvider CreateProvider(Action<IServiceCollection> addPorts)
    {
        var services = new ServiceCollection();
        services.AddMcpHostServices(Stream.Null, Stream.Null);
        services.RemoveAll<ICommandApplicationPort>();
        addPorts(services);
        return services.BuildServiceProvider();
    }

    private static McpCommandAdapter CreateAdapter(
        IServiceProvider provider,
        McpHostOptions options,
        IMcpMsBuildBinding binding) => new(
        provider.GetRequiredService<ApplicationCommandCatalog>(),
        new DaemonRpcRecoveryCoordinator(new UnavailableRpcClient(), new UnavailableLauncher()),
        options,
        binding);

    private static async Task<ToolResult> InvokeDoctorAsync(
        McpCommandAdapter adapter,
        string solution)
    {
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            directory = Path.GetDirectoryName(solution),
            solution,
            installationOnly = false
        }));
        return await adapter.InvokeAsync("doctor", arguments.RootElement);
    }

    private static async Task<ToolResult> InvokeWorkspaceProbeAsync(
        McpCommandAdapter adapter,
        string solution)
    {
        using var arguments = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            description = "This string is not an authority-bearing solution path.",
            solution
        }));
        return await adapter.InvokeAsync("doctor", arguments.RootElement);
    }

    private static MsBuildInstanceIdentity Identity(string name) =>
        MsBuildInstanceIdentity.Create(
            Path.Combine(Path.GetTempPath(), "rt-mcp-sdk-tests", name),
            new Version(10, 0, 201));

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RoslynTools.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }

    private sealed class RecordingBinding(
        Func<string, Result<bool, RtError>> execute) : IMcpMsBuildBinding
    {
        internal List<string> Directories { get; } = [];

        public Result<bool, RtError> EnsureRegistered(string directory)
        {
            Directories.Add(directory);
            return execute(directory);
        }
    }

    private sealed class DoctorProbePort
        : CommandApplicationPort<DoctorRequest, DoctorProbeResponse>
    {
        internal int InvocationCount { get; private set; }

        public override ApplicationCommandDefinition Definition { get; } = new(
            DoctorCommandDefinition.Info,
            typeof(DoctorRequest),
            typeof(DoctorProbeResponse),
            mcpVisible: true,
            CommandSafety.ReadOnly,
            CommandExecutionPlacement.InProcess,
            []);

        public override Task<Result<DoctorProbeResponse, RtError>> ExecuteAsync(
            DoctorRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(Result<DoctorProbeResponse, RtError>.Ok(
                new DoctorProbeResponse(request.Solution!)));
        }
    }

    private sealed record DoctorProbeResponse(string Solution);

    private sealed class MissingSolutionProbePort
        : CommandApplicationPort<MissingSolutionProbeRequest, DoctorProbeResponse>
    {
        internal int InvocationCount { get; private set; }

        public override ApplicationCommandDefinition Definition { get; } = new(
            DoctorCommandDefinition.Info,
            typeof(MissingSolutionProbeRequest),
            typeof(DoctorProbeResponse),
            mcpVisible: true,
            CommandSafety.ReadOnly,
            CommandExecutionPlacement.InProcess,
            []);

        public override Task<Result<DoctorProbeResponse, RtError>> ExecuteAsync(
            MissingSolutionProbeRequest request,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return Task.FromResult(Result<DoctorProbeResponse, RtError>.Ok(
                new DoctorProbeResponse(request.Solution?.ToString() ?? string.Empty)));
        }
    }

    private sealed record MissingSolutionProbeRequest(object? Solution = null);

    private sealed class HealthyDoctorEnvironmentProbe : IDoctorEnvironmentProbe
    {
        public Task<DoctorEnvironmentProbeResult> ProbeAsync(
            string directory,
            CancellationToken cancellationToken) =>
            Task.FromResult(DoctorEnvironmentProbeResult.Healthy);
    }

    private sealed class UnavailableRpcClient : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) => Task.FromResult(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
    }

    private sealed class UnavailableLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class TestRepository : IDisposable
    {
        private readonly string _root;

        private TestRepository(string root)
        {
            _root = root;
            PrimaryPath = Path.Combine(root, "primary");
            LinkedPath = Path.Combine(root, "linked");
            RegistryPath = Path.Combine(root, "profile", "repository-trust.json");
        }

        internal string PrimaryPath { get; }
        internal string LinkedPath { get; }
        internal string RegistryPath { get; }
        internal string PrimarySolutionPath => Path.Combine(PrimaryPath, "App.slnx");
        internal string LinkedSolutionPath => Path.Combine(LinkedPath, "App.slnx");

        internal static TestRepository Create(bool withLinkedWorktree)
        {
            var repository = new TestRepository(Path.Combine(
                Path.GetTempPath(),
                $"rt-mcp-msbuild-{Guid.NewGuid():N}"));
            Directory.CreateDirectory(repository.PrimaryPath);
            File.WriteAllText(repository.PrimarySolutionPath, "<Solution />");
            RunGit(repository.PrimaryPath, "init", "--initial-branch=main");
            RunGit(repository.PrimaryPath, "config", "user.email", "tests@roslyn-tools.local");
            RunGit(repository.PrimaryPath, "config", "user.name", "Roslyn Tools Tests");
            RunGit(repository.PrimaryPath, "add", "App.slnx");
            RunGit(repository.PrimaryPath, "commit", "-m", "Initial");
            if (withLinkedWorktree)
            {
                RunGit(
                    repository.PrimaryPath,
                    "worktree",
                    "add",
                    "-b",
                    "feat/linked",
                    repository.LinkedPath);
            }

            return repository;
        }

        internal RepositoryTrustRegistry CreateRegistry(params string[] trustedRepositories)
        {
            var registry = new RepositoryTrustRegistry(RegistryPath);
            foreach (var repository in trustedRepositories)
                registry.Add(repository).IsOk.ShouldBeTrue();
            return registry;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private static void RunGit(string workingDirectory, params string[] arguments)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("git")
                {
                    WorkingDirectory = workingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                }
            };
            foreach (var argument in arguments)
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"git {string.Join(' ', arguments)} failed ({process.ExitCode}): {stderr}{stdout}");
            }
        }
    }
}
