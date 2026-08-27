using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindPatternOperationTests
{
    [Fact]
    public async Task ExecuteAsync_loads_the_requested_solution_and_returns_typed_matches()
    {
        using var project = new AdhocProjectBuilder("PatternApp");
        project.AddSource("Source.cs", """
            namespace PatternApp;
            public class Source
            {
                public void Run()
                {
                    int first = 1;
                    System.Console.WriteLine(first);
                }
            }
            """);
        project.AddSource("Match.cs", """
            namespace PatternApp;
            public class Match
            {
                public void Run()
                {
                    int second = 2;
                    System.Console.WriteLine(second);
                }
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var factory = new CapturingSessionFactory(solution);
            var operation = new FindPatternOperation(factory);

            var result = await operation.ExecuteAsync(new FindPatternRequest(
                Path.Combine(project.ProjectDir, "Source.cs"),
                6,
                7,
                "requested.slnx"));

            operation.Definition.Name.ShouldBe("find_pattern");
            factory.RequestedSolution.ShouldBe("requested.slnx");
            var response = result.Match(
                value => value,
                error => throw new ShouldAssertException(error.Message));
            response.Matches.ShouldHaveSingleItem().File.ShouldEndWith("Match.cs");
        }
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(3, 2)]
    public async Task ExecuteAsync_rejects_invalid_line_ranges_before_loading_the_solution(
        int startLine,
        int endLine)
    {
        var factory = new CapturingSessionFactory(null!);
        var operation = new FindPatternOperation(factory);

        var result = await operation.ExecuteAsync(new FindPatternRequest(
            "Source.cs",
            startLine,
            endLine,
            "requested.slnx"));

        var error = result.Match<RtError?>(_ => null, failure => failure);
        error.ShouldNotBeNull();
        error.Kind.ShouldBe(RtErrorKind.InvalidInput);
        error.Message.ShouldContain("line range");
        factory.RequestedSolution.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_returns_not_found_for_a_file_outside_the_solution()
    {
        using var project = new AdhocProjectBuilder("PatternApp");
        project.AddSource("Source.cs", "namespace PatternApp; public class Source { }");
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var operation = new FindPatternOperation(new CapturingSessionFactory(solution));

            var result = await operation.ExecuteAsync(new FindPatternRequest(
                Path.Combine(project.ProjectDir, "Missing.cs"),
                1,
                1));

            var error = result.Match<RtError?>(_ => null, failure => failure);
            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.NotFound);
            error.Message.ShouldContain("Missing.cs");
        }
    }

    [Fact]
    public async Task ExecuteAsync_honors_cancellation_before_loading_the_solution()
    {
        var factory = new CapturingSessionFactory(null!);
        var operation = new FindPatternOperation(factory);

        await Should.ThrowAsync<OperationCanceledException>(() => operation.ExecuteAsync(
            new FindPatternRequest("Source.cs", 1, 1, "requested.slnx"),
            new CancellationToken(canceled: true)));

        factory.RequestedSolution.ShouldBeNull();
    }

    private sealed class CapturingSessionFactory(Solution solution) : IWorkspaceSessionFactory
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
            return Task.FromResult((solution.Workspace, solution));
        }
    }
}
