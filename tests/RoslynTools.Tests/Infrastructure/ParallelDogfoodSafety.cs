using System.Globalization;

using Microsoft.CodeAnalysis.MSBuild;

using RoslynTools.Core;

using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     AC5 of US-075: an integration test that exercises the parallel-dogfood
///     safety net end-to-end. Covers the reproduction scenarios from:
///     <list type="bullet">
///         <item>
///             <description>
///                 FB-007 — concurrent <c>rt feedback add</c> calls must not
///                 corrupt the store or duplicate ids. Already covered by
///                 <see cref="FeedbackStoreConcurrencyTests" />; cross-link only.
///             </description>
///         </item>
///         <item>
///             <description>
///                 FB-013 — parallel read-only <c>rt analyze …</c> invocations
///                 must not emit MSBuild <c>obj/Debug</c> file-lock warnings.
///                 Verified here by spawning N parallel <see cref="WorkspaceLoader.LoadAsync" />
///                 calls with <c>RT_ISOLATED_OBJ=1</c> set, asserting every load
///                 completes without an <see cref="IOException" /> and that
///                 <c>BaseIntermediateOutputPath</c> is redirected onto the
///                 per-process temp directory.
///             </description>
///         </item>
///         <item>
///             <description>
///                 FB-008 — concurrent wrapper rebuilds racing on <c>bin/obj</c>
///                 outputs. The fix lives in <c>rt.ps1</c> (named mutex,
///                 commit 0ad69f3) and <c>rt.sh</c> (flock, commit 1aa5077) —
///                 these are shell wrappers around <c>dotnet build</c>, not
///                 exercisable from xUnit without spawning shells. Coverage
///                 path: manual smoke + the AC4 obj-isolation work in
///                 commit d8cfa21 which removes the in-process arm of the race.
///             </description>
///         </item>
///     </list>
/// </summary>
public class ParallelDogfoodSafety
{
    static ParallelDogfoodSafety()
    {
        MsBuildFixture.Ensure();
    }

    private const string EnvVar = "RT_ISOLATED_OBJ";

    [Fact]
    public async Task parallel_workspace_loads_under_isolated_obj_complete_without_lock_warnings()
    {
        const int parallelLoadCount = 4;

        using var envScope = new EnvVarScope(EnvVar, "1");
        using var project = new WorkspaceLoaderTestProject();

        // Each parallel task targets its own solution on disk so the test
        // does not depend on the in-process SolutionLoadGate serializing
        // loads of the same solution. This mirrors FB-013: distinct agent
        // shells loading distinct (or shared) solutions concurrently.
        var solutions = Enumerable.Range(0, parallelLoadCount)
            .Select(i => project.AddSolution(
                relativeDirectory: $"sln-{i}",
                solutionName: $"Parallel{i}",
                projectName: $"ParallelProject{i}"))
            .ToArray();

        var stderrBuffers = new System.Text.StringBuilder[parallelLoadCount];
        for (var i = 0; i < parallelLoadCount; i++)
            stderrBuffers[i] = new System.Text.StringBuilder();

        var tasks = solutions
            .Select((sln, idx) => Task.Run(async () =>
            {
                var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
                using (workspace)
                {
                    solution.Projects.ShouldNotBeEmpty();

                    var msbuild = workspace.ShouldBeOfType<MSBuildWorkspace>();
                    var configured = msbuild.Properties["BaseIntermediateOutputPath"];

                    // Capture any workspace load diagnostics as "stderr-equivalent"
                    // — MSBuild surfaces obj lock contention as WorkspaceDiagnostic
                    // entries when it can't read or write obj/Debug files.
                    foreach (var diag in msbuild.Diagnostics)
                        stderrBuffers[idx].AppendLine(diag.Message);

                    return configured;
                }
            }))
            .ToArray();

        // Assert: every load completes without throwing (IOException would
        // surface here if obj/Debug locks raced).
        var configuredPaths = await Task.WhenAll(tasks);

        // Assert: every load was redirected to the isolated obj path.
        var isolatedRoot = Path.Combine(Path.GetTempPath(), "rt-obj");
        var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
        foreach (var path in configuredPaths)
        {
            path.ShouldStartWith(isolatedRoot, Case.Insensitive);
            path.ShouldContain(pid);
        }

        // Assert: no diagnostic message mentions an obj/Debug file lock. The
        // canonical wording from MSBuild's file-IO layer is "being used by
        // another process" — that's the signal FB-013 was capturing.
        var combinedStderr = string.Concat(stderrBuffers.Select(b => b.ToString()));
        combinedStderr.ShouldNotContain(
            "being used by another process",
            Case.Insensitive,
            customMessage: "MSBuild reported a file lock during parallel workspace load — FB-013 regression.");
        combinedStderr.ShouldNotContain(
            "could not access",
            Case.Insensitive,
            customMessage: "MSBuild reported an obj/Debug access failure during parallel workspace load.");
    }

    /// <summary>
    ///     Sets an environment variable on entry and restores the previous
    ///     value on disposal. xUnit does not isolate environment variables
    ///     across tests, so every test that mutates one must restore it in
    ///     a <see langword="finally" />-equivalent — this scope makes that
    ///     mechanical.
    /// </summary>
    private sealed class EnvVarScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _previous;

        public EnvVarScope(string name, string? value)
        {
            _name = name;
            _previous = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _previous);
        }
    }
}
