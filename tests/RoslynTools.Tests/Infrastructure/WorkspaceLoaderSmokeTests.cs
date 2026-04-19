using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
/// The rest of the suite runs on AdhocWorkspace (no MSBuild). This file keeps the
/// real MSBuildWorkspace path covered end-to-end so a regression in
/// <see cref="WorkspaceLoader"/> itself won't slip through silently.
///
/// Lives outside any xunit Collection so it runs in parallel with the rest, but
/// the BuildHost-netcore subprocess startup (~1–2s) is paid here rather than
/// by every test.
/// </summary>
public class WorkspaceLoaderSmokeTests
{
    static WorkspaceLoaderSmokeTests() => MsBuildFixture.Ensure();

    [Fact]
    public async Task Opens_the_solution_and_finds_a_known_type()
    {
        var slnPath = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(WorkspaceLoaderSmokeTests).Assembly.Location)!,
            "..", "..", "..", "..", "..", "..", "RoslynTools.slnx"));

        Assert.True(File.Exists(slnPath), $"Expected slnx at {slnPath}");

        var (workspace, solution) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            Assert.NotEmpty(solution.Projects);

            var sym = await SymbolLocator.FindSymbolByFqnAsync(
                solution, "RoslynTools.Core.WorkspaceLoader", "type");
            Assert.NotNull(sym);
        }
    }
}
