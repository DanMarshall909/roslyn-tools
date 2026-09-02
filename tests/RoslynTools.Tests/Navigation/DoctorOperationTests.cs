using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class DoctorOperationTests
{
    [Fact]
    public async Task missing_solution_is_a_typed_unsupported_outcome_without_evaluation()
    {
        using var directory = new TemporaryDirectory();
        var sessions = new CountingSessionFactory();
        var operation = new DoctorOperation(sessions, new FixedCapabilityCatalog());

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
        var operation = new DoctorOperation(sessions, new FixedCapabilityCatalog());

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
        var operation = new DoctorOperation(sessions, new FixedCapabilityCatalog());

        var envelope = (await operation.ExecuteAsync(new DoctorRequest(directory.Path))).Unwrap();

        envelope.Outcome.ShouldBe("TrustRequired");
        envelope.Data.Workspace.Trust.ShouldBe("TrustRequired");
        envelope.NextActions.ShouldHaveSingleItem().Capability.ShouldBe("doctor");
        sessions.InvocationCount.ShouldBe(0);
    }

    [Fact]
    public async Task repository_bound_trust_does_not_authorize_a_sibling_repository()
    {
        using var approvedDirectory = new TemporaryDirectory();
        using var selectedDirectory = new TemporaryDirectory();
        var solutionPath = System.IO.Path.Combine(selectedDirectory.Path, "App.slnx");
        File.WriteAllText(solutionPath, "");
        var sessions = new CountingSessionFactory();
        var operation = new DoctorOperation(sessions, new FixedCapabilityCatalog());

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
        var operation = new DoctorOperation(sessions, new FixedCapabilityCatalog());

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
            if (workspace is null || solution is null)
                throw new InvalidOperationException("Unexpected workspace evaluation.");
            return Task.FromResult((workspace, solution));
        }
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
}
