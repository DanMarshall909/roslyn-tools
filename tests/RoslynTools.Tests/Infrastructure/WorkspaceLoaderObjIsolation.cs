using System.Globalization;

using Microsoft.CodeAnalysis.MSBuild;

using RoslynTools.Core;

using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

/// <summary>
///     AC4 of US-075: parallel <c>rt analyze …</c> invocations must not race on
///     shared <c>obj/Debug</c> files. When <c>RT_ISOLATED_OBJ=1</c> is set,
///     <see cref="WorkspaceLoader" /> redirects <c>BaseIntermediateOutputPath</c>
///     to a per-process temp directory so concurrent agents don't trip each
///     other's file locks.
/// </summary>
public class WorkspaceLoaderObjIsolation
{
    static WorkspaceLoaderObjIsolation()
    {
        MsBuildFixture.Ensure();
    }

    private const string EnvVar = "RT_ISOLATED_OBJ";

    [Fact]
    public void isolation_disabled_by_default_so_obj_cache_is_shared()
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, null);
        try
        {
            WorkspaceLoader.ResolveIsolatedObjPath().ShouldBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }

    [Fact]
    public void isolation_enabled_yields_per_process_path_under_temp()
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, "1");
        try
        {
            var path = WorkspaceLoader.ResolveIsolatedObjPath();
            path.ShouldNotBeNull();
            path!.ShouldStartWith(Path.Combine(Path.GetTempPath(), "rt-obj"), Case.Insensitive);
            path.ShouldContain(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            // MSBuild requires the property to end with a directory separator.
            path[^1].ShouldBeOneOf(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }

    [Fact]
    public async Task workspace_load_propagates_isolated_obj_path_into_msbuild_properties()
    {
        var previous = Environment.GetEnvironmentVariable(EnvVar);
        Environment.SetEnvironmentVariable(EnvVar, "1");
        try
        {
            using var project = new WorkspaceLoaderTestProject();
            var sln = project.AddSolution("", "ObjIso", "ObjIsoProject");

            var (workspace, solution) = await WorkspaceLoader.LoadAsync(sln.SolutionPath);
            using (workspace)
            {
                solution.Projects.ShouldNotBeEmpty();

                var msbuild = workspace.ShouldBeOfType<MSBuildWorkspace>();
                msbuild.Properties.ShouldContainKey("BaseIntermediateOutputPath");
                var configured = msbuild.Properties["BaseIntermediateOutputPath"];
                configured.ShouldStartWith(Path.Combine(Path.GetTempPath(), "rt-obj"), Case.Insensitive);
                configured.ShouldContain(Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(EnvVar, previous);
        }
    }
}
