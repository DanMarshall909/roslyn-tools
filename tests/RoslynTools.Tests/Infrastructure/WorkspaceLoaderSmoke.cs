using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     The rest of the suite runs on AdhocWorkspace (no MSBuild). This file keeps the
///     real MSBuildWorkspace path covered end-to-end so a regression in
///     <see cref="WorkspaceLoader" /> itself won't slip through silently.
///     Lives outside any xunit Collection so it runs in parallel with the rest, but
///     the BuildHost-netcore subprocess startup (~1–2s) is paid here rather than
///     by every test.
/// </summary>
public class WorkspaceLoaderSmoke
{
    static WorkspaceLoaderSmoke()
    {
        MsBuildFixture.Ensure();
    }

    [Fact]
    public async Task opens_the_solution_and_finds_a_known_type()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution(
            "",
            "Smoke",
            "SmokeProject",
            """
            namespace SmokeProject;

            public class SmokeType
            {
            }
            """);

        var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
        using (workspace)
        {
            solution.Projects.ShouldNotBeEmpty();

            var sym = await Core.SymbolLocator.FindSymbolByFqnAsync(
                solution, "SmokeProject.SmokeType", "type");
            sym.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task trusted_production_load_attaches_authoritative_complete_acquisition()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution("", "Complete", "CompleteProject");
        var projectPath = Path.Combine(project.RootDir, "CompleteProject", "CompleteProject.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            (await File.ReadAllTextAsync(projectPath)).Replace("net8.0", "net10.0", StringComparison.Ordinal));
        await File.WriteAllTextAsync(
            Path.Combine(project.RootDir, "global.json"),
            "{\"sdk\":{\"version\":\"10.0.301\",\"rollForward\":\"latestPatch\"}}");
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.RootDir);
        using var loader = new WorkspaceLoader();

        var loaded = await loader.LoadWorkspaceResultAsync(sln.SolutionPath, CancellationToken.None);
        var assessment = loaded.Completeness;

        assessment.State.ShouldBe(
            WorkspaceCompletenessState.Complete,
            string.Join(Environment.NewLine, assessment.Diagnostics.Concat(
                assessment.Acquisition!.Outcomes
                    .Where(outcome => outcome.InputId.Contains("Marker", StringComparison.Ordinal))
                    .Select(outcome => $"{outcome.InputId}: {outcome.State}: {outcome.Detail}"))));
        assessment.Acquisition.ShouldNotBeNull().Manifest.DiscoveryState
            .ShouldBe(ManifestDiscoveryState.Closed);
        assessment.Acquisition.Provenance.TrustState.ShouldBe("Trusted");
        assessment.Acquisition.Provenance.RepositoryRoot.ShouldBe(project.RootDir);
        assessment.Acquisition.Provenance.TargetFrameworks.ShouldContain("net10.0");
    }

