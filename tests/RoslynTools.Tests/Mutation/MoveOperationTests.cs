using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class MoveOperationTests
{
    [Fact]
    public void classifier_rejects_namespace_to_project_and_mixed_sources()
    {
        var namespaceToProject = MoveRequestClassifier.Classify(new MoveRequest(
            MoveMode.NamespaceToNamespace,
            Namespace: "Old.Models",
            ToProject: "Target"));
        var mixedSources = MoveRequestClassifier.Classify(new MoveRequest(
            MoveMode.NamespaceToNamespace,
            Type: "Old.Widget",
            Namespace: "Old",
            ToNamespace: "New"));

        GetError(namespaceToProject).Kind.ShouldBe(RtErrorKind.InvalidInput);
        GetError(mixedSources).Message.ShouldContain("forbids type", Case.Insensitive);
    }

    [Fact]
    public async Task same_project_with_a_new_namespace_normalizes_to_type_to_namespace()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Widget.cs", "namespace App.Old; public class Widget { }");
        var (_, solution) = project.Build();

        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToProjectMove("App.Old.Widget", "App", "App.New"))).Unwrap();

        preparation.Result.Mode.ShouldBe(MoveMode.TypeToNamespace);
        preparation.Result.TargetNamespace.ShouldBe("App.New");
        var text = await preparation.Solution.Projects.Single().Documents.Single().GetTextAsync();
        text.ToString().ShouldContain("namespace App.New;", Case.Sensitive);
    }

    [Fact]
    public async Task identical_source_and_target_namespace_is_a_no_op()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Widget.cs", "namespace App.Models; public class Widget { }");
        var (_, solution) = project.Build();

        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("App.Models.Widget", "App.Models", MoveFile: false))).Unwrap();

        preparation.ToChangeSet(solution).HasChanges.ShouldBeFalse();
        preparation.Diagnostics.Single().Message.ShouldContain("identical", Case.Insensitive);
    }

    [Fact]
    public async Task namespace_move_rewrites_a_single_segment_namespace()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Widget.cs", "namespace Models; public class Widget { }")
            .AddSource("Consumer.cs", "using Models; class Consumer { Widget Value = new(); }");
        var (_, solution) = project.Build();

        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new NamespaceToNamespaceMove("Models", "Domain", MoveFiles: false))).Unwrap();

        var texts = await Task.WhenAll(preparation.Solution.Projects.Single().Documents
            .Select(async document => (await document.GetTextAsync()).ToString()));
        texts.ShouldContain(text => text.Contains("namespace Domain;", StringComparison.Ordinal));
        texts.ShouldContain(text => text.Contains("using Domain;", StringComparison.Ordinal));
    }

    [Fact]
    public async Task type_move_reports_types_in_the_same_declaration_but_not_another_namespace_block()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Types.cs", """
                namespace App.Old
                {
                    public class Widget { }
                    public class CoLocated { }
                }
                namespace App.Unrelated
                {
                    public class Other { }
                }
                """);
        var (_, solution) = project.Build();

        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("App.Old.Widget", "App.New", MoveFile: false))).Unwrap();

        preparation.Result.MovedTypes.Select(type => type.FullyQualifiedName)
            .ShouldBe(["App.Old.CoLocated", "App.Old.Widget"], ignoreOrder: true);
        preparation.Result.MovedTypes.ShouldNotContain(type =>
            type.FullyQualifiedName.Contains("Other", StringComparison.Ordinal));
        preparation.Diagnostics.Single().Code.ShouldBe("co-located-types");
    }

    [Fact]
    public async Task dry_run_returns_exact_paths_without_crossing_the_writer_boundary()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Widget.cs", "namespace App.Old; public class Widget { }");
        var (workspace, solution) = project.Build();
        var writer = new RecordingPreparedWriter();
        var operation = new MoveOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new MoveService(),
            new PreparedChangeSetExecutor(writer));

        var execution = (await operation.ExecuteAsync(new MoveRequest(
            MoveMode.TypeToNamespace,
            Type: "App.Old.Widget",
            ToNamespace: "App.New",
            DryRun: true))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.AffectedPaths.ShouldHaveSingleItem();
        execution.AffectedPaths[0].ShouldEndWith("Widget.cs");
        writer.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task cancellation_before_workspace_loading_never_crosses_the_writer_boundary()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Widget.cs", "namespace App.Old; public class Widget { }");
        var (workspace, solution) = project.Build();
        var writer = new RecordingPreparedWriter();
        var operation = new MoveOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new MoveService(),
            new PreparedChangeSetExecutor(writer));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new MoveRequest(
                MoveMode.TypeToNamespace,
                Type: "App.Old.Widget",
                ToNamespace: "App.New"),
            cancellation.Token));

        writer.ApplyCount.ShouldBe(0);
    }

    [Fact]
    public async Task move_file_destination_collision_fails_before_a_change_set_is_returned()
    {
        using var project = new AdhocProjectBuilder("App")
            .AddSource("Old/Widget.cs", "namespace App.Old; public class Widget { }")
            .AddSource("New/Widget.cs", "namespace App.New; public class Existing { }");
        var (_, solution) = project.Build();

        var result = await new MoveService().PrepareAsync(
            solution,
            new TypeToNamespaceMove("App.Old.Widget", "App.New", MoveFile: true));

        GetError(result).Message.ShouldContain("destination", Case.Insensitive);
    }

    [Theory]
    [InlineData("WageClaim.Calculation.Domain")]
    [InlineData("WageClaims.Domain")]
    public async Task namespace_move_uses_the_project_folder_boundary_regardless_of_rename_order(
        string defaultNamespace)
    {
        using var project = new AdhocProjectBuilder("Domain")
            .WithDefaultNamespace(defaultNamespace)
            .AddSource(
                "Allowances/AllowanceRule.cs",
                "namespace WageClaim.Calculation.Domain.Allowances; public class AllowanceRule { }");
        var (_, solution) = project.Build();

        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new NamespaceToNamespaceMove(
                "WageClaim.Calculation.Domain.Allowances",
                "WageClaims.Domain.Calculation.Allowances",
                MoveFiles: true))).Unwrap();

        var fileMove = preparation.Result.FileMoves.ShouldHaveSingleItem();
        fileMove.DestinationPath.ShouldBe(Path.Combine(
            project.ProjectDir,
            "Calculation",
            "Allowances",
            "AllowanceRule.cs"));
    }

    [Fact]
    public async Task cross_project_preparation_updates_explicit_compile_items_and_previews_both_paths()
    {
        using var project = new AdhocProjectBuilder("Source")
            .AddSource("Models/Widget.cs", "namespace App.Models; public class Widget { }");
        project.AddProject("Target");
        var (_, solution) = project.Build();
        var source = solution.Projects.Single(candidate => candidate.Name == "Source");
        var target = solution.Projects.Single(candidate => candidate.Name == "Target");
        await File.WriteAllTextAsync(source.FilePath!, """
            <Project>
              <ItemGroup><Compile Include="Models/Widget.cs" /></ItemGroup>
            </Project>
            """);
        await File.WriteAllTextAsync(target.FilePath!, "<Project />");

        var preparation = (await new MoveService().PrepareAsync(
            solution,
            new TypeToProjectMove("App.Models.Widget", "Target", TargetNamespace: null))).Unwrap();
        var changeSet = preparation.ToChangeSet(solution);

        preparation.ProjectFileEdits.Count.ShouldBe(2);
        preparation.ProjectFileEdits.Single(edit => edit.Path == source.FilePath).Content
            .ShouldNotContain("Compile Include", Case.Sensitive);
        preparation.ProjectFileEdits.Single(edit => edit.Path == target.FilePath).Content
            .ShouldContain("Compile Include=\"Models/Widget.cs\"", Case.Sensitive);
        changeSet.AffectedPaths.ShouldContain(source.FilePath!);
        changeSet.AffectedPaths.ShouldContain(target.FilePath!);
        preparation.Result.FileMoves.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task cross_project_preparation_rejects_a_required_reference_cycle()
    {
        using var project = new AdhocProjectBuilder("Source")
            .AddSource("Widget.cs", "namespace App; public class Widget { }")
            .AddSource("Consumer.cs", "namespace App; public class Consumer { Widget Value = new(); }");
        project.AddProject("Target").ReferenceProject("Source");
        var (_, solution) = project.Build();
        foreach (var candidate in solution.Projects)
            await File.WriteAllTextAsync(candidate.FilePath!, "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var result = await new MoveService().PrepareAsync(
            solution,
            new TypeToProjectMove("App.Widget", "Target", TargetNamespace: null));

        GetError(result).Message.ShouldContain("circular project reference", Case.Insensitive);
    }

    private static RtError GetError<T>(Result<T, RtError> result) => result.Match(
        _ => throw new ShouldAssertException("Expected an error."),
        error => error);

    private sealed class RecordingPreparedWriter : IPreparedChangeSetWriter
    {
        internal int ApplyCount { get; private set; }

        public Task ApplyAsync(
            PreparedChangeSet changeSet,
            CancellationToken cancellationToken = default)
        {
            ApplyCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class FixedWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
