using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MoveSliceServiceTests
{
    private static readonly string[] CaseFindingTypes =
    [
        "WageClaims.Infrastructure.Cases.Findings.CaseFindingAssessmentStatuses",
        "WageClaims.Infrastructure.Cases.Findings.CaseFindingLineFact",
        "WageClaims.Infrastructure.Cases.Findings.CaseFindingCalculationFact",
        "WageClaims.Infrastructure.Cases.Findings.CaseFindingClaimFact",
        "WageClaims.Infrastructure.Cases.Findings.CaseJobFindingSummary",
        "WageClaims.Infrastructure.Cases.Findings.CaseFindingSummary",
        "WageClaims.Infrastructure.Cases.Findings.ICaseFindingRepository",
        "WageClaims.Infrastructure.Cases.Findings.CaseFindingService"
    ];

    [Fact]
    public async Task plans_the_wage_claims_multi_declaration_slice_and_external_references()
    {
        using var fixture = BuildCaseFindingFixture();
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                CaseFindingTypes,
                SourceNamespace: null,
                TargetProject: "Application",
                TargetNamespace: "WageClaims.Application.Cases.Findings",
                AnalyzeDependencies: true,
                UpdateProjectReferences: true,
                ProjectReferenceJustification: "Infrastructure keeps the Cosmos adapter behind the Application port.",
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.Applicable.ShouldBeTrue(
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(item => item.Message)));
        preparation.Result.Types.Count.ShouldBe(8);
        preparation.Result.Types.ShouldAllBe(type =>
            type.SelectionReason == MoveSliceTypeSelectionReason.Explicit);
        preparation.Result.FileMoves.Count.ShouldBe(3);
        preparation.Result.FileMoves.ShouldContain(move => move.DestinationPath.EndsWith(
            Path.Combine("Application", "Cases", "Findings", "CaseFindingModels.cs"),
            StringComparison.OrdinalIgnoreCase));
        preparation.Result.ReferenceEdits.Select(edit => Path.GetFileName(edit.Path)).ShouldBe(
            ["CalculateClaim.cs", "CosmosCaseFindingRepository.cs"],
            ignoreOrder: true);
        preparation.Result.ProjectEdits.ShouldContain(edit =>
            edit.Kind == MoveSliceProjectEditKind.ProjectReference
            && edit.Project == "Infrastructure"
            && edit.ExplicitlyRequested
            && edit.Justification != null);
        preparation.Result.RequiredRelatedTypes.ShouldBeEmpty();
        preparation.Result.ChangedDocumentCount.ShouldBe(5);
    }

    [Fact]
    public async Task recursive_namespace_selection_reports_child_types_and_rooted_destinations()
    {
        using var fixture = new AdhocProjectBuilder("Legacy")
            .WithDefaultNamespace("Legacy.Calculation.Application")
            .AddSource(
                "RootService.cs",
                "namespace Legacy.Calculation.Application; public class RootService { }")
            .AddSource(
                "Cases/Finding.cs",
                "namespace Legacy.Calculation.Application.Cases; public record Finding(int Value);");
        fixture.AddProject("Application").WithDefaultNamespace("WageClaims.Application");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Namespace,
                [],
                "Legacy.Calculation.Application",
                "Application",
                "WageClaims.Application.Calculation",
                AnalyzeDependencies: false,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.Types.Select(type => type.TargetType).ShouldBe(
            [
                "WageClaims.Application.Calculation.Cases.Finding",
                "WageClaims.Application.Calculation.RootService"
            ],
            ignoreOrder: true);
        preparation.Result.FileMoves.ShouldContain(move => move.DestinationPath.EndsWith(
            Path.Combine("Application", "Calculation", "RootService.cs"),
            StringComparison.OrdinalIgnoreCase));
        preparation.Result.FileMoves.ShouldContain(move => move.DestinationPath.EndsWith(
            Path.Combine("Application", "Calculation", "Cases", "Finding.cs"),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task moved_source_preserves_a_binding_to_its_former_parent_namespace()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .WithDefaultNamespace("Legacy")
            .AddSource("Policy.cs", "namespace Legacy; public sealed class Policy { }")
            .AddSource(
                "Feature/Service.cs",
                "namespace Legacy.Feature; public sealed class Service(Policy policy) { }");
        fixture.AddProject("Target").WithDefaultNamespace("App");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Feature.Service"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Feature",
                AnalyzeDependencies: true,
                UpdateProjectReferences: true,
                ProjectReferenceJustification: "The moved service retains its reviewed policy dependency.",
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        var rewritten = preparation.ChangedSourceSolution.Projects
            .Single(project => project.Name == "Source")
            .Documents.Single(document => document.Name == "Service.cs");
        var text = (await rewritten.GetTextAsync()).ToString();
        text.ShouldContain("using Legacy;");
        text.ShouldContain("namespace App.Feature;");
        preparation.Result.Applicable.ShouldBeTrue(
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task relative_namespace_qualifiers_are_retargeted_without_prefix_duplication()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource(
                "Service.cs",
                "namespace Legacy.Feature; public sealed class Service { }");
        fixture.AddProject("Target").WithDefaultNamespace("App");
        fixture.AddProject("Consumer")
            .ReferenceProject("Source")
            .AddSource(
                "Consumer.cs",
                "namespace Legacy; public sealed class Consumer(Feature.Service service) { }");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Feature.Service"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Feature",
                AnalyzeDependencies: false,
                UpdateProjectReferences: true,
                ProjectReferenceJustification: "Retarget the reviewed consumer to the destination project.",
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        var rewritten = preparation.ChangedSourceSolution.Projects
            .Single(project => project.Name == "Consumer")
            .Documents.Single();
        var text = (await rewritten.GetTextAsync()).ToString();
        text.ShouldContain("App.Feature.Service");
        text.ShouldNotContain("App.Feature.Feature.Service");
        preparation.Result.Applicable.ShouldBeTrue(
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task dependency_analysis_reports_but_never_silently_selects_related_types()
    {
        using var fixture = new AdhocProjectBuilder("Infrastructure")
            .AddSource(
                "Service.cs",
                "namespace Legacy; public class Service(SourcePolicy policy) { public SourcePolicy Policy => policy; }")
            .AddSource(
                "SourcePolicy.cs",
                "namespace Legacy; public class SourcePolicy { }");
        fixture.AddProject("Application").WithDefaultNamespace("App.Application");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Service"],
                SourceNamespace: null,
                TargetProject: "Application",
                TargetNamespace: "App.Application.Feature",
                AnalyzeDependencies: true,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.Types.Select(type => type.SourceType).ShouldBe(["Legacy.Service"]);
        var related = preparation.Result.RequiredRelatedTypes.ShouldHaveSingleItem();
        related.Type.ShouldBe("Legacy.SourcePolicy");
        related.Evidence.ShouldNotBeEmpty();
        preparation.Result.Applicable.ShouldBeFalse();
        preparation.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == "project-reference-not-authorized");
    }

    [Fact]
    public async Task constructed_tuples_of_target_project_types_are_not_source_owned_dependencies()
    {
        using var fixture = BuildWageClaimsRuleItemFixture();
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                [
                    "WageClaim.Function.Services.IRuleItemManagementService",
                    "WageClaim.Function.Services.RuleItemManagementService"
                ],
                SourceNamespace: null,
                TargetProject: "Application",
                TargetNamespace: "WageClaims.Application.Rules",
                AnalyzeDependencies: true,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.RequiredRelatedTypes.ShouldBeEmpty();
        preparation.Result.ProjectEdits.ShouldNotContain(edit =>
            edit.Kind == MoveSliceProjectEditKind.ProjectReference
            && edit.Project == "Application");
        preparation.Result.Applicable.ShouldBeTrue(
            string.Join(Environment.NewLine, preparation.Diagnostics.Select(item => item.Message)));
    }

    [Fact]
    public async Task source_defined_generic_dependencies_remain_fail_closed()
    {
        using var fixture = new AdhocProjectBuilder("Infrastructure")
            .AddSource(
                "Service.cs",
                """
                using WageClaims.Application.Profiles;
                namespace Legacy;
                public sealed class Service
                {
                    public ProfileHeaderRecord Read(SourceEnvelope<ProfileHeaderRecord> result) => result.Value;
                }
                """)
            .AddSource(
                "SourceEnvelope.cs",
                "namespace Legacy; public sealed record SourceEnvelope<T>(T Value);");
        fixture.AddProject("Application")
            .AddSource(
                "ProfileHeaderRecord.cs",
                "namespace WageClaims.Application.Profiles; public sealed record ProfileHeaderRecord;");
        fixture.AddProject("Infrastructure").ReferenceProject("Application");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Service"],
                SourceNamespace: null,
                TargetProject: "Application",
                TargetNamespace: "WageClaims.Application.Services",
                AnalyzeDependencies: true,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.RequiredRelatedTypes.ShouldContain(related =>
            related.Type == "Legacy.SourceEnvelope<T>"
            && related.Project == "Infrastructure");
        preparation.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == "project-reference-not-authorized");
        preparation.Result.Applicable.ShouldBeFalse();
    }

    [Fact]
    public async Task a_multi_declaration_file_discloses_unrequested_co_located_types()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource(
                "Types.cs",
                "namespace Legacy; public record Selected; public record CoLocated;");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Selected"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Target.Feature",
                AnalyzeDependencies: false,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.Types.Count.ShouldBe(2);
        preparation.Result.Types.Single(type => type.SourceType == "Legacy.Selected")
            .SelectionReason.ShouldBe(MoveSliceTypeSelectionReason.Explicit);
        preparation.Result.Types.Single(type => type.SourceType == "Legacy.CoLocated")
            .SelectionReason.ShouldBe(MoveSliceTypeSelectionReason.CoLocatedDeclaration);
        preparation.Result.FileMoves.Count.ShouldBe(1);
    }

    [Fact]
    public async Task source_generated_partial_declarations_fail_closed_with_a_typed_error()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource(
                "Service.cs",
                "namespace Legacy; public sealed partial class Service { }");
        fixture.AddProject("Source").AddGeneratedSource(
            "Service.g.cs",
            "namespace Legacy; public sealed partial class Service { public int Generated => 1; }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var result = await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Service"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Target.Feature",
                AnalyzeDependencies: true,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null));

        RtError? error = null;
        result.Match(_ => { }, value => error = value);
        error.ShouldNotBeNull();
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("Legacy.Service");
        error.Message.ShouldContain("source-generated declaration");
    }

    [Fact]
    public async Task explicit_friend_assembly_edits_are_justified_and_disclosed_in_the_manifest()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", "namespace Legacy; internal sealed class Service { }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Service"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Target.Feature",
                AnalyzeDependencies: false,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [new InternalsVisibleToEditRequest("Source", "Target")],
                InternalsVisibleToJustification: "Preserve the reviewed internal adapter seam."))).Unwrap();

        var edit = preparation.Result.ProjectEdits.Single(candidate =>
            candidate.Kind == MoveSliceProjectEditKind.InternalsVisibleTo);
        edit.Project.ShouldBe("Source");
        edit.ExplicitlyRequested.ShouldBeTrue();
        edit.Justification.ShouldBe("Preserve the reviewed internal adapter seam.");
        preparation.ProjectFileEdits.ShouldContain(write =>
            write.Content.Contains("InternalsVisibleTo Include=\"Target\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task incomplete_original_compilation_fails_before_a_manifest_is_prepared()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Broken.cs", "namespace Legacy; public class Broken { Missing Value; }");
        fixture.AddProject("Target");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var result = await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Broken"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Target",
                AnalyzeDependencies: false,
                UpdateProjectReferences: false,
                ProjectReferenceJustification: null,
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null));

        result.Match(_ => false, _ => true).ShouldBeTrue();
    }

    [Fact]
    public async Task an_existing_destination_fails_before_the_writer_boundary()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", "namespace Legacy; public sealed class Service { }");
        fixture.AddProject("Target")
            .WithDefaultNamespace("App.Target")
            .AddSource(
                "Feature/Service.cs",
                "namespace App.Target.Feature; public sealed class ExistingService { }");
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);
        var writer = new RecordingWriter();
        var operation = new MoveSliceOperation(
            new FixedSessionFactory(workspace, solution),
            new MoveSliceService(),
            new PreparedChangeSetExecutor(writer));

        var result = await operation.ExecuteAsync(new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Target",
            ToNamespace: "App.Target.Feature"));

        RtError? error = null;
        result.Match(_ => { }, value => error = value);
        error.ShouldNotBeNull();
        error.Message.ShouldContain("already exists");
        writer.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task dry_run_returns_the_complete_manifest_without_crossing_the_writer_boundary()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", "namespace Legacy; public sealed class Service { }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);
        var writer = new RecordingWriter();
        var operation = new MoveSliceOperation(
            new FixedSessionFactory(workspace, solution),
            new MoveSliceService(),
            new PreparedChangeSetExecutor(writer));

        var execution = (await operation.ExecuteAsync(new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Target",
            ToNamespace: "App.Target.Feature"))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value.ShouldNotBeNull().Applicable.ShouldBeTrue();
        execution.AffectedPaths.ShouldContain(path => path.EndsWith("Service.cs"));
        writer.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task cli_dry_run_reaches_the_shared_operation_and_leaves_files_untouched()
    {
        const string sourceText = "namespace Legacy; public sealed class Service { }";
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", sourceText);
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        var sourcePath = Path.Combine(fixture.Directory, "Source", "Service.cs");
        var targetPath = Path.Combine(fixture.Directory, "Target", "Feature", "Service.cs");
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            using var workspaceOverride = WorkspaceLoader.UseOverride(workspace, solution);
            var services = new ServiceCollection();
            global::RoslynTools.Bootstrap.RegisterServices(services);
            await using var provider = services.BuildServiceProvider();

            var exitCode = await App.RunAsync(
                [
                    "move-slice",
                    "--type", "Legacy.Service",
                    "--to-project", "Target",
                    "--to-namespace", "App.Target.Feature",
                    "--solution", fixture.SolutionPath,
                    "--json",
                    "--trust-repository"
                ],
                provider);

            exitCode.ShouldBe(0, stderr.ToString());
            using var payload = JsonDocument.Parse(stdout.ToString());
            var execution = payload.RootElement.GetProperty("data");
            execution.GetProperty("status").GetInt32().ShouldBe((int)MutationExecutionStatus.DryRun);
            execution.GetProperty("value").GetProperty("applicable").GetBoolean().ShouldBeTrue();
            File.ReadAllText(sourcePath).ShouldBe(sourceText);
            File.Exists(targetPath).ShouldBeFalse();
            stderr.ToString().ShouldBeEmpty();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            workspace.Dispose();
        }
    }

    [Fact]
    public async Task cli_dependency_analysis_ignores_constructed_target_types()
    {
        using var fixture = BuildWageClaimsRuleItemFixture();
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            using var workspaceOverride = WorkspaceLoader.UseOverride(workspace, solution);
            var services = new ServiceCollection();
            global::RoslynTools.Bootstrap.RegisterServices(services);
            await using var provider = services.BuildServiceProvider();

            var exitCode = await App.RunAsync(
                [
                    "move-slice",
                    "--type", "WageClaim.Function.Services.IRuleItemManagementService",
                    "--type", "WageClaim.Function.Services.RuleItemManagementService",
                    "--to-project", "Application",
                    "--to-namespace", "WageClaims.Application.Rules",
                    "--analyze-dependencies",
                    "--solution", fixture.SolutionPath,
                    "--json",
                    "--trust-repository"
                ],
                provider);

            exitCode.ShouldBe(0, stderr.ToString());
            using var payload = JsonDocument.Parse(stdout.ToString());
            var value = payload.RootElement.GetProperty("data").GetProperty("value");
            value.GetProperty("applicable").GetBoolean().ShouldBeTrue();
            value.GetProperty("requiredRelatedTypes").GetArrayLength().ShouldBe(0);
            value.GetProperty("projectEdits").GetArrayLength().ShouldBe(0);
            stderr.ToString().ShouldBeEmpty();
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            workspace.Dispose();
        }
    }

    [Fact]
    public async Task apply_submits_one_prepared_transaction_after_validation()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", "namespace Legacy; public sealed class Service { }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);
        var writer = new RecordingWriter();
        var operation = new MoveSliceOperation(
            new FixedSessionFactory(workspace, solution),
            new MoveSliceService(),
            new PreparedChangeSetExecutor(writer));

        var execution = (await operation.ExecuteAsync(new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Target",
            ToNamespace: "App.Target.Feature",
            DryRun: false))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        writer.ApplyCount.ShouldBe(1);
        writer.ChangeSet.ShouldNotBeNull().MovedFiles.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task apply_rolls_back_the_source_rewrite_when_the_file_move_fails()
    {
        const string sourceText = "namespace Legacy; public sealed class Service { }";
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", sourceText);
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);
        var sourcePath = Path.Combine(fixture.Directory, "Source", "Service.cs");
        var destinationPath = Path.Combine(
            fixture.Directory,
            "Target",
            "Feature",
            "Service.cs");
        var writer = new PreparedChangeSetWriter(new FaultingMoveFileSystem(
            new PhysicalPreparedFileSystem(),
            sourcePath,
            destinationPath));
        var operation = new MoveSliceOperation(
            new FixedSessionFactory(workspace, solution),
            new MoveSliceService(),
            new PreparedChangeSetExecutor(writer));

        await Should.ThrowAsync<IOException>(() => operation.ExecuteAsync(new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Target",
            ToNamespace: "App.Target.Feature",
            DryRun: false)));

        File.ReadAllText(sourcePath).ShouldBe(sourceText);
        File.Exists(destinationPath).ShouldBeFalse();
    }

    [Fact]
    public async Task apply_fails_closed_before_the_writer_when_project_edits_are_not_authorized()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Service.cs", "namespace Legacy; public sealed class Service(Policy policy) { }")
            .AddSource("Policy.cs", "namespace Legacy; public sealed class Policy { }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (workspace, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);
        var writer = new RecordingWriter();
        var operation = new MoveSliceOperation(
            new FixedSessionFactory(workspace, solution),
            new MoveSliceService(),
            new PreparedChangeSetExecutor(writer));

        var result = await operation.ExecuteAsync(new MoveSliceRequest(
            ["Legacy.Service"],
            Namespace: null,
            ToProject: "Target",
            ToNamespace: "App.Target.Feature",
            DryRun: false));

        result.Match(_ => false, _ => true).ShouldBeTrue();
        writer.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task reciprocal_project_reference_additions_are_reported_as_a_cycle_without_throwing()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource(
                "Moved.cs",
                "namespace Legacy; public sealed class Moved(Policy policy) { public Policy Value => policy; }")
            .AddSource("Policy.cs", "namespace Legacy; public sealed class Policy { }")
            .AddSource("Consumer.cs", "namespace Legacy; public sealed class Consumer(Moved value) { }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        var (_, solution) = fixture.Build();
        WriteProjectFiles(solution);
        using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

        var preparation = (await new MoveSliceService().PrepareAsync(
            solution,
            new MoveSliceIntent(
                MoveSliceSelectionKind.Types,
                ["Legacy.Moved"],
                SourceNamespace: null,
                TargetProject: "Target",
                TargetNamespace: "App.Target.Feature",
                AnalyzeDependencies: true,
                UpdateProjectReferences: true,
                ProjectReferenceJustification: "Keep both reviewed callers compiling after the move.",
                InternalsVisibleToEdits: [],
                InternalsVisibleToJustification: null))).Unwrap();

        preparation.Result.Applicable.ShouldBeFalse();
        preparation.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Code == "project-reference-cycle");
    }

    [Fact]
    public async Task project_file_edits_outside_the_trusted_root_are_rejected_during_planning()
    {
        using var fixture = new AdhocProjectBuilder("Source")
            .AddSource("Moved.cs", "namespace Legacy; public sealed class Moved { }");
        fixture.AddProject("Target").WithDefaultNamespace("App.Target");
        fixture.AddProject("External")
            .ReferenceProject("Source")
            .AddSource("Consumer.cs", "namespace Outside; public sealed class Consumer(Legacy.Moved value) { }");
        var (_, initialSolution) = fixture.Build();
        WriteProjectFiles(initialSolution);
        var externalDirectory = Directory.CreateTempSubdirectory("rt-move-slice-external-");

        try
        {
            var externalProject = initialSolution.Projects.Single(project => project.Name == "External");
            var externalProjectPath = Path.Combine(externalDirectory.FullName, "External.csproj");
            File.WriteAllText(externalProjectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var solution = initialSolution.WithProjectFilePath(externalProject.Id, externalProjectPath);
            using var trust = RepositoryTrustContext.UseExplicitTrust(fixture.Directory);

            var result = await new MoveSliceService().PrepareAsync(
                solution,
                new MoveSliceIntent(
                    MoveSliceSelectionKind.Types,
                    ["Legacy.Moved"],
                    SourceNamespace: null,
                    TargetProject: "Target",
                    TargetNamespace: "App.Target.Feature",
                    AnalyzeDependencies: false,
                    UpdateProjectReferences: true,
                    ProjectReferenceJustification: "Retarget the reviewed external consumer.",
                    InternalsVisibleToEdits: [],
                    InternalsVisibleToJustification: null));

            RtError? error = null;
            result.Match(_ => { }, value => error = value);
            error.ShouldNotBeNull();
            error.Message.ShouldContain("outside the trusted repository");
        }
        finally
        {
            externalDirectory.Delete(recursive: true);
        }
    }

    private static AdhocProjectBuilder BuildCaseFindingFixture()
    {
        var fixture = new AdhocProjectBuilder("Infrastructure")
            .WithDefaultNamespace("WageClaims.Infrastructure")
            .AddSource(
                "Models/CaseFindingModels.cs",
                """
                namespace WageClaims.Infrastructure.Cases.Findings;
                public enum CaseFindingAssessmentStatuses { Ready }
                public record CaseFindingLineFact(decimal Amount);
                public record CaseFindingCalculationFact(CaseFindingLineFact Line);
                public record CaseFindingClaimFact(CaseFindingCalculationFact Calculation);
                public record CaseJobFindingSummary(CaseFindingClaimFact Claim);
                public record CaseFindingSummary(CaseJobFindingSummary Job);
                """)
            .AddSource(
                "ICaseFindingRepository.cs",
                "namespace WageClaims.Infrastructure.Cases.Findings; public interface ICaseFindingRepository { }")
            .AddSource(
                "CaseFindingService.cs",
                "namespace WageClaims.Infrastructure.Cases.Findings; public sealed class CaseFindingService(ICaseFindingRepository repository) { }")
            .AddSource(
                "CosmosCaseFindingRepository.cs",
                "namespace WageClaims.Infrastructure.Cases.Findings; internal sealed class CosmosCaseFindingRepository : ICaseFindingRepository { }");
        fixture.AddProject("Application")
            .WithDefaultNamespace("WageClaims.Application")
            .AddSource("Marker.cs", "namespace WageClaims.Application; public sealed class Marker { }");
        fixture.AddProject("Functions")
            .WithDefaultNamespace("WageClaims.Functions")
            .AddSource(
                "CalculateClaim.cs",
                "using WageClaims.Infrastructure.Cases.Findings; namespace WageClaims.Functions; public sealed class CalculateClaim(CaseFindingService service) { }")
            .ReferenceProject("Infrastructure")
            .ReferenceProject("Application");
        return fixture;
    }

    private static AdhocProjectBuilder BuildWageClaimsRuleItemFixture()
    {
        var fixture = new AdhocProjectBuilder("Infrastructure")
            .AddSource(
                "IRuleItemManagementService.cs",
                """
                using System.Threading.Tasks;
                using WageClaims.Application.Profiles;
                using WageClaims.Application.Rules;
                namespace WageClaim.Function.Services;
                public interface IRuleItemManagementService
                {
                    Task<(ProfileHeaderRecord header, RuleVersionFence version)> LoadAsync();
                    Task<RuleItemResult<ProfileHeaderRecord, RuleVersionFence>> LoadResultAsync();
                }
                """)
            .AddSource(
                "RuleItemManagementService.cs",
                """
                using System.Threading.Tasks;
                using WageClaims.Application.Profiles;
                using WageClaims.Application.Rules;
                namespace WageClaim.Function.Services;
                public sealed class RuleItemManagementService : IRuleItemManagementService
                {
                    public Task<(ProfileHeaderRecord header, RuleVersionFence version)> LoadAsync() =>
                        Task.FromResult((new ProfileHeaderRecord(), new RuleVersionFence()));

                    public async Task<ProfileHeaderRecord> LoadHeaderAsync()
                    {
                        var result = await LoadAsync();
                        return result.header;
                    }

                    public Task<RuleItemResult<ProfileHeaderRecord, RuleVersionFence>> LoadResultAsync() =>
                        Task.FromResult(new RuleItemResult<ProfileHeaderRecord, RuleVersionFence>(
                            new ProfileHeaderRecord(),
                            new RuleVersionFence()));

                    public async Task<ProfileHeaderRecord> LoadResultHeaderAsync()
                    {
                        var result = await LoadResultAsync();
                        return result.Header;
                    }
                }
                """);
        fixture.AddProject("Application")
            .WithDefaultNamespace("WageClaims.Application")
            .AddSource(
                "Profiles/ProfileHeaderRecord.cs",
                "namespace WageClaims.Application.Profiles; public sealed record ProfileHeaderRecord;")
            .AddSource(
                "Rules/RuleVersionFence.cs",
                "namespace WageClaims.Application.Rules; public sealed record RuleVersionFence;")
            .AddSource(
                "Rules/RuleItemResult.cs",
                "namespace WageClaims.Application.Rules; public sealed record RuleItemResult<THeader, TVersion>(THeader Header, TVersion Version);");
        fixture.AddProject("Infrastructure").ReferenceProject("Application");
        return fixture;
    }

    private static void WriteProjectFiles(Solution solution)
    {
        foreach (var project in solution.Projects)
            File.WriteAllText(project.FilePath!, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
    }

    private sealed class RecordingWriter : IPreparedChangeSetWriter
    {
        internal int ApplyCount { get; private set; }

        internal PreparedChangeSet? ChangeSet { get; private set; }

        public Task ApplyAsync(
            PreparedChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            ChangeSet = changeSet;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedSessionFactory(Workspace workspace, Solution solution)
        : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }

    private sealed class FaultingMoveFileSystem(
        IPreparedFileSystem inner,
        string failedSource,
        string failedDestination) : IPreparedFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public bool DirectoryIsEmpty(string path) => inner.DirectoryIsEmpty(path);

        public IEnumerable<string> EnumerateFiles(string path) => inner.EnumerateFiles(path);

        public void CreateDirectory(string path) => inner.CreateDirectory(path);

        public void DeleteDirectory(string path) => inner.DeleteDirectory(path);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
            inner.CopyFile(sourcePath, destinationPath, overwrite);

        public void MoveFile(string sourcePath, string destinationPath, bool overwrite)
        {
            if (PathEquals(sourcePath, failedSource)
                && PathEquals(destinationPath, failedDestination))
                throw new IOException("Injected move failure.");
            inner.MoveFile(sourcePath, destinationPath, overwrite);
        }

        public void DeleteFile(string path) => inner.DeleteFile(path);

        public string ReadAllText(string path) => inner.ReadAllText(path);

        public Task WriteAllTextAsync(
            string path,
            string content,
            CancellationToken cancellationToken) =>
            inner.WriteAllTextAsync(path, content, cancellationToken);

        private static bool PathEquals(string left, string right) => string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }
}
