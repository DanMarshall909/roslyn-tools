using System.Diagnostics;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class DoctorOperationTests
{
    [Fact]
    public async Task installation_only_requires_every_installation_probe_without_loading_a_workspace()
    {
        var sessions = new CountingSessionFactory();
        var bootstrap = new CountingWorkspaceBootstrap();
        var operation = new DoctorOperation(
            sessions,
            new FixedCapabilityCatalog(),
            new FixedEnvironmentProbe(new DoctorEnvironmentProbeResult(
                CliLauncherHealthy: true,
                McpLauncherHealthy: true,
                BuildHostHealthy: true,
                CompatibleSdkAvailable: true,
                McpHandshakeHealthy: false,
                ProtocolVersion: "2024-11-05",
                ServerName: "roslyn-tools-mcp",
                DiscoveredTools: 0,
                RequestedSdk: null,
                SelectedSdk: "10.0.201",
                Diagnostics: ["tools/list returned no tools."])),
            bootstrap);

        var envelope = (await operation.ExecuteAsync(new DoctorRequest(
            Directory.GetCurrentDirectory(),
            InstallationOnly: true))).Unwrap();

        envelope.Outcome.ShouldBe("Failed");
        envelope.Data.Installation.McpHandshakeHealthy.ShouldBeFalse();
        envelope.Data.Workspace.State.ShouldBe("NotRequested");
        sessions.InvocationCount.ShouldBe(0);
        bootstrap.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task installation_only_fails_when_dotnet_cannot_select_the_requested_sdk()
    {
        var sessions = new CountingSessionFactory();
        var operation = new DoctorOperation(
            sessions,
            new FixedCapabilityCatalog(),
            new FixedEnvironmentProbe(DoctorEnvironmentProbeResult.Healthy with
            {
                CompatibleSdkAvailable = false,
                RequestedSdk = "10.0.301",
                SelectedSdk = "10.0.201",
                SdkRequestState = "Valid",
                GlobalJsonPath = "/repo/global.json",
                SdkDiagnostic = "dotnet could not select SDK 10.0.301 from /repo/global.json.",
                Diagnostics = ["dotnet could not select SDK 10.0.301 from global.json."]
            }),
            new CountingWorkspaceBootstrap());

        var envelope = (await operation.ExecuteAsync(new DoctorRequest(
            Directory.GetCurrentDirectory(),
            InstallationOnly: true))).Unwrap();

        envelope.Outcome.ShouldBe("Failed");
        envelope.Data.Installation.RequestedSdk.ShouldBe("10.0.301");
        envelope.Data.Installation.SelectedSdk.ShouldBe("10.0.201");
        envelope.Data.Installation.SdkRequestState.ShouldBe("Valid");
        envelope.Data.Installation.GlobalJsonPath.ShouldBe("/repo/global.json");
        envelope.Data.Installation.SdkDiagnostic.ShouldNotBeNull();
        envelope.Data.Installation.SdkDiagnostic.ShouldContain("10.0.301");
        sessions.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task trusted_workspace_bootstraps_msbuild_immediately_before_loading()
    {
        using var directory = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(directory.Path, "App.slnx");
        File.WriteAllText(solutionPath, "");
        using var project = new AdhocProjectBuilder("DoctorApp");
        project.AddSource("Healthy.cs", "public class Healthy { }");
        var (workspace, solution) = project.Build();
        var sessions = new CountingSessionFactory(workspace, solution);
        var bootstrap = new CountingWorkspaceBootstrap();
        var operation = CreateOperation(sessions, bootstrap);

        using var trust = RepositoryTrustContext.UseExplicitTrust();
        var envelope = (await operation.ExecuteAsync(new DoctorRequest(
            directory.Path,
            solutionPath))).Unwrap();

        envelope.Outcome.ShouldBe("Succeeded");
        bootstrap.InvocationCount.ShouldBe(1);
        sessions.InvocationCount.ShouldBe(1);
        bootstrap.CallOrder.ShouldBeLessThan(sessions.CallOrder);
    }

    [Fact]
    public async Task sdk_bootstrap_failure_is_reported_without_loading_the_solution()
    {
        using var directory = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(directory.Path, "App.slnx");
        File.WriteAllText(solutionPath, "");
        var sessions = new CountingSessionFactory();
        var bootstrap = new CountingWorkspaceBootstrap(new RtError(
            RtErrorKind.ExternalToolFailure,
            "SDK 10.0.301 requested by global.json is not installed."));
        var operation = CreateOperation(sessions, bootstrap);

        using var trust = RepositoryTrustContext.UseExplicitTrust();
        var envelope = (await operation.ExecuteAsync(new DoctorRequest(
            directory.Path,
            solutionPath))).Unwrap();

        envelope.Outcome.ShouldBe("Failed");
        envelope.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Message.Contains("10.0.301", StringComparison.Ordinal));
        bootstrap.InvocationCount.ShouldBe(1);
        sessions.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task normal_doctor_reports_persistent_trust_even_when_no_solution_is_selected()
    {
        using var directory = new TemporaryDirectory();
        RunGit(directory.Path, "init");
        RunGit(directory.Path, "config", "user.email", "doctor@example.invalid");
        RunGit(directory.Path, "config", "user.name", "Doctor Test");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "README.md"), "fixture");
        RunGit(directory.Path, "add", "README.md");
        RunGit(directory.Path, "commit", "-m", "fixture");
        var registry = new RepositoryTrustRegistry(System.IO.Path.Combine(directory.Path, "trust.json"));
        registry.Add(directory.Path).IsOk.ShouldBeTrue();
        var sessions = new CountingSessionFactory();
        var operation = CreateOperation(sessions);

        using var trust = RepositoryTrustContext.UsePersistentTrust(registry);
        var envelope = (await operation.ExecuteAsync(new DoctorRequest(directory.Path))).Unwrap();

        envelope.Outcome.ShouldBe("Unsupported");
        envelope.Data.Workspace.Trust.ShouldBe("PersistentTrusted");
        envelope.Limitations.ShouldContain(message => message.Contains("Persistent trust", StringComparison.Ordinal));
    }

    [Fact]
    public async Task missing_solution_is_a_typed_unsupported_outcome_without_evaluation()
    {
        using var directory = new TemporaryDirectory();
        var sessions = new CountingSessionFactory();
        var operation = CreateOperation(sessions);

        var envelope = (await operation.ExecuteAsync(new DoctorRequest(directory.Path))).Unwrap();

        envelope.Outcome.ShouldBe("Unsupported");
        envelope.Data.Workspace.State.ShouldBe("NotLoaded");
        envelope.Data.Workspace.Candidates.ShouldBeEmpty();
        sessions.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task multiple_solutions_are_ranked_deterministically_without_evaluation()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "Zeta.slnx"), "");
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "Alpha.sln"), "");
        var sessions = new CountingSessionFactory();
        var operation = CreateOperation(sessions);

        var envelope = (await operation.ExecuteAsync(new DoctorRequest(directory.Path))).Unwrap();

        envelope.Outcome.ShouldBe("Ambiguous");
        envelope.Data.Workspace.Candidates.Select(System.IO.Path.GetFileName)
            .ShouldBe(["Alpha.sln", "Zeta.slnx"]);
        sessions.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task selected_repository_requires_explicit_one_shot_trust_before_evaluation()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(System.IO.Path.Combine(directory.Path, "App.slnx"), "");
        var sessions = new CountingSessionFactory();
        var bootstrap = new CountingWorkspaceBootstrap();
        var operation = CreateOperation(sessions, bootstrap);

        var envelope = (await operation.ExecuteAsync(new DoctorRequest(directory.Path))).Unwrap();

        envelope.Outcome.ShouldBe("TrustRequired");
        envelope.Data.Workspace.Trust.ShouldBe("TrustRequired");
        envelope.NextActions.ShouldHaveSingleItem().Capability.ShouldBe("doctor");
        sessions.InvocationCount.ShouldBe(0);
        bootstrap.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task repository_bound_trust_does_not_authorize_a_sibling_repository()
    {
        using var approvedDirectory = new TemporaryDirectory();
        using var selectedDirectory = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(selectedDirectory.Path, "App.slnx");
        File.WriteAllText(solutionPath, "");
        var sessions = new CountingSessionFactory();
        var operation = CreateOperation(sessions);

        using var trust = RepositoryTrustContext.UseExplicitTrust(approvedDirectory.Path);
        var envelope = (await operation.ExecuteAsync(new DoctorRequest(
            selectedDirectory.Path,
            solutionPath))).Unwrap();

        envelope.Outcome.ShouldBe("TrustRequired");
        envelope.Data.Workspace.Trust.ShouldBe("TrustRequired");
        sessions.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task trusted_healthy_workspace_returns_capabilities_and_limits()
    {
        using var directory = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(directory.Path, "App.slnx");
        File.WriteAllText(solutionPath, "");
        using var project = new AdhocProjectBuilder("DoctorApp");
        project.AddSource("Healthy.cs", "public class Healthy { }");
        var (workspace, solution) = project.Build();
        var sessions = new CountingSessionFactory(workspace, solution);
        var operation = CreateOperation(sessions);

        using var trust = RepositoryTrustContext.UseExplicitTrust();
        var envelope = (await operation.ExecuteAsync(new DoctorRequest(
            directory.Path,
            solutionPath))).Unwrap();

        envelope.Outcome.ShouldBe("Succeeded");
        envelope.SchemaVersion.ShouldBe("rt.result.v1");
        envelope.Data.Installation.DaemonRequired.ShouldBeFalse();
        envelope.Data.Workspace.State.ShouldBe("Complete");
        envelope.Data.Workspace.Trust.ShouldBe("OneShotTrusted");
        envelope.Data.Capabilities.StableCandidates.ShouldBe(2);
        envelope.Data.Limits.MaximumItems.ShouldBeGreaterThan(0);
        sessions.InvocationCount.ShouldBe(1);
    }

    private sealed class FixedCapabilityCatalog : IProductCapabilityCatalog
    {
        public IReadOnlyList<ApplicationCommandDefinition> GetDefinitions() =>
            [OutlineCommandDefinition.Definition, DoctorCommandDefinition.Definition];
    }

    private static DoctorOperation CreateOperation(
        CountingSessionFactory sessions,
        CountingWorkspaceBootstrap? bootstrap = null) => new(
            sessions,
            new FixedCapabilityCatalog(),
            new FixedEnvironmentProbe(DoctorEnvironmentProbeResult.Healthy),
            bootstrap ?? new CountingWorkspaceBootstrap());

    private sealed class FixedEnvironmentProbe(DoctorEnvironmentProbeResult result)
        : IDoctorEnvironmentProbe
    {
        public Task<DoctorEnvironmentProbeResult> ProbeAsync(string directory, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class CountingWorkspaceBootstrap(RtError? error = null) : IDoctorWorkspaceBootstrap
    {
        public int InvocationCount { get; private set; }

        public long CallOrder { get; private set; }

        public Result<bool, RtError> Register(string solutionPath)
        {
            InvocationCount++;
            CallOrder = Stopwatch.GetTimestamp();
            return error is null
                ? Result<bool, RtError>.Ok(true)
                : Result<bool, RtError>.Err(error);
        }
    }

    private sealed class CountingSessionFactory(
        Workspace? workspace = null,
        Solution? solution = null) : IWorkspaceSessionFactory
    {
        public int InvocationCount { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath) =>
            Task.FromResult(solution ?? throw new InvalidOperationException("Unexpected workspace evaluation."));

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            InvocationCount++;
            CallOrder = Stopwatch.GetTimestamp();
            if (workspace is null || solution is null)
                throw new InvalidOperationException("Unexpected workspace evaluation.");
            return Task.FromResult((workspace, solution));
        }

        public long CallOrder { get; private set; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"rt-doctor-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => System.IO.Directory.Delete(Path, recursive: true);
    }

    private static void RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("git did not start");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
    }
}
