using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     The rest of the suite runs on AdhocWorkspace (no MSBuild). This file keeps the
///     real MSBuildWorkspace path covered end-to-end so a regression in
///     <see cref="WorkspaceLoader" /> itself won't slip through silently.
///     Lives outside any xunit Collection so it runs in parallel with the rest, but
///     the BuildHost-netcore subprocess startup (~1–2s) is paid here rather than
///     by every test.
/// </summary>
public class WorkspaceLoaderSmoke
{
    static WorkspaceLoaderSmoke()
    {
        MsBuildFixture.Ensure();
    }

    [Fact]
    public async Task opens_the_solution_and_finds_a_known_type()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution(
            "",
            "Smoke",
            "SmokeProject",
            """
            namespace SmokeProject;

            public class SmokeType
            {
            }
            """);

        var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
        using (workspace)
        {
            solution.Projects.ShouldNotBeEmpty();

            var sym = await Core.SymbolLocator.FindSymbolByFqnAsync(
                solution, "SmokeProject.SmokeType", "type");
            sym.ShouldNotBeNull();
        }
    }

    [Fact]
    public async Task successful_load_does_not_emit_progress_banner_to_diagnostic_sink()
    {
        using var project = new WorkspaceLoaderTestProject();
        var sln = project.AddSolution(
            "",
            "Smoke",
            "SmokeProject");
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        try
        {
            var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
            using (workspace)
            {
                solution.Projects.ShouldNotBeEmpty();
            }

            var output = sink.ToString();
            output.ShouldNotContain("Loading solution", Case.Sensitive);
            output.ShouldNotContain("Loaded ", Case.Sensitive);

            // If any diagnostics surfaced from the real load, they must live under the
            // delimited warnings section, not as raw interleaved lines.
            if (output.Length > 0)
            {
                output.ShouldStartWith("---", Case.Sensitive);
                output.ShouldContain("[workspace-warnings]", Case.Sensitive);
            }
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
        }
    }

    [Fact]
    public async Task handles_missing_solution_file_throws_without_touching_diagnostic_sink()
    {
        using var sink = new StringWriter();
        WorkspaceLoader.DiagnosticSinkOverride.Value = sink;
        try
        {
            var missing = Path.Combine(Path.GetTempPath(), $"rt-nonexistent-{Guid.NewGuid():N}.sln");

            await Assert.ThrowsAsync<FileNotFoundException>(() => WorkspaceLoader.LoadAsync(missing));

            sink.ToString().ShouldBe(string.Empty);
        }
        finally
        {
            WorkspaceLoader.DiagnosticSinkOverride.Value = null;
        }
    }
}
