using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests;

public class ExtractInterfaceTests
{
    [Fact]
    public async Task ExtractInterface_CreatesInterfaceFile()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
            namespace MyApp.Services;

            public class AgentService
            {
                public string GetAgent(int id) => id.ToString();
                public void DeleteAgent(int id) { }
                private void Helper() { }
            }
            """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractInterfaceCommand.ExecuteAsync(
            typeName: "AgentService",
            solutionPath: "ignored",
            outputDir: null,
            ns: null,
            dryRun: false);

        Assert.True(project.SourceExists("IAgentService.cs"),
            "IAgentService.cs should have been created alongside AgentService.cs");
    }

    [Fact]
    public async Task ExtractInterface_InterfaceContainsPublicMembersOnly()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
            namespace MyApp.Services;

            public class AgentService
            {
                public string GetAgent(int id) => id.ToString();
                public void DeleteAgent(int id) { }
                private void Helper() { }
            }
            """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractInterfaceCommand.ExecuteAsync(
            typeName: "AgentService",
            solutionPath: "ignored",
            outputDir: null,
            ns: null,
            dryRun: false);

        var content = await project.ReadSourceAsync("IAgentService.cs");
        Assert.Contains("GetAgent", content);
        Assert.Contains("DeleteAgent", content);
        Assert.DoesNotContain("Helper", content);
    }

    [Fact]
    public async Task ExtractInterface_ClassUpdatedToImplementInterface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
            namespace MyApp.Services;

            public class AgentService
            {
                public string GetAgent(int id) => id.ToString();
                public void DeleteAgent(int id) { }
                private void Helper() { }
            }
            """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractInterfaceCommand.ExecuteAsync(
            typeName: "AgentService",
            solutionPath: "ignored",
            outputDir: null,
            ns: null,
            dryRun: false);

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains(": IAgentService", content);
    }

    [Fact]
    public async Task ExtractInterface_DryRun_NoFileWritten()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", """
            namespace MyApp.Services;

            public class AgentService
            {
                public string GetAgent(int id) => id.ToString();
                public void DeleteAgent(int id) { }
                private void Helper() { }
            }
            """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await ExtractInterfaceCommand.ExecuteAsync(
            typeName: "AgentService",
            solutionPath: "ignored",
            outputDir: null,
            ns: null,
            dryRun: true);

        Assert.False(project.SourceExists("IAgentService.cs"),
            "IAgentService.cs should NOT be written during dry-run");
    }
}
