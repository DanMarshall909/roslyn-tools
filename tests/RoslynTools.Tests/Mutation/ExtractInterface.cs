using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Mutation;

public class ExtractInterface
{
    [Fact]
    public async Task creates_interface_file()
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
    public async Task includes_only_public_members()
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
    public async Task updates_the_class_to_implement_the_interface()
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
    public async Task does_not_write_the_interface_file()
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

    [Fact]
    public async Task rejects_non_class_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Point.cs", """
                                      namespace MyApp;
                                      public struct Point { public int X; public int Y; }
                                      """);
        var (_, sln) = project.Build();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new ExtractInterfaceService(
                NullLogger<ExtractInterfaceService>.Instance,
                new CapturingSolutionChangeWriter())
                .RunAsync(sln, "Point", null, null, false));

        Assert.Contains("must be a class", ex.Message);
    }

    [Fact]
    public async Task appends_to_existing_base_list()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public interface IBase { }
                                       public class Widget : IBase
                                       {
                                           public void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractInterfaceCommand.ExecuteAsync("Widget", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("Widget.cs");
        Assert.Contains("IBase", content);
        Assert.Contains("IWidget", content);
    }

    [Fact]
    public async Task includes_public_properties_in_the_interface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Repo.cs", """
                                     namespace MyApp;
                                     public class Repo
                                     {
                                         public string Name { get; set; } = "";
                                         public int Count { get; } = 0;
                                         private string Secret { get; set; } = "";
                                     }
                                     """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractInterfaceCommand.ExecuteAsync("Repo", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("IRepo.cs");
        Assert.Contains("Name", content);
        Assert.Contains("get;", content);
        Assert.Contains("set;", content);
        Assert.Contains("Count", content);
        Assert.DoesNotContain("Secret", content);
    }

    [Fact]
    public async Task uses_custom_namespace_override()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Widget.cs", """
                                       namespace MyApp;
                                       public class Widget
                                       {
                                           public void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractInterfaceCommand.ExecuteAsync("Widget", "ignored", null, "MyApp.Contracts", false);
        }

        var content = await project.ReadSourceAsync("IWidget.cs");
        Assert.Contains("namespace MyApp.Contracts;", content);
    }

    [Fact]
    public async Task includes_method_parameters_in_the_interface()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                           namespace MyApp;
                                           public class Calculator
                                           {
                                               public int Add(int a, int b) => a + b;
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ExtractInterfaceCommand.ExecuteAsync("Calculator", "ignored", null, null, false);
        }

        var content = await project.ReadSourceAsync("ICalculator.cs");
        Assert.Contains("int Add(int a, int b)", content);
    }
}
