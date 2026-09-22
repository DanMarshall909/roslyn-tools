using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class RepositoryTrustMiddlewareTests
{
    [Fact]
    public async Task doctor_trust_resolves_relative_solution_under_requested_directory()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var requestedDirectory = fixture.CreateDirectory("requested");
        var unrelatedDirectory = fixture.CreateDirectory("unrelated");
        File.WriteAllText(Path.Combine(requestedDirectory, "App.slnx"), "");

        var sessions = new ThrowingSessionFactory();
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        services.AddSingleton<IWorkspaceSessionFactory>(sessions);
        await using var provider = services.BuildServiceProvider();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        int exitCode;
        try
        {
            using var currentDirectory = fixture.UseCurrentDirectory(unrelatedDirectory);
            exitCode = await App.RunAsync(
                [
                    "doctor",
                    "--directory", requestedDirectory,
                    "--solution", "App.slnx",
                    "--trust-repository",
                    "--json"
                ],
                provider);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }

        exitCode.ShouldBe(0, stderr.ToString());
        sessions.LoadCount.ShouldBe(1);
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    [Fact]
    public async Task trust_uses_parent_solution_instead_of_nested_working_repository()
    {
        using var fixture = new WorkspaceLoaderTestProject();
        var solution = fixture.AddSolution("", "App", "App");
        var nestedDirectory = fixture.CreateDirectory(Path.Combine("src", "nested"));
        Directory.CreateDirectory(Path.Combine(nestedDirectory, ".git"));
        var trusted = false;
        var probe = new Command("trust-target-probe");
        probe.AddOption(new Option<string?>("--solution"));
        probe.SetHandler(() =>
        {
            trusted = RepositoryTrustContext.IsExplicitlyTrustedFor(solution.SolutionPath);
        });

        int exitCode;
        using (fixture.UseCurrentDirectory(nestedDirectory))
        {
            exitCode = await App.RunAsync(
                ["trust-target-probe", "--trust-repository"],
                additionalCommands: [probe]);
        }

        exitCode.ShouldBe(0);
        trusted.ShouldBeTrue();
        RepositoryTrustContext.IsExplicitlyTrusted.ShouldBeFalse();
    }

    private sealed class ThrowingSessionFactory : IWorkspaceSessionFactory
    {
        public int LoadCount { get; private set; }

        public Task<Solution> GetSolutionAsync(string? solutionPath) =>
            throw new InvalidOperationException("Unexpected solution-only load.");

        public Task<(Workspace Workspace, Solution Solution)> LoadWorkspaceAsync(string? solutionPath)
        {
            LoadCount++;
            throw new InvalidOperationException("Trusted workspace probe reached the session boundary.");
        }
    }
}
