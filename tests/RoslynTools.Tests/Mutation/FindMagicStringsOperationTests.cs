using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public sealed class FindMagicStringsOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_the_requested_workspace_and_returns_typed_groups()
    {
        using var project = new AdhocProjectBuilder("MagicStringsApp");
        project.AddSource("Messages.cs", """
                                         class Messages
                                         {
                                             string First() => "repeat-value";
                                             string Second() => "repeat-value";
                                         }
                                         """);
        var (workspace, solution) = project.Build();
        var sessionFactory = new CapturingWorkspaceSessionFactory(workspace, solution);
        var operation = new FindMagicStringsOperation(sessionFactory, new MagicStringService());

        var result = await operation.ExecuteAsync(new FindMagicStringsRequest(
            MinLength: 5,
            MinOccurrences: 2,
            Solution: "requested.slnx"));

        operation.CommandName.ShouldBe("find_magic_strings");
        sessionFactory.RequestedSolution.ShouldBe("requested.slnx");
        result.SolutionPath.ShouldBe(solution.FilePath);
        var group = result.Groups.ShouldHaveSingleItem();
        group.Value.ShouldBe("repeat-value");
        group.Count.ShouldBe(2);
        group.Occurrences.Select(occurrence => occurrence.MemberName)
            .ShouldBe(["First", "Second"]);
    }

    [Fact]
    public async Task ExecuteAsync_applies_the_requested_thresholds()
    {
        using var project = new AdhocProjectBuilder("MagicStringsApp");
        project.AddSource("Messages.cs", """
                                         class Messages
                                         {
                                             string First() => "repeat-value";
                                             string Second() => "repeat-value";
                                             string Third() => "other-value";
                                         }
                                         """);
        var (workspace, solution) = project.Build();
        var operation = new FindMagicStringsOperation(
            new CapturingWorkspaceSessionFactory(workspace, solution),
            new MagicStringService());

        var result = await operation.ExecuteAsync(new FindMagicStringsRequest(
            MinLength: 5,
            MinOccurrences: 3));

        result.Groups.ShouldBeEmpty();
    }

    private sealed class CapturingWorkspaceSessionFactory(
        Workspace workspace,
        Solution solution) : IWorkspaceSessionFactory
    {
        public string? RequestedSolution { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult(solution);
        }

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            RequestedSolution = solutionPath;
            return Task.FromResult((workspace, solution));
        }
    }
}
