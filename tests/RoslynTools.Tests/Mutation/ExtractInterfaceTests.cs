using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ExtractInterfaceTests
{
    [Fact]
    public async Task Creates_interface_file()
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
        {
            await ExtractInterfaceCommand.ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                false);
        }

        Assert.True(project.SourceExists("IAgentService.cs"),
            "IAgentService.cs should have been created alongside AgentService.cs");
    }

    [Fact]
    public async Task Includes_only_public_members()
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
        {
            await ExtractInterfaceCommand.ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                false);
        }

        var content = await project.ReadSourceAsync("IAgentService.cs");
        Assert.Contains("GetAgent", content);
        Assert.Contains("DeleteAgent", content);
        Assert.DoesNotContain("Helper", content);
    }

    [Fact]
    public async Task Updates_the_class_to_implement_the_interface()
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
        {
            await ExtractInterfaceCommand.ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains(": IAgentService", content);
    }

    [Fact]
    public async Task Does_not_write_the_interface_file()
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
        {
            await ExtractInterfaceCommand.ExecuteAsync(
                "AgentService",
                "ignored",
                null,
                null,
                true);
        }

        Assert.False(project.SourceExists("IAgentService.cs"),
            "IAgentService.cs should NOT be written during dry-run");
    }
}
