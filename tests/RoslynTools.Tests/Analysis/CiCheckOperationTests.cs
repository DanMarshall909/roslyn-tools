using System.Text.Json;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Analysis;

public sealed class CiCheckOperationTests
{
    [Theory]
    [InlineData(1, false, 1)]
    [InlineData(2, true, 0)]
    public async Task Complexity_enforces_strict_maximum_and_reports_all_findings(int maximum, bool passed, int count)
    {
        using var builder = Fixture();
        var (workspace, solution) = builder.Build();
        WritePolicy(builder, ComplexityPolicy(maximum));
        var result = Unwrap(await new CiCheckOperation(new FixedSessions(workspace, solution))
            .ExecuteAsync(new CiCheckRequest(builder.SolutionPath)));

        result.Passed.ShouldBe(passed);
        result.FindingCount.ShouldBe(count);
        result.CheckedProjectCount.ShouldBe(1);
        result.Rules.Single().FindingCount.ShouldBe(count);
        if (!passed)
        {
            result.Findings.Single().File.ShouldBe("Demo/Value.cs");
            result.Findings.Single().Line.ShouldBeGreaterThan(0);
            result.Findings.Single().Message.ShouldContain("exceeds maximum 1");
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"rules\":[]}")]
    [InlineData("{\"version\":1,\"rules\":[]}")]
    [InlineData("{\"version\":1,\"rules\":[null]}")]
    [InlineData("{\"version\":1,\"rules\":[{\"id\":\"typo\"}]}")]
    [InlineData("{\"version\":1,\"rules\":[{\"id\":\"architecture\"}]}")]
    [InlineData("{\"version\":1,\"rules\":[{\"id\":\"maintainability.complexity\",\"maxCyclomatic\":0}]}")]
    [InlineData("{\"version\":1,\"rules\":[{\"id\":\"maintainability.complexity\",\"maxCyclomatic\":10,\"maxCyclomtic\":1}]}")]
    [InlineData("{\"version\":1,\"rules\":[{\"id\":\"maintainability.complexity\",\"maxCyclomatic\":10},{\"id\":\"maintainability.complexity\",\"maxCyclomatic\":20}]}")]
    [InlineData("{")]
    public async Task Invalid_policy_never_passes(string policy)
    {
        using var builder = Fixture();
        var (workspace, solution) = builder.Build();
        WritePolicy(builder, policy);
        var result = await new CiCheckOperation(new FixedSessions(workspace, solution))
            .ExecuteAsync(new CiCheckRequest(builder.SolutionPath));
        result.Match(_ => throw new ShouldAssertException("Invalid policy passed."),
            error => error.Kind.ShouldBe(RtErrorKind.InvalidInput));
    }

    [Theory]
    [InlineData(".rt/missing.json")]
    [InlineData("../outside.json")]
    public async Task Missing_or_escaping_configuration_never_passes(string config)
    {
        using var builder = Fixture();
        var (workspace, solution) = builder.Build();
        var result = await new CiCheckOperation(new FixedSessions(workspace, solution))
            .ExecuteAsync(new CiCheckRequest(builder.SolutionPath, config));
        result.Match(_ => throw new ShouldAssertException("Invalid path passed."),
            error => (error.Kind is RtErrorKind.NotFound or RtErrorKind.InvalidInput).ShouldBeTrue());
    }

    [Fact]
    public async Task Compilation_errors_never_produce_a_passing_gate()
    {
        using var builder = new AdhocProjectBuilder("Broken");
        builder.AddSource("Broken.cs", "public class Broken { Missing Value; }");
        var (workspace, solution) = builder.Build();
        WritePolicy(builder, ComplexityPolicy(10));
        var result = await new CiCheckOperation(new FixedSessions(workspace, solution))
            .ExecuteAsync(new CiCheckRequest(builder.SolutionPath));
        result.Match(_ => throw new ShouldAssertException("Incomplete check passed."),
            error => error.Message.ShouldContain("could not complete"));
    }

