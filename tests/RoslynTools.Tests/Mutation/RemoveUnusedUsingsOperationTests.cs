using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class RemoveUnusedUsingsOperationTests
{
    [Fact]
    public async Task dry_run_removes_multiple_unused_usings_across_the_solution_without_writing()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("First.cs", """
            using System;
            using System.Text;

            internal sealed class First
            {
                internal StringBuilder Create() => new();
            }
            """);
        project.AddSource("Second.cs", """
            using System.Collections.Generic;
            using System.Linq;

            internal sealed class Second
            {
                internal List<int> Create() => [];
            }
            """);
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest(
            Solution: "requested.slnx",
            DryRun: true));
        var execution = result.Unwrap();

        operation.Definition.Name.ShouldBe("remove_unused_usings");
        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.Value!.RemovedCount.ShouldBe(2);
        execution.Value.RemovedUsings.Select(entry => entry.Directive)
            .ShouldBe(["using System;", "using System.Linq;"], ignoreOrder: true);
        execution.ChangedFiles.Count.ShouldBe(2);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task file_scope_leaves_unused_usings_in_other_files_untouched()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("First.cs", "using System;\ninternal sealed class First { }");
        project.AddSource("Second.cs", "using System.Text;\ninternal sealed class Second { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest(
            Path.Combine(project.ProjectDir, "First.cs"),
            DryRun: true));
        var execution = result.Unwrap();

        execution.Value!.RemovedUsings.ShouldHaveSingleItem()
            .Directive.ShouldBe("using System;");
        execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("First.cs");
    }

    [Fact]
    public async Task apply_crosses_the_writer_boundary_once_with_comment_trivia_preserved()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", """
            // explanation retained
            using System; // inline note retained
            using System.Text;

            internal sealed class Imports
            {
                internal StringBuilder Create() => new();
            }
            """);
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest(
            Solution: "requested.slnx",
            DryRun: false));
        var execution = result.Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        writer.NewSolution.ShouldNotBeNull();
        writer.DryRun.ShouldBe(false);
        var changedDocument = writer.NewSolution!.Projects.Single().Documents.Single();
        var changedText = (await changedDocument.GetTextAsync()).ToString();
        changedText.ShouldNotContain("using System;");
        changedText.ShouldContain("// explanation retained");
        changedText.ShouldContain("// inline note retained");
        changedText.ShouldContain("using System.Text;");
    }

    [Fact]
    public async Task used_alias_and_static_usings_are_preserved()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", """
            using Text = System.String;
            using static System.Math;

            internal sealed class Imports
            {
                internal Text Format(double value) => Abs(value).ToString();
            }
            """);
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest());
        var execution = result.Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.RemovedCount.ShouldBe(0);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task global_using_consumed_by_another_file_is_preserved()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("GlobalUsings.cs", "global using System.Text;");
        project.AddSource(
            "Service.cs",
            "internal sealed class Service { internal StringBuilder Create() => new(); }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest());
        var execution = result.Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.RemovedCount.ShouldBe(0);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task cancellation_before_execution_never_reaches_the_writer()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", "using System;\ninternal sealed class Imports { }");
        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            operation.ExecuteAsync(
                new RemoveUnusedUsingsRequest(DryRun: false),
                cancellation.Token));

        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task cli_accepts_a_positional_file_and_defaults_to_dry_run()
    {
        const string sourceText = "using System;\ninternal sealed class Imports { }";
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", sourceText);
        var (workspace, solution) = project.Build();
        var filePath = Path.Combine(project.ProjectDir, "Imports.cs");
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
                    "remove-unused-usings",
                    filePath,
                    "--solution", project.SolutionPath,
                    "--json",
                    "--trust-repository"
                ],
                provider);

            exitCode.ShouldBe(0, stderr.ToString());
            using var payload = JsonDocument.Parse(stdout.ToString());
            var execution = payload.RootElement.GetProperty("data");
            execution.GetProperty("status").GetInt32()
                .ShouldBe((int)MutationExecutionStatus.DryRun);
            execution.GetProperty("value").GetProperty("removedCount").GetInt32().ShouldBe(1);
            File.ReadAllText(filePath).ShouldBe(sourceText);
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
    public async Task explicitly_targeted_rtignored_file_is_not_changed()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Ignored.cs", "using System;\ninternal sealed class Ignored { }");
        var (workspace, solution) = project.Build();
        await File.WriteAllTextAsync(
            Path.Combine(project.ProjectDir, ".rtignore"),
            "remove-unused-usings:*Ignored.cs");
        var writer = new CapturingSolutionChangeWriter();
        var operation = CreateOperation(workspace, solution, writer);

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest(
            Path.Combine(project.ProjectDir, "Ignored.cs"),
            DryRun: false));
        var execution = result.Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
        execution.Value!.RemovedCount.ShouldBe(0);
        writer.NewSolution.ShouldBeNull();
    }

    [Fact]
    public async Task missing_file_returns_a_typed_not_found_error()
    {
        using var project = new AdhocProjectBuilder("ImportsApp");
        project.AddSource("Imports.cs", "internal sealed class Imports { }");
        var (workspace, solution) = project.Build();
        var operation = CreateOperation(workspace, solution, new CapturingSolutionChangeWriter());

        var result = await operation.ExecuteAsync(new RemoveUnusedUsingsRequest(
            Path.Combine(project.ProjectDir, "Missing.cs")));

        result.IsErr.ShouldBeTrue();
        var error = result.Match(_ => null!, failure => failure);
        error.Kind.ShouldBe(RtErrorKind.NotFound);
        error.Context!["file"].ShouldEndWith("Missing.cs");
    }

    private static RemoveUnusedUsingsOperation CreateOperation(
        Workspace workspace,
        Solution solution,
        CapturingSolutionChangeWriter writer)
    {
        return new RemoveUnusedUsingsOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new RemoveUnusedUsingsService(),
            writer);
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
