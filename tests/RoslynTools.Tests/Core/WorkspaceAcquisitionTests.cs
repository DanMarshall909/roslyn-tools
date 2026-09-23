using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class WorkspaceAcquisitionTests
{
    [Fact]
    public async Task arbitrary_adhoc_solution_is_partial_without_acquisition_evidence()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Known.cs", "public class Known { }");
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
            assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
            assessment.RequestedProjects.ShouldBeEmpty("loaded projects must not be recast as requested inputs");
            assessment.Inputs.ShouldContain(input => input.Id == "scope:closure" && input.State == "Unknown");
        }
    }

    [Fact]
    public async Task explicit_closed_world_fixture_can_be_complete_and_cannot_certify_a_changed_snapshot()
    {
        using var fixture = new DeclaredFixture();
        await fixture.AttachReceiptAsync();
        var complete = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        complete.State.ShouldBe(WorkspaceCompletenessState.Complete);
        complete.RequestedProjects.ShouldBe([fixture.ProjectPath]);
        var changed = fixture.Solution.WithDocumentText(fixture.DocumentId, SourceText.From("public class Changed { }"));
        var stale = await WorkspaceCompleteness.AssessSemanticAsync(changed, CancellationToken.None);
        stale.State.ShouldBe(WorkspaceCompletenessState.Partial);
        stale.Diagnostics.ShouldContain(message => message.Contains("snapshot", StringComparison.Ordinal));
    }

    [Fact]
    public async Task missing_expected_project_overrides_a_claimed_successful_receipt()
    {
        using var fixture = new DeclaredFixture(includeMissingProject: true);
        await fixture.AttachReceiptAsync();
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.FailedProjects.ShouldContain(fixture.MissingProjectPath);
        assessment.Inputs.ShouldContain(input => input.Project == fixture.MissingProjectPath && input.State == "Failed");
    }

    [Theory]
    [InlineData("source")]
    [InlineData("reference")]
    [InlineData("context")]
    public async Task closed_receipt_cannot_omit_observed_inputs_in_its_exact_bound_snapshot(string omittedId)
    {
        using var fixture = new DeclaredFixture(omittedInputId: omittedId);
        await fixture.AttachReceiptAsync();
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic => diagnostic.Id == "acquisition.undeclared-observed-input");
    }

    [Fact]
    public async Task closed_receipt_cannot_ignore_an_extra_loaded_project()
    {
        using var fixture = new DeclaredFixture(addUndeclaredProject: true);
        await fixture.AttachReceiptAsync();
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic => diagnostic.Id == "acquisition.undeclared-observed-input");
    }

    [Fact]
    public async Task successful_receipt_for_absent_document_cannot_establish_complete()
    {
        using var fixture = new DeclaredFixture(extraInput: new ExpectedSemanticInput("phantom", SemanticInputKind.SourceDocument,
            true, ProjectPath: "not-an-observed-project", Path: "Missing.cs"));
        await fixture.AttachReceiptAsync();
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic => diagnostic.Id == "acquisition.unobserved-successful-input");
    }

    [Theory]
    [InlineData("Failed")]
    [InlineData("PolicyBlocked")]
    [InlineData("Unknown")]
    public async Task loaded_error_free_project_cannot_upgrade_an_incomplete_collector_receipt(string state)
    {
        using var fixture = new DeclaredFixture();
        var expectedState = Enum.Parse<SemanticInputState>(state);
        await fixture.AttachReceiptAsync(expectedState);
        var compilation = (await fixture.Solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
        compilation.GetDiagnostics().ShouldNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.Acquisition.ShouldNotBeNull().Outcomes.Single(outcome => outcome.InputId == "project").State.ShouldBe(expectedState);
    }

    [Fact]
    public async Task readable_build_input_cannot_upgrade_a_failed_collector_receipt()
    {
        using var files = new WorkspaceLoaderTestProject();
        var path = Path.Combine(files.RootDir, "Known.props");
        File.WriteAllText(path, "<Project/>");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        using var fixture = new DeclaredFixture(extraInput: new ExpectedSemanticInput("build-input", SemanticInputKind.BuildInput,
            true, Path: path, Fingerprint: fingerprint));
        await fixture.AttachReceiptAsync(failedInputId: "build-input");
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.Acquisition.ShouldNotBeNull().Outcomes.Single(outcome => outcome.InputId == "build-input")
            .State.ShouldBe(SemanticInputState.Failed);
    }

    [Fact]
    public async Task changed_build_input_contradicts_receipt_even_when_bound_source_snapshot_is_unchanged()
    {
        using var files = new WorkspaceLoaderTestProject();
        var path = Path.Combine(files.RootDir, "Known.props");
        File.WriteAllText(path, "<Project/>");
        var fingerprint = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        using var fixture = new DeclaredFixture(extraInput: new ExpectedSemanticInput("build-input", SemanticInputKind.BuildInput,
            true, Path: path, Fingerprint: fingerprint));
        await fixture.AttachReceiptAsync();
        File.WriteAllText(path, "<Project><Import Project=\"Missing.props\"/></Project>");
        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(fixture.Solution, CancellationToken.None);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
        assessment.StructuredDiagnostics.ShouldContain(diagnostic => diagnostic.Id == "acquisition.contradictory-fingerprint");
    }

    [Fact]
    public async Task late_workspace_warning_is_retained_with_original_severity_and_unknown_impact()
    {
        using var workspace = new AdhocWorkspace();
        var project = ProjectId.CreateNewId();
        var solution = workspace.CurrentSolution.AddProject(ProjectInfo.Create(project, VersionStamp.Create(), "Late", "Late",
            LanguageNames.CSharp, compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]));
        var document = DocumentId.CreateNewId(project);
        solution = solution.AddDocument(DocumentInfo.Create(document, "Late.cs", loader: new DiagnosticTextLoader(workspace)));
        WorkspaceDiagnosticRegistry.Capture(workspace);
        WorkspaceDiagnosticRegistry.GetStructured(workspace).ShouldBeEmpty();

        var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);

        var warning = assessment.StructuredDiagnostics.Single(diagnostic => diagnostic.Message == DiagnosticTextLoader.Warning);
        warning.Severity.ShouldBe(DiagnosticSeverity.Warning);
        warning.Impact.ShouldBe(CompletenessDiagnosticImpact.Unknown);
        assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
    }

    [Fact]
    public async Task pre_acquisition_solution_inventory_keeps_an_omitted_project_visible()
    {
        MsBuildFixture.Ensure();
        using var files = new WorkspaceLoaderTestProject();
        var solutionPath = Path.Combine(files.RootDir, "Selected.slnx");
        var loadedPath = Path.Combine(files.RootDir, "Loaded.csproj");
        var missingPath = Path.Combine(files.RootDir, "Missing.csproj");
        File.WriteAllText(solutionPath, "<Solution><Project Path=\"Loaded.csproj\"/><Project Path=\"Missing.csproj\"/></Solution>");
        WorkspaceLoader.OpenSolutionOverride.Value = (workspace, _) =>
        {
            var (manifest, receipt) = WorkspaceAcquisitionRegistry.Get(workspace);
            manifest.ShouldNotBeNull().Inputs.Count(input => input.Kind == SemanticInputKind.Project).ShouldBe(2);
            receipt.ShouldBeNull("the expected manifest must exist before any project is acquired");
            var project = ProjectId.CreateNewId();
            return Task.FromResult(workspace.CurrentSolution.AddProject(ProjectInfo.Create(project, VersionStamp.Create(),
                    "Loaded", "Loaded", LanguageNames.CSharp, loadedPath,
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                    metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
                .AddDocument(DocumentId.CreateNewId(project), "Loaded.cs", SourceText.From("public class Loaded { }")));
        };
        try
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
            using (workspace)
            {
                var compilation = (await solution.Projects.Single().GetCompilationAsync()).ShouldNotBeNull();
                compilation.GetDiagnostics().ShouldNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
                var assessment = await WorkspaceCompleteness.AssessSemanticAsync(solution, CancellationToken.None);
                assessment.State.ShouldBe(WorkspaceCompletenessState.Partial);
                assessment.RequestedProjects.ShouldBe([loadedPath, missingPath]);
                assessment.FailedProjects.ShouldContain(missingPath);
                assessment.Acquisition.ShouldNotBeNull().Manifest.DiscoveryState.ShouldBe(ManifestDiscoveryState.Unknown);
                assessment.Acquisition.Provenance.EvaluatorMode
                    .ShouldBe("MSBuild evaluation reconciled with Roslyn snapshot");
                assessment.Acquisition.Provenance.RepositoryRoot.ShouldBeNull();
                assessment.Inputs.ShouldContain(input => input.Kind == "Project" && input.Project == missingPath && input.State == "Failed");
            }
        }
        finally
        {
            WorkspaceLoader.OpenSolutionOverride.Value = null;
        }
    }

    [Fact]
    public async Task sln_inventory_is_independent_of_project_file_existence()
    {
        using var files = new WorkspaceLoaderTestProject();
        var spec = files.AddSolution("", "Selected", "Gone");
        var projectPath = Path.Combine(files.RootDir, "Gone", "Gone.csproj");
        File.Delete(projectPath);
        var seed = await WorkspaceManifestDiscovery.SeedAsync(spec.SolutionPath);
        seed.Manifest.Inputs.ShouldContain(input => input.Kind == SemanticInputKind.Project && input.ProjectPath == projectPath);
        seed.Manifest.DiscoveryState.ShouldBe(ManifestDiscoveryState.Unknown);
    }

    [Fact]
    public async Task malformed_inventory_is_unclosed_and_reports_structured_discovery_failure()
    {
        using var files = new WorkspaceLoaderTestProject();
        var path = Path.Combine(files.RootDir, "Broken.slnx");
        File.WriteAllText(path, "<Solution><Project/></Solution>");
        var seed = await WorkspaceManifestDiscovery.SeedAsync(path);
        seed.Manifest.DiscoveryState.ShouldBe(ManifestDiscoveryState.Unknown);
        seed.Diagnostics.ShouldContain(diagnostic => diagnostic.Id == "manifest.project-discovery"
            && diagnostic.Impact == CompletenessDiagnosticImpact.RequiredInputIncomplete);
    }

    [Fact]
    public async Task locked_package_graph_with_missing_compile_asset_is_not_resolved()
    {
        MsBuildFixture.Ensure();
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "MissingPackageAsset", "App");
        var projectPath = Path.Combine(fixture.RootDir, "App", "App.csproj");
        await File.WriteAllTextAsync(projectPath,
            (await File.ReadAllTextAsync(projectPath))
            .Replace("net8.0", "net10.0", StringComparison.Ordinal)
            .Replace(
                "</Project>",
                "  <ItemGroup><PackageReference Include=\"Package\" Version=\"1.0.0\" /></ItemGroup>\n</Project>",
                StringComparison.Ordinal));
        var packageRoot = fixture.CreateDirectory("packages");
        var packageDirectory = Path.Combine(packageRoot, "package", "1.0.0");
        Directory.CreateDirectory(Path.Combine(packageDirectory, "lib", "net10.0"));
        var packageAssembly = Path.Combine(packageDirectory, "lib", "net10.0", "Package.dll");
        await File.WriteAllBytesAsync(packageAssembly, [1, 2, 3]);
        File.Delete(packageAssembly);
        var objDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootDir, "App", "obj"));
        var assets = new
        {
            version = 3,
            targets = new Dictionary<string, object>
            {
                ["net10.0"] = new Dictionary<string, object>
                {
                    ["Package/1.0.0"] = new
                    {
                        type = "package",
                        compile = new Dictionary<string, object>
                        {
                            ["lib/net10.0/Package.dll"] = new { }
                        }
                    }
                }
            },
            libraries = new Dictionary<string, object>
            {
                ["Package/1.0.0"] = new { type = "package", path = "package/1.0.0" }
            },
            packageFolders = new Dictionary<string, object> { [packageRoot + Path.DirectorySeparatorChar] = new { } },
            project = new
            {
                frameworks = new Dictionary<string, object>
                {
                    ["net10.0"] = new
                    {
                        frameworkReferences = new Dictionary<string, object>
                        {
                            ["Microsoft.NETCore.App"] = new { }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(objDirectory.FullName, "project.assets.json"),
            JsonSerializer.Serialize(assets));

        var seed = await WorkspaceManifestDiscovery.SeedAsync(spec.SolutionPath);
        var discovery = await WorkspaceAcquisitionCollector.DiscoverAsync(
            seed,
            new Dictionary<string, string>(),
            CancellationToken.None);

        var packageInput = discovery.Manifest.Inputs.Single(input =>
            input.Kind == SemanticInputKind.Package && input.Path == "Package/1.0.0");
        discovery.Outcomes.Single(outcome => outcome.InputId == packageInput.Id).State
            .ShouldBe(SemanticInputState.Failed);
        discovery.Outcomes.ShouldContain(outcome =>
            outcome.State == SemanticInputState.Failed
            && outcome.InputId.Contains("Package.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task package_asset_empty_folder_marker_does_not_require_a_physical_file()
    {
        MsBuildFixture.Ensure();
        using var fixture = new WorkspaceLoaderTestProject();
        var spec = fixture.AddSolution("", "EmptyPackageAsset", "App");
        var projectPath = Path.Combine(fixture.RootDir, "App", "App.csproj");
        await File.WriteAllTextAsync(projectPath,
            (await File.ReadAllTextAsync(projectPath))
            .Replace("net8.0", "net10.0", StringComparison.Ordinal)
            .Replace(
                "</Project>",
                "  <ItemGroup><PackageReference Include=\"Package\" Version=\"1.0.0\" /></ItemGroup>\n</Project>",
                StringComparison.Ordinal));
        var packageRoot = fixture.CreateDirectory("packages");
        Directory.CreateDirectory(Path.Combine(packageRoot, "package", "1.0.0", "lib", "net10.0"));
        var objDirectory = Directory.CreateDirectory(Path.Combine(fixture.RootDir, "App", "obj"));
        var assets = new
        {
            version = 3,
            targets = new Dictionary<string, object>
            {
                ["net10.0"] = new Dictionary<string, object>
                {
                    ["Package/1.0.0"] = new
                    {
                        type = "package",
                        compile = new Dictionary<string, object> { ["lib/net10.0/_._"] = new { } }
                    }
                }
            },
            libraries = new Dictionary<string, object>
            {
                ["Package/1.0.0"] = new { type = "package", path = "package/1.0.0" }
            },
            packageFolders = new Dictionary<string, object> { [packageRoot + Path.DirectorySeparatorChar] = new { } },
            project = new
            {
                frameworks = new Dictionary<string, object>
                {
                    ["net10.0"] = new
                    {
                        frameworkReferences = new Dictionary<string, object>
                        {
                            ["Microsoft.NETCore.App"] = new { }
                        }
                    }
                }
            }
        };
        await File.WriteAllTextAsync(
            Path.Combine(objDirectory.FullName, "project.assets.json"),
            JsonSerializer.Serialize(assets));

        var seed = await WorkspaceManifestDiscovery.SeedAsync(spec.SolutionPath);
        var discovery = await WorkspaceAcquisitionCollector.DiscoverAsync(
            seed,
            new Dictionary<string, string>(),
            CancellationToken.None);

        var packageInput = discovery.Manifest.Inputs.Single(input =>
            input.Kind == SemanticInputKind.Package && input.Path == "Package/1.0.0");
        discovery.Outcomes.Single(outcome => outcome.InputId == packageInput.Id).State
            .ShouldBe(SemanticInputState.Resolved);
        discovery.Outcomes.ShouldNotContain(outcome =>
            outcome.State == SemanticInputState.Failed
            && outcome.InputId.Contains("_._", StringComparison.Ordinal));
    }

    [Fact]
    public async Task byte_identical_duplicate_metadata_asset_is_a_validated_exclusion()
    {
        using var files = new WorkspaceLoaderTestProject();
        var selectedPath = CreateMetadataAssembly(files.RootDir);
        var duplicatePath = Path.Combine(files.RootDir, "Duplicate.dll");
        File.Copy(selectedPath, duplicatePath);
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Known.cs", "public class Known { }");
        var (workspace, original) = builder.Build();
        using (workspace)
        {
            var solution = original.AddMetadataReference(
                original.ProjectIds.Single(),
                MetadataReference.CreateFromFile(selectedPath));
            var project = solution.Projects.Single();
            var projectPath = WorkspaceCompleteness.ProjectIdentity(project);
            const string context = "declared-context";
            var inputs = new[]
            {
                new ExpectedSemanticInput("project", SemanticInputKind.Project, true, ProjectPath: projectPath),
                new ExpectedSemanticInput("context", SemanticInputKind.CompilationContext, true,
                    ProjectPath: projectPath, Context: context),
                new ExpectedSemanticInput("metadata-package:duplicate", SemanticInputKind.MetadataReference, true,
                    ProjectPath: projectPath, Context: context, Path: duplicatePath),
                new ExpectedSemanticInput("closure", SemanticInputKind.ManifestClosure, true),
                new ExpectedSemanticInput("snapshot", SemanticInputKind.SourceSnapshot, true)
            };
            var discovery = new WorkspaceAcquisitionDiscovery(
                new SemanticInputManifest(null, inputs, ManifestDiscoveryState.Closed),
                inputs.Select(input => new SemanticInputOutcome(
                        input.Id,
                        input.Kind == SemanticInputKind.SourceSnapshot
                            ? SemanticInputState.Unknown
                            : SemanticInputState.Resolved))
                    .ToImmutableArray(),
                [],
                "fixture",
                [],
                "fixture",
                true);

            var completed = await WorkspaceAcquisitionCollector.CompleteAsync(
                solution,
                discovery,
                workspaceIdentity: null,
                inputFingerprint: null,
                preLoadInputSet: null,
                discoveryWindowStable: true,
                cancellationToken: CancellationToken.None);

            var duplicate = completed.Outcomes.Single(outcome => outcome.InputId == "metadata-package:duplicate");
            duplicate.State.ShouldBe(SemanticInputState.Excluded);
            duplicate.ExclusionValidated.ShouldBeTrue();
            duplicate.Detail.ShouldNotBeNull().ShouldContain("byte-identical");
        }
    }

    [Fact]
    public async Task byte_identical_aliased_metadata_reference_is_not_a_validated_package_exclusion()
    {
        using var files = new WorkspaceLoaderTestProject();
        var selectedPath = CreateMetadataAssembly(files.RootDir);
        var duplicatePath = Path.Combine(files.RootDir, "Duplicate.dll");
        File.Copy(selectedPath, duplicatePath);
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Known.cs", "public class Known { }");
        var (workspace, original) = builder.Build();
        using (workspace)
        {
            var aliasedReference = MetadataReference.CreateFromFile(
                selectedPath,
                new MetadataReferenceProperties(
                    MetadataImageKind.Assembly,
                    aliases: ImmutableArray.Create("PackageAlias")));
            var solution = original.AddMetadataReference(original.ProjectIds.Single(), aliasedReference);
            var project = solution.Projects.Single();
            var projectPath = WorkspaceCompleteness.ProjectIdentity(project);
            const string context = "declared-context";
            var inputs = new[]
            {
                new ExpectedSemanticInput("project", SemanticInputKind.Project, true, ProjectPath: projectPath),
                new ExpectedSemanticInput("context", SemanticInputKind.CompilationContext, true,
                    ProjectPath: projectPath, Context: context),
                new ExpectedSemanticInput("metadata-package:duplicate", SemanticInputKind.MetadataReference, true,
                    ProjectPath: projectPath, Context: context, Path: duplicatePath),
                new ExpectedSemanticInput("closure", SemanticInputKind.ManifestClosure, true),
                new ExpectedSemanticInput("snapshot", SemanticInputKind.SourceSnapshot, true)
            };
            var discovery = new WorkspaceAcquisitionDiscovery(
                new SemanticInputManifest(null, inputs, ManifestDiscoveryState.Closed),
                inputs.Select(input => new SemanticInputOutcome(
                        input.Id,
                        input.Kind == SemanticInputKind.SourceSnapshot
                            ? SemanticInputState.Unknown
                            : SemanticInputState.Resolved))
                    .ToImmutableArray(),
                [],
                "fixture",
                [],
                "fixture",
                true);

            var completed = await WorkspaceAcquisitionCollector.CompleteAsync(
                solution,
                discovery,
                workspaceIdentity: null,
                inputFingerprint: null,
                preLoadInputSet: null,
                discoveryWindowStable: true,
                cancellationToken: CancellationToken.None);

            var duplicate = completed.Outcomes.Single(outcome => outcome.InputId == "metadata-package:duplicate");
            duplicate.State.ShouldBe(SemanticInputState.Failed);
            duplicate.ExclusionValidated.ShouldBeFalse();
        }
    }

    [Theory]
    [InlineData("generator", nameof(SemanticInputKind.GeneratorStage))]
    [InlineData("analyzer", nameof(SemanticInputKind.AnalyzerStage))]
    public async Task analyzer_or_generator_load_failure_cannot_complete_acquisition(
        string failingStage,
        string expectedKindName)
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Known.cs", "public class Known { }");
        var (workspace, original) = builder.Build();
        using (workspace)
        {
            var projectId = original.ProjectIds.Single();
            var solution = original.AddAnalyzerReference(projectId, new FailingAnalyzerReference(failingStage));
            var project = solution.GetProject(projectId).ShouldNotBeNull();
            var projectPath = WorkspaceCompleteness.ProjectIdentity(project);
            const string context = "declared-context";
            var inputs = new[]
            {
                new ExpectedSemanticInput("project", SemanticInputKind.Project, true, ProjectPath: projectPath),
                new ExpectedSemanticInput("context", SemanticInputKind.CompilationContext, true,
                    ProjectPath: projectPath, Context: context),
                new ExpectedSemanticInput("closure", SemanticInputKind.ManifestClosure, true),
                new ExpectedSemanticInput("snapshot", SemanticInputKind.SourceSnapshot, true),
                new ExpectedSemanticInput("input-set", SemanticInputKind.WorkspaceInputSet, true),
                new ExpectedSemanticInput("evaluation", SemanticInputKind.EvaluationStage, true,
                    ProjectPath: projectPath, Context: context),
                new ExpectedSemanticInput("sdk", SemanticInputKind.Sdk, true),
                new ExpectedSemanticInput("package", SemanticInputKind.Package, true),
                new ExpectedSemanticInput("build", SemanticInputKind.BuildInput, true),
                new ExpectedSemanticInput("framework", SemanticInputKind.FrameworkReference, true),
                new ExpectedSemanticInput("generators", SemanticInputKind.GeneratorStage, true,
                    ProjectPath: projectPath, Context: context),
                new ExpectedSemanticInput("analyzers", SemanticInputKind.AnalyzerStage, true,
                    ProjectPath: projectPath, Context: context)
            };
            var manifest = new SemanticInputManifest(null, inputs, ManifestDiscoveryState.Closed);
            var discovery = new WorkspaceAcquisitionDiscovery(
                manifest,
                inputs.Select(input => new SemanticInputOutcome(input.Id,
                    input.Kind == SemanticInputKind.SourceSnapshot ? SemanticInputState.Unknown : SemanticInputState.Resolved))
                    .ToImmutableArray(),
                [],
                "fixture",
                [],
                "fixture",
                true);

            var completed = await WorkspaceAcquisitionCollector.CompleteAsync(
                solution,
                discovery,
                workspaceIdentity: null,
                inputFingerprint: null,
                preLoadInputSet: null,
                discoveryWindowStable: true,
                cancellationToken: CancellationToken.None);
            var expectedKind = Enum.Parse<SemanticInputKind>(expectedKindName);
            var stage = completed.Manifest.Inputs.Single(input => input.Kind == expectedKind);

            completed.Outcomes.Single(outcome => outcome.InputId == stage.Id).State
                .ShouldBe(SemanticInputState.Failed);
            WorkspaceCompletenessPolicy.Evaluate(
                    completed.Manifest,
                    completed.Outcomes,
                    [],
                    hasUsableRequestedScope: true,
                    snapshotMatches: true)
                .State.ShouldBe(WorkspaceCompletenessState.Partial);
        }
    }

    private sealed class DiagnosticTextLoader(Workspace workspace) : TextLoader
    {
        internal const string Warning = "Un diagnostic localise sans mots cles anglais.";

        public override Task<TextAndVersion> LoadTextAndVersionAsync(LoadTextOptions options, CancellationToken cancellationToken)
        {
            WorkspaceDiagnosticRegistry.Record(workspace, new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Warning, Warning));
            return Task.FromResult(TextAndVersion.Create(SourceText.From("public class Late { }"), VersionStamp.Create()));
        }
    }

    private static string CreateMetadataAssembly(string directory)
    {
        var path = Path.Combine(directory, "Selected.dll");
        var compilation = CSharpCompilation.Create(
            $"PackageFixture{Guid.NewGuid():N}",
            [CSharpSyntaxTree.ParseText("public sealed class PackageMarker { }")],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = File.Create(path);
        var result = compilation.Emit(stream);
        result.Success.ShouldBeTrue(string.Join(Environment.NewLine, result.Diagnostics));
        return path;
    }

    private sealed class FailingAnalyzerReference(string failingStage) : AnalyzerReference
    {
        public override string FullPath => "/virtual/failing-analyzer.dll";

        public override object Id { get; } = new();

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzers(string language)
            => failingStage == "analyzer" ? throw new InvalidOperationException("Analyzer load failed.") : [];

        public override ImmutableArray<DiagnosticAnalyzer> GetAnalyzersForAllLanguages()
            => GetAnalyzers(LanguageNames.CSharp);

        public override ImmutableArray<ISourceGenerator> GetGenerators(string language)
            => failingStage == "generator" ? throw new InvalidOperationException("Generator load failed.") : [];

        public override ImmutableArray<ISourceGenerator> GetGeneratorsForAllLanguages()
            => GetGenerators(LanguageNames.CSharp);
    }

    /// <summary>One explicitly specified in-memory compilation, not a helper that certifies arbitrary workspaces.</summary>
    private sealed class DeclaredFixture : IDisposable
    {
        private const string Source = "public class Known { }";
        private readonly AdhocWorkspace _workspace = new();
        private readonly SemanticInputManifest _manifest;

        internal DeclaredFixture(bool includeMissingProject = false, ExpectedSemanticInput? extraInput = null,
            string? omittedInputId = null, bool addUndeclaredProject = false)
        {
            var directory = Path.Combine(Path.GetTempPath(), $"declared-workspace-{Guid.NewGuid():N}");
            ProjectPath = Path.Combine(directory, "Known.csproj");
            MissingProjectPath = Path.Combine(directory, "Missing.csproj");
            var sourcePath = Path.Combine(directory, "Known.cs");
            var project = ProjectId.CreateNewId();
            DocumentId = DocumentId.CreateNewId(project);
            var inputs = new List<ExpectedSemanticInput>
            {
                new("closure", SemanticInputKind.ManifestClosure, true),
                new("project", SemanticInputKind.Project, true, ProjectPath: ProjectPath),
                new("context", SemanticInputKind.CompilationContext, true, ProjectPath: ProjectPath, Context: $"roslyn-context:{project.Id:D}"),
                new("source", SemanticInputKind.SourceDocument, true, ProjectPath: ProjectPath, Path: sourcePath,
                    Fingerprint: Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Source)))),
                new("reference", SemanticInputKind.MetadataReference, true, ProjectPath: ProjectPath,
                    Path: WorkspaceCompleteness.MetadataReferenceIdentity(MetadataReference.CreateFromFile(typeof(object).Assembly.Location), 0)),
                new("snapshot", SemanticInputKind.SourceSnapshot, true),
                new("input-set", SemanticInputKind.WorkspaceInputSet, true),
                new("evaluation", SemanticInputKind.EvaluationStage, true),
                new("sdk", SemanticInputKind.Sdk, true),
                new("package", SemanticInputKind.Package, true),
                new("build", SemanticInputKind.BuildInput, true),
                new("framework", SemanticInputKind.FrameworkReference, true),
                new("generators", SemanticInputKind.GeneratorStage, true),
                new("analyzers", SemanticInputKind.AnalyzerStage, true)
            };
            if (includeMissingProject)
                inputs.Add(new ExpectedSemanticInput("missing", SemanticInputKind.Project, true, ProjectPath: MissingProjectPath));
            if (extraInput is not null)
                inputs.Add(extraInput);
            inputs.RemoveAll(input => input.Id == omittedInputId);
            _manifest = new SemanticInputManifest(null, inputs, ManifestDiscoveryState.Closed);
            WorkspaceAcquisitionRegistry.Declare(_workspace, _manifest);
            Solution = _workspace.CurrentSolution.AddProject(ProjectInfo.Create(project, VersionStamp.Create(), "Known", "Known",
                    LanguageNames.CSharp, ProjectPath,
                    compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
                    metadataReferences: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)]))
                .AddDocument(DocumentId, "Known.cs", SourceText.From(Source), filePath: sourcePath);
            if (addUndeclaredProject)
                Solution = Solution.AddProject(ProjectId.CreateNewId(), "Undeclared", "Undeclared", LanguageNames.CSharp);
        }

        internal string ProjectPath { get; }
        internal string MissingProjectPath { get; }
        internal DocumentId DocumentId { get; }
        internal Solution Solution { get; }

        internal async Task AttachReceiptAsync(SemanticInputState projectState = SemanticInputState.Resolved, string? failedInputId = null)
        {
            var outcomes = _manifest.Inputs.Select(input => input.Kind is SemanticInputKind.EvaluationStage
                    or SemanticInputKind.Sdk or SemanticInputKind.Package or SemanticInputKind.BuildInput or SemanticInputKind.FrameworkReference
                    or SemanticInputKind.GeneratorStage or SemanticInputKind.AnalyzerStage
                    && input.Id != "build-input"
                ? new SemanticInputOutcome(input.Id, SemanticInputState.Excluded,
                    Detail: "Explicit fixture supplies its only source and framework reference; no MSBuild, analyzer or generator inputs exist.",
                    ExclusionValidated: true)
                : new SemanticInputOutcome(input.Id, input.Id == failedInputId ? SemanticInputState.Failed
                    : input.Id == "project" ? projectState : SemanticInputState.Resolved, input.Fingerprint));
            WorkspaceAcquisitionRegistry.Attach(_workspace, new WorkspaceAcquisitionReceipt(Solution, _manifest, outcomes,
                new WorkspaceEvaluatorProvenance(null, null, await SolutionRevision.ComputeAsync(Solution),
                    "Explicit in-memory C# DLL", [], "NotRequired: in-memory compilation", typeof(Compilation).Assembly.FullName!,
                    "No analyzer/generator references declared", "Untrusted", "NoEvaluation")));
        }

        public void Dispose() => _workspace.Dispose();
    }
}