    [Fact]
    public async Task Architecture_and_complexity_share_one_gate_and_keep_baselined_violations_failing()
    {
        using var builder = Fixture();
        var (workspace, solution) = builder.Build();
        File.WriteAllText(Path.Combine(builder.ProjectDir, "Demo.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Forbidden.Package\" Version=\"1.0.0\" /></ItemGroup></Project>");
        WritePolicy(builder, """
            {"version":1,"rules":[
              {"id":"architecture","config":".rt/architecture.json"},
              {"id":"maintainability.complexity","maxCyclomatic":1}
            ]}
            """);
        File.WriteAllText(Path.Combine(builder.Directory, ".rt", "architecture.json"), """
            {"version":1,"layers":[
              {"name":"Domain","projects":["Demo"],"allowedProjectReferences":[],"forbiddenPackageReferences":["Forbidden.Package"]}
            ]}
            """);
        var architecture = Unwrap(await ArchitectureCheckService.CheckAsync(
            solution, new ArchitectureCheckRequest(builder.SolutionPath), CancellationToken.None));
        architecture.Violations.ShouldNotBeEmpty();
        File.WriteAllText(Path.Combine(builder.Directory, ".rt", "baseline.json"),
            JsonSerializer.Serialize(new { version = 1, fingerprints = architecture.Violations.Select(item => item.Fingerprint) }));
        var configPath = Path.Combine(builder.Directory, ".rt", "architecture.json");
        File.WriteAllText(configPath, File.ReadAllText(configPath).Replace(
            "\"version\":1", "\"version\":1,\"baselineFile\":\".rt/baseline.json\""));

        var result = Unwrap(await new CiCheckOperation(new FixedSessions(workspace, solution))
            .ExecuteAsync(new CiCheckRequest(builder.SolutionPath)));
        result.Passed.ShouldBeFalse();
        result.Rules.Count.ShouldBe(2);
        result.Findings.ShouldContain(item => item.RuleId.StartsWith("architecture.") && item.Message.Contains("(existing)"));
        result.Findings.ShouldContain(item => item.RuleId == "maintainability.complexity");
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 0)]
    public async Task Cli_ci_group_returns_enforcement_exit_status_and_json(int maximum, int expectedExit)
    {
        using var builder = Fixture();
        var (workspace, solution) = builder.Build();
        WritePolicy(builder, ComplexityPolicy(maximum));
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        {
            var original = Console.Out;
            using var stdout = new StringWriter();
            Console.SetOut(stdout);
            try
            {
                var exit = await App.RunAsync(["ci", "check", "--solution", builder.SolutionPath, "--json"]);
                exit.ShouldBe(expectedExit);
                using var payload = JsonDocument.Parse(stdout.ToString());
                payload.RootElement.GetProperty("passed").GetBoolean().ShouldBe(expectedExit == 0);
            }
            finally
            {
                Console.SetOut(original);
            }
        }
    }

    [Fact]
    public async Task Cancellation_is_propagated()
    {
        using var builder = Fixture();
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            await Should.ThrowAsync<OperationCanceledException>(() =>
                new CiCheckOperation(new FixedSessions(workspace, solution))
                    .ExecuteAsync(new CiCheckRequest(builder.SolutionPath), new CancellationToken(true)));
        }
    }

    [Fact]
    public async Task Empty_workspaces_never_pass()
    {
        using var builder = new AdhocProjectBuilder("Demo");
        var (workspace, solution) = builder.Build();
        WritePolicy(builder, ComplexityPolicy(10));
        var empty = solution.RemoveProject(solution.ProjectIds.Single());
        var result = await new CiCheckOperation(new FixedSessions(workspace, empty))
            .ExecuteAsync(new CiCheckRequest(builder.SolutionPath));
        result.Match(_ => throw new ShouldAssertException("Empty workspace passed."),
            error => error.Message.ShouldContain("at least one loaded C# project"));
    }

    private static AdhocProjectBuilder Fixture() => new AdhocProjectBuilder("Demo")
        .AddSource("Value.cs", "namespace Demo; public class Value { public int Run(bool flag) { if (flag) return 1; return 0; } }");

    private static string ComplexityPolicy(int maximum) =>
        JsonSerializer.Serialize(new { version = 1, rules = new[] { new { id = "maintainability.complexity", maxCyclomatic = maximum } } });

    private static void WritePolicy(AdhocProjectBuilder builder, string policy)
    {
        Directory.CreateDirectory(Path.Combine(builder.Directory, ".rt"));
        File.WriteAllText(Path.Combine(builder.Directory, ".rt", "ci.json"), policy);
    }

    private static T Unwrap<T>(Result<T, RtError> result) =>
        result.Match(value => value, error => throw new ShouldAssertException(error.Message));

    private sealed class FixedSessions(Workspace workspace, Solution solution) : IWorkspaceSessionFactory
    {
        public Task<Solution> GetSolutionAsync(string? solutionPath) => Task.FromResult(solution);
        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath) =>
            Task.FromResult((workspace, solution));
    }
}
