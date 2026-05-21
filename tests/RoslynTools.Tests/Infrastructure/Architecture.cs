using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     Architecture validation tests using rt analysis APIs directly (no subprocess).
/// </summary>
public class Architecture : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public Architecture(RtServicesFixture services) => _services = services;

    static Architecture()
    {
        MsBuildFixture.Ensure();
    }

    [Fact(DisplayName = "Solution must not have circular namespace dependencies")]
    public async Task solution_must_not_have_circular_namespace_dependencies()
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(FindSolutionPath());
        using (workspace)
        {
            var cycles = await _services.Resolve<CircularDepsCommand>().AnalyzeCyclesAsync(solution, null);

            Assert.True(
                cycles.Count == 0,
                "Circular namespace dependencies detected:\n" +
                string.Join("\n", cycles.Select(c => "  " + string.Join(" -> ", c))));
        }
    }

    private static string FindSolutionPath()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "RoslynTools.slnx")))
                return Path.Combine(dir.FullName, "RoslynTools.slnx");

        throw new InvalidOperationException("Could not locate RoslynTools.slnx.");
    }
}
