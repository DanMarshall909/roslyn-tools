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
}