    [Fact]
    public async Task missing_evaluated_project_reference_keeps_production_load_partial()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "MissingReference", "App");
        var projectPath = await PinSdkAsync(fixture, "App");
        await File.WriteAllTextAsync(projectPath,
            (await File.ReadAllTextAsync(projectPath)).Replace(
                "</Project>",
                "  <ItemGroup><ProjectReference Include=\"../Missing/Missing.csproj\" /></ItemGroup>\n</Project>",
                StringComparison.Ordinal));
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();

        var solution = await loader.GetSolutionAsync(spec.SolutionPath);
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);

        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.Acquisition.ShouldNotBeNull().Outcomes.ShouldContain(outcome =>
            outcome.State == SemanticInputState.Failed
            && outcome.Detail != null
            && outcome.Detail.Contains("project reference", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(
        "<Reference Include=\"DefinitelyMissing\"><HintPath>missing/DefinitelyMissing.dll</HintPath></Reference>",
        nameof(SemanticInputKind.MetadataReference))]
    [InlineData(
        "<PackageReference Include=\"Definitely.Missing.Rt.Test\" Version=\"99.99.99\" />",
        nameof(SemanticInputKind.Package))]
    [InlineData(
        "<FrameworkReference Include=\"Definitely.Missing.Framework\" />",
        nameof(SemanticInputKind.FrameworkReference))]
    public async Task unresolved_declared_reference_keeps_production_load_partial(
        string referenceItem,
        string expectedKind)
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "MissingResolution", "App");
        var projectPath = await PinSdkAsync(fixture, "App");
        await File.WriteAllTextAsync(projectPath,
            (await File.ReadAllTextAsync(projectPath)).Replace(
                "</Project>",
                $"  <ItemGroup>{referenceItem}</ItemGroup>\n</Project>",
                StringComparison.Ordinal));
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();

        var loaded = await loader.LoadWorkspaceResultAsync(spec.SolutionPath, CancellationToken.None);

        loaded.Completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
        var acquisition = loaded.Completeness.Acquisition.ShouldNotBeNull();
        var failedIds = acquisition.Outcomes
            .Where(outcome => outcome.State == SemanticInputState.Failed)
            .Select(outcome => outcome.InputId)
            .ToHashSet(StringComparer.Ordinal);
        acquisition.Manifest.Inputs.ShouldContain(input =>
            input.Kind.ToString() == expectedKind && failedIds.Contains(input.Id));
    }

    [Fact]
    public async Task transitive_project_reference_is_part_of_the_authoritative_requested_closure()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "TransitiveClosure", "App");
        var appPath = await PinSdkAsync(fixture, "App");
        var libraryDirectory = fixture.CreateDirectory("Lib");
        var libraryPath = Path.Combine(libraryDirectory, "Lib.csproj");
        await File.WriteAllTextAsync(libraryPath,
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");
        await File.WriteAllTextAsync(Path.Combine(libraryDirectory, "Library.cs"), "public class Library { }");
        await File.WriteAllTextAsync(appPath,
            (await File.ReadAllTextAsync(appPath)).Replace(
                "</Project>",
                "  <ItemGroup><ProjectReference Include=\"../Lib/Lib.csproj\" /></ItemGroup>\n</Project>",
                StringComparison.Ordinal));
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();

        var loaded = await loader.LoadWorkspaceResultAsync(spec.SolutionPath, CancellationToken.None);

        loaded.Completeness.State.ShouldBe(
            WorkspaceCompletenessState.Complete,
            string.Join(Environment.NewLine,
                loaded.Completeness.Diagnostics.Concat(
                    loaded.Completeness.Acquisition!.Outcomes
                        .Where(outcome => outcome.State != SemanticInputState.Resolved
                            && outcome.State != SemanticInputState.Loaded
                            && outcome.State != SemanticInputState.Excluded)
                        .Select(outcome => $"{outcome.InputId}: {outcome.State}: {outcome.Detail}"))));
        loaded.Completeness.RequestedProjects.ShouldContain(appPath);
        loaded.Completeness.RequestedProjects.ShouldContain(libraryPath);
    }

    [Fact]
    public async Task one_failed_target_framework_context_keeps_multitarget_load_partial()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "MultiTarget", "App");
        var projectPath = await PinSdkAsync(fixture, "App");
        var contents = (await File.ReadAllTextAsync(projectPath))
            .Replace("<TargetFramework>net10.0</TargetFramework>",
                "<TargetFrameworks>net10.0;net9.0</TargetFrameworks>", StringComparison.Ordinal)
            .Replace("</Project>",
                "  <ItemGroup Condition=\"'$(TargetFramework)' == 'net9.0'\"><Compile Include=\"Missing.net9.cs\" /></ItemGroup>\n</Project>",
                StringComparison.Ordinal);
        await File.WriteAllTextAsync(projectPath, contents);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();

        var solution = await loader.GetSolutionAsync(spec.SolutionPath);
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);

        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.Acquisition.ShouldNotBeNull().Provenance.TargetFrameworks
            .ShouldBe(["net10.0", "net9.0"], ignoreOrder: true);
        assessment.Acquisition.Outcomes.ShouldContain(outcome => outcome.State == SemanticInputState.Failed
            && outcome.InputId.Contains("Missing.net9.cs", StringComparison.Ordinal),
            string.Join(Environment.NewLine, assessment.Acquisition.Outcomes.Select(outcome =>
                $"{outcome.InputId}: {outcome.State}: {outcome.Detail}")));
    }

    [Fact]
    public async Task broken_import_returns_typed_incomplete_acquisition()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "BrokenImport", "App");
        var projectPath = await PinSdkAsync(fixture, "App");
        await File.WriteAllTextAsync(projectPath,
            (await File.ReadAllTextAsync(projectPath)).Replace(
                "</Project>",
                "  <Import Project=\"Missing.props\" />\n</Project>",
                StringComparison.Ordinal));
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();

        var solution = await loader.GetSolutionAsync(spec.SolutionPath);
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);

        assessment.State.ShouldNotBe(WorkspaceCompletenessState.Complete);
        assessment.Acquisition.ShouldNotBeNull().Manifest.DiscoveryState
            .ShouldBe(ManifestDiscoveryState.Unknown);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Id == "acquisition.project-evaluation");
    }

    [Fact]
    public async Task physical_source_change_invalidates_the_bound_production_receipt()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "ChangedSource", "App");
        await PinSdkAsync(fixture, "App");
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();
        var solution = await loader.GetSolutionAsync(spec.SolutionPath);
        var sourcePath = Path.Combine(fixture.RootDir, "App", "Marker.cs");
        await File.AppendAllTextAsync(sourcePath, "\npublic class ChangedAfterLoad { }\n");

        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);

        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Id == "acquisition.contradictory-fingerprint");
    }

    [Fact]
    public async Task new_default_glob_source_invalidates_the_bound_input_membership()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "ChangedMembership", "App");
        await PinSdkAsync(fixture, "App");
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();
        var loaded = await loader.LoadWorkspaceResultAsync(spec.SolutionPath, CancellationToken.None);
        loaded.Completeness.State.ShouldBe(WorkspaceCompletenessState.Complete);
        await File.WriteAllTextAsync(
            Path.Combine(fixture.RootDir, "App", "AddedByDefaultGlob.cs"),
            "public class AddedByDefaultGlob { }");

        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(
            loaded.Solution,
            CancellationToken.None);

        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Id == "acquisition.contradictory-fingerprint"
            && diagnostic.InputId == "workspace:input-set");
    }

    [Fact]
    public async Task default_glob_change_during_solution_load_cannot_receive_complete_evidence()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "ChangedDuringLoad", "App");
        await PinSdkAsync(fixture, "App");
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();
        WorkspaceLoader.OpenSolutionOverride.Value = async (workspace, path) =>
        {
            var loadedSolution = await workspace.OpenSolutionAsync(path);
            await File.WriteAllTextAsync(
                Path.Combine(fixture.RootDir, "App", "AddedDuringLoad.cs"),
                "public class AddedDuringLoad { }");
            return loadedSolution;
        };
        try
        {
            var loaded = await loader.LoadWorkspaceResultAsync(spec.SolutionPath, CancellationToken.None);

            loaded.Completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
            loaded.Completeness.Acquisition.ShouldNotBeNull().Outcomes.ShouldContain(outcome =>
                outcome.InputId == "workspace:input-set"
                && outcome.State == SemanticInputState.Failed
                && !string.IsNullOrWhiteSpace(outcome.Detail));
        }
        finally
        {
            WorkspaceLoader.OpenSolutionOverride.Value = null;
        }
    }

    [Fact]
    public async Task new_build_input_after_manifest_discovery_cannot_receive_complete_evidence()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "ChangedDuringDiscovery", "App");
        await PinSdkAsync(fixture, "App");
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();
        WorkspaceLoader.AfterDiscoveryOverride.Value = () => File.WriteAllTextAsync(
            Path.Combine(fixture.RootDir, "Directory.Build.props"),
            "<Project><ItemGroup><PackageReference Include=\"Appeared.After.Discovery\" Version=\"99.99.99\" /></ItemGroup></Project>");
        try
        {
            var loaded = await loader.LoadWorkspaceResultAsync(spec.SolutionPath, CancellationToken.None);

            loaded.Completeness.State.ShouldBe(WorkspaceCompletenessState.Partial);
            loaded.Completeness.Acquisition.ShouldNotBeNull().Outcomes.ShouldContain(outcome =>
                outcome.InputId == "workspace:input-set"
                && outcome.State == SemanticInputState.Failed
                && outcome.Detail != null
                && outcome.Detail.Contains("manifest", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            WorkspaceLoader.AfterDiscoveryOverride.Value = null;
        }
    }

    [Fact]
    public async Task typed_load_returns_failed_evidence_when_solution_open_cannot_establish_scope()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var slnxPath = Path.Combine(fixture.RootDir, "Broken.slnx");
        await File.WriteAllTextAsync(slnxPath, "{}");
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.RootDir);
        using var loader = new WorkspaceLoader();

        var loaded = await loader.LoadWorkspaceResultAsync(slnxPath, CancellationToken.None);

        loaded.Completeness.State.ShouldBe(WorkspaceCompletenessState.Failed);
        loaded.Completeness.Acquisition.ShouldNotBeNull().Manifest.SolutionPath.ShouldBe(slnxPath);
        loaded.Completeness.StructuredDiagnostics.ShouldContain(diagnostic =>
            diagnostic.Id == "acquisition.solution-load"
            && diagnostic.Impact == CompletenessDiagnosticImpact.MinimumScopeUnavailable);
    }

    [Fact]
    public async Task concurrent_solution_loads_are_serialized_and_complete_cleanly()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution("", "Concurrent", "ConcurrentProject");
        var activeLoads = 0;
        var maxActiveLoads = 0;

        WorkspaceLoader.OpenSolutionOverride.Value = async (workspace, path) =>
        {
            path.ShouldBe(sln.SolutionPath);
            var active = Interlocked.Increment(ref activeLoads);
            try
            {
                maxActiveLoads = Math.Max(maxActiveLoads, active);
                await Task.Delay(25);
                return workspace.CurrentSolution;
            }
            finally
            {
                Interlocked.Decrement(ref activeLoads);
            }
        };

        try
        {
            var loads = Enumerable.Range(0, 4)
                .Select(_ => WorkspaceLoader.LoadAsync(sln.SolutionPath))
                .ToArray();

            var results = await Task.WhenAll(loads);

            maxActiveLoads.ShouldBe(1);
            foreach (var (workspace, solution) in results)
            {
                using (workspace)
                {
                    solution.ShouldNotBeNull();
                }
            }
        }
        finally
        {
            WorkspaceLoader.OpenSolutionOverride.Value = null;
        }
    }

    [Fact]
    public async Task successful_load_does_not_emit_progress_banner_to_diagnostic_sink()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution(
            "",
            "Smoke",
            "SmokeProject");
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        try
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
            using (workspace)
            {
                solution.Projects.ShouldNotBeEmpty();
            }

            var output = sink.ToString();
            output.ShouldNotContain("Loading solution", Case.Sensitive);
            output.ShouldNotContain("Loaded ", Case.Sensitive);

            // If any diagnostics surfaced from the real load, they must live under the
            // delimited warnings section, not as raw interleaved lines.
            if (output.Length > 0)
            {
                output.ShouldStartWith("---", Case.Sensitive);
                output.ShouldContain("[workspace-warnings]", Case.Sensitive);
            }
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
        }
    }

    [Fact]
    public async Task handles_missing_solution_file_throws_without_touching_diagnostic_sink()
    {
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        try
        {
            var missing = Path.Combine(Path.GetTempPath(), $"rt-nonexistent-{Guid.NewGuid():N}.sln");

            await Assert.ThrowsAsync<FileNotFoundException>(() => WorkspaceLoader.LoadAsync(missing));

            sink.ToString().ShouldBe(string.Empty);
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
        }
    }

    [Fact]
    public async Task injected_warning_policy_controls_workspace_warning_logging()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution("", "WarningPolicy", "WarningProject");
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.RootDir);
        var logger = new CapturingWorkspaceLogger();
        var showWarnings = false;
        using var loader = new WorkspaceLoader(logger, () => showWarnings);
        WorkspaceLoader.OpenSolutionOverride.Value = async (workspace, path) =>
        {
            WorkspaceDiagnosticRegistry.Record(workspace,
                new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Warning, "injected-warning"));
            return await workspace.OpenSolutionAsync(path);
        };

        try
        {
            await loader.GetSolutionAsync(sln.SolutionPath);
            logger.Entries.ShouldNotContain(entry => entry.Message.Contains("injected-warning", StringComparison.Ordinal));

            showWarnings = true;
            await loader.GetSolutionAsync(sln.SolutionPath);
            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
                && entry.Message == "[workspace-warnings]");
            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Warning
                && entry.Message.Contains("injected-warning", StringComparison.Ordinal));
        }
        finally
        {
            WorkspaceLoader.OpenSolutionOverride.Value = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task injected_logger_reports_workspace_failure_even_when_open_is_cancelled(bool cancelled)
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution("", "FailureReporting", "FailureProject");
        using var trust = RepositoryTrustContext.UseExplicitTrust(project.RootDir);
        var logger = new CapturingWorkspaceLogger();
        using var loader = new WorkspaceLoader(logger, static () => false);
        WorkspaceLoader.OpenSolutionOverride.Value = (workspace, _) =>
        {
            WorkspaceDiagnosticRegistry.Record(workspace,
                new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, "injected-failure"));
            return cancelled
                ? Task.FromCanceled<Solution>(new CancellationToken(true))
                : Task.FromException<Solution>(new InvalidOperationException("injected-open-failure"));
        };

        try
        {
            if (cancelled)
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loader.GetSolutionAsync(sln.SolutionPath));
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() => loader.GetSolutionAsync(sln.SolutionPath));

            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error
                && entry.Message == "[workspace-failures]");
            logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Error
                && entry.Message.Contains("injected-failure", StringComparison.Ordinal));
        }
        finally
        {
            WorkspaceLoader.OpenSolutionOverride.Value = null;
        }
    }

    [Fact]
    public async Task static_test_loader_reports_failures_to_its_test_sink()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution("", "StaticFailure", "StaticProject");
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        WorkspaceLoader.OpenSolutionOverride.Value = (workspace, _) =>
        {
            WorkspaceDiagnosticRegistry.Record(workspace,
                new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Failure, "static-injected-failure"));
            return Task.FromException<Solution>(new InvalidOperationException("static-open-failure"));
        };

        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => WorkspaceLoader.LoadAsync(sln.SolutionPath));
            sink.ToString().ShouldContain("[workspace-failures]");
            sink.ToString().ShouldContain("static-injected-failure");
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
            WorkspaceLoader.OpenSolutionOverride.Value = null;
        }
    }

    private static async Task<string> PinSdkAsync(WorkspaceLoaderTestProject fixture, string projectName)
    {
        var projectPath = Path.Combine(fixture.RootDir, projectName, $"{projectName}.csproj");
        await File.WriteAllTextAsync(
            projectPath,
            (await File.ReadAllTextAsync(projectPath)).Replace("net8.0", "net10.0", StringComparison.Ordinal));
        await File.WriteAllTextAsync(
            Path.Combine(fixture.RootDir, "global.json"),
            "{\"sdk\":{\"version\":\"10.0.301\",\"rollForward\":\"latestPatch\"}}");
        return projectPath;
    }

    private sealed class CapturingWorkspaceLogger : ILogger<WorkspaceLoader>
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
