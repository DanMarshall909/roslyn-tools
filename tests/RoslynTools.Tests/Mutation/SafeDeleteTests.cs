using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class SafeDeleteTests
{
    private const string SafeAppSource = """
        public class Orphan { }

        public class Used { public void Go() { } }

        public class Caller { void Run() { var x = new Used(); } }

        public class Svc
        {
            public void Dead() { }
            public void Live() { }
            void CallLive() { Live(); }
        }
        """;

    private static (AdhocProjectBuilder Project, Workspace Workspace, Solution Solution) BuildFixture()
    {
        var project = new AdhocProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", SafeAppSource);
        var (workspace, sln) = project.Build();
        return (project, workspace, sln);
    }

    [Fact]
    public async Task Deletes_the_type()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await SafeDeleteCommand.ExecuteAsync("Orphan", kind: null, solutionPath: "ignored", dryRun: false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("class Orphan", content);
        }
    }

    [Fact]
    public async Task Throws_an_error_for_a_referenced_type()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SafeDeleteCommand.ExecuteAsync("Used", kind: null, solutionPath: "ignored", dryRun: false));

            Assert.Contains("Used", ex.Message);
        }
    }

    [Fact]
    public async Task Deletes_the_method()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await SafeDeleteCommand.ExecuteAsync("Svc.Dead", kind: "method", solutionPath: "ignored", dryRun: false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("void Dead()", content);
            Assert.Contains("void Live()", content);
        }
    }

    [Fact]
    public async Task Throws_an_error_for_a_referenced_method()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SafeDeleteCommand.ExecuteAsync("Svc.Live", kind: "method", solutionPath: "ignored", dryRun: false));

            Assert.Contains("Live", ex.Message);
        }
    }

    [Fact]
    public async Task Does_not_modify_files()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var originalContent = await project.ReadSourceAsync("SafeApp.cs");

            await SafeDeleteCommand.ExecuteAsync("Orphan", kind: null, solutionPath: "ignored", dryRun: true);

            var afterContent = await project.ReadSourceAsync("SafeApp.cs");
            Assert.Equal(originalContent, afterContent);
        }
    }
}
