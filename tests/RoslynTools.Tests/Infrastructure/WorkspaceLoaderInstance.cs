using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class WorkspaceLoaderInstance
{
    [Fact]
    public async Task gets_solution_async_with_override_returns_override_solution()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "namespace T; public class A {}");
        var (workspace, sln) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            using var loader = new WorkspaceLoader();
            var result = await loader.GetSolutionAsync(null);
            Assert.Same(sln, result);
        }
    }

    [Fact]
    public async Task gets_solution_async_override_takes_precedence_over_solution_path()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "namespace T; public class A {}");
        var (workspace, sln) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            using var loader = new WorkspaceLoader();
            // Passing a non-existent path — override should prevent any file lookup.
            var result = await loader.GetSolutionAsync("/does/not/exist.sln");
            Assert.Same(sln, result);
        }
    }

    [Fact]
    public async Task gets_solution_async_multiple_calls_return_same_solution_from_override()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", "namespace T; public class A {}");
        var (workspace, sln) = project.Build();

        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            using var loader = new WorkspaceLoader();
            var first = await loader.GetSolutionAsync(null);
            var second = await loader.GetSolutionAsync(null);
            Assert.Same(first, second);
        }
    }

    [Fact]
    public void dispose_does_not_throw_when_no_solution_loaded()
    {
        var loader = new WorkspaceLoader();
        loader.Dispose(); // must not throw
    }
}
