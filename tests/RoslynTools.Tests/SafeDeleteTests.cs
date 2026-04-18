using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
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

    private static async Task<(TestProjectBuilder project, string slnPath)> BuildFixtureAsync()
    {
        var project = new TestProjectBuilder("SafeApp");
        project.AddSource("SafeApp.cs", SafeAppSource);
        var slnPath = await project.BuildAsync();
        return (project, slnPath);
    }

    [Fact]
    public async Task SafeDelete_UnreferencedType_Deleted()
    {
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            await SafeDeleteCommand.ExecuteAsync("Orphan", kind: null, solutionPath: slnPath, dryRun: false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("class Orphan", content);
        }
    }

    [Fact]
    public async Task SafeDelete_ReferencedType_Rejected()
    {
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SafeDeleteCommand.ExecuteAsync("Used", kind: null, solutionPath: slnPath, dryRun: false));

            Assert.Contains("Used", ex.Message);
        }
    }

    [Fact]
    public async Task SafeDelete_UnreferencedMethod_Deleted()
    {
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            await SafeDeleteCommand.ExecuteAsync("Svc.Dead", kind: "method", solutionPath: slnPath, dryRun: false);

            var content = await project.ReadSourceAsync("SafeApp.cs");
            Assert.DoesNotContain("void Dead()", content);
            Assert.Contains("void Live()", content);
        }
    }

    [Fact]
    public async Task SafeDelete_ReferencedMethod_Rejected()
    {
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(
                () => SafeDeleteCommand.ExecuteAsync("Svc.Live", kind: "method", solutionPath: slnPath, dryRun: false));

            Assert.Contains("Live", ex.Message);
        }
    }

    [Fact]
    public async Task SafeDelete_DryRun_NoWrites()
    {
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            var originalContent = await project.ReadSourceAsync("SafeApp.cs");

            // Dry-run on an unreferenced type — should not throw, should not write
            await SafeDeleteCommand.ExecuteAsync("Orphan", kind: null, solutionPath: slnPath, dryRun: true);

            var afterContent = await project.ReadSourceAsync("SafeApp.cs");
            Assert.Equal(originalContent, afterContent);
        }
    }
}
