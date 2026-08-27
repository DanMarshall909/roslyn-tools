using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Refactor;

/// <summary>
/// US-140: annotate a composition root so a reflectively-registered type gains a real
/// typeof reference. The annotation is only worth anything if the file still compiles —
/// dogfooding rt on its own Bootstrap.cs produced "usingSystem.Diagnostics.CodeAnalysis;"
/// (CS8956 + CS0246) and CRLF endings in an LF repo.
/// </summary>
public class AddDynamicDependency
{
    private const string CompositionRoot = """
                                           using System;

                                           namespace App;

                                           public sealed class ServiceCollection { }

                                           public sealed class ReflectiveService { }

                                           public static class Bootstrap
                                           {
                                               public static void RegisterServices(ServiceCollection services)
                                               {
                                               }
                                           }
                                           """;

    private static async Task<string> RunAndReadRootAsync(params string[] typeNames)
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Bootstrap.cs", CompositionRoot);

        var (workspace, solution) = project.Build();
        var writer = new CapturingSolutionChangeWriter();
        var operation = new AddDynamicDependencyOperation(
            new FixedWorkspaceSessionFactory(workspace, solution),
            new AddDynamicDependencyService(),
            writer);

        var execution = (await operation.ExecuteAsync(new AddDynamicDependencyRequest(
            typeNames,
            "App.Bootstrap.RegisterServices"))).Unwrap();

        execution.Status.ShouldBe(MutationExecutionStatus.Applied);
        var document = writer.NewSolution!.Projects
            .SelectMany(p => p.Documents)
            .Single(d => d.Name == "Bootstrap.cs");
        return (await document.GetTextAsync()).ToString();
    }

    [Fact]
    public async Task emits_a_using_directive_that_parses()
    {
        var text = await RunAndReadRootAsync("App.ReflectiveService");

        text.ShouldContain("using System.Diagnostics.CodeAnalysis;", Case.Sensitive,
            "without the space after the keyword the file does not compile: 'usingSystem...' parses as a member");
    }

    [Fact]
    public async Task does_not_introduce_carriage_returns_into_an_lf_file()
    {
        var text = await RunAndReadRootAsync("App.ReflectiveService");

        text.ShouldNotContain("\r", Case.Sensitive,
            "the repo is LF-only via .gitattributes; ElasticCarriageReturnLineFeed mixes endings into the file");
    }

    [Fact]
    public async Task emits_the_dynamic_dependency_attribute_for_the_type()
    {
        var text = await RunAndReadRootAsync("App.ReflectiveService");

        text.ShouldContain("DynamicDependency", Case.Sensitive, "the annotation is the whole point of the refactor");
        text.ShouldContain("typeof(global::App.ReflectiveService)", Case.Sensitive,
            "a typeof reference is what SymbolFinder resolves, which is what makes the type visible again");
    }

    [Fact]
    public async Task separates_attribute_arguments_with_a_space()
    {
        var text = await RunAndReadRootAsync("App.ReflectiveService");

        text.ShouldContain("PublicConstructors, typeof(", Case.Sensitive,
            "argument lists are comma-space separated everywhere else in the codebase");
    }

    [Fact]
    public async Task indents_the_attribute_to_match_the_method_it_annotates()
    {
        var text = await RunAndReadRootAsync("App.ReflectiveService");

        text.ShouldContain("    [DynamicDependency(", Case.Sensitive,
            "an unindented attribute fails dotnet format --verify-no-changes, which the repo gates on");
    }

    [Fact]
    public async Task is_idempotent_for_a_type_already_annotated()
    {
        var text = await RunAndReadRootAsync("App.ReflectiveService", "App.ReflectiveService");

        var occurrences = text.Split("DynamicDependency").Length - 1;
        occurrences.ShouldBe(1, "a repeated --type must not duplicate the annotation");
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
