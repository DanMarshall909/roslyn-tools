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

    private static string ResolveSelfSlnx() => Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(WorkspaceLoaderSmokeTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", "..", "RoslynTools.slnx"));

    [Fact]
    public async Task Opens_the_solution_and_finds_a_known_type()
    {
        var slnPath = ResolveSelfSlnx();
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

    [Fact]
    public async Task Successful_load_does_not_emit_progress_banner_to_diagnostic_sink()
    {
        var slnPath = ResolveSelfSlnx();
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        try
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(slnPath);
            using (workspace)
            {
                Assert.NotEmpty(solution.Projects);
            }

            var output = sink.ToString();
            Assert.DoesNotContain("Loading solution", output);
            Assert.DoesNotContain("Loaded ", output);

            // If any diagnostics surfaced from the real load, they must live under the
            // delimited warnings section, not as raw interleaved lines.
            if (output.Length > 0)
            {
                Assert.StartsWith("---", output);
                Assert.Contains("[workspace-warnings]", output);
            }
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
        }
    }

    [Fact]
    public async Task Missing_solution_file_throws_without_touching_diagnostic_sink()
    {
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        try
        {
            var missing = Path.Combine(Path.GetTempPath(), $"rt-nonexistent-{Guid.NewGuid():N}.sln");

            await Assert.ThrowsAsync<FileNotFoundException>(
                () => WorkspaceLoader.LoadAsync(missing));

            Assert.Equal(string.Empty, sink.ToString());
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
        }
    }
}
