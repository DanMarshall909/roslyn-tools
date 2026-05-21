using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Mutation;

public class FixNamespace : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public FixNamespace(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task updates_declaration_to_match_path()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("Services/AgentService.cs", """
                                                      namespace WrongNamespace;
                                                      public class AgentService { }
                                                      """);
        project.AddSource("Program.cs", """
                                        using WrongNamespace;
                                        public class Program { AgentService s = new(); }
                                        """);

        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<FixNamespaceCommand>().ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                false);
        }

        var content = await project.ReadSourceAsync("Services/AgentService.cs");
        Assert.Contains("namespace MyApp.Services", content);
        Assert.DoesNotContain("WrongNamespace", content);
    }

    [Fact]
    public async Task updates_referencing_files()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("Services/AgentService.cs", """
                                                      namespace WrongNamespace;
                                                      public class AgentService { }
                                                      """);
        project.AddSource("Program.cs", """
                                        using WrongNamespace;
                                        public class Program { AgentService s = new(); }
                                        """);

        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<FixNamespaceCommand>().ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                false);
        }

        var programContent = await project.ReadSourceAsync("Program.cs");
        Assert.Contains("using MyApp.Services;", programContent);
        Assert.DoesNotContain("using WrongNamespace;", programContent);
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder("MyApp");
        project.AddSource("Services/AgentService.cs", """
                                                      namespace WrongNamespace;
                                                      public class AgentService { }
                                                      """);
        project.AddSource("Program.cs", """
                                        using WrongNamespace;
                                        public class Program { AgentService s = new(); }
                                        """);

        var (workspace, sln) = project.Build();

        var originalAgentService = await project.ReadSourceAsync("Services/AgentService.cs");
        var originalProgram = await project.ReadSourceAsync("Program.cs");

        var output = new StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(output);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
            {
                await _services.Resolve<FixNamespaceCommand>().ExecuteAsync(
                    Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                    "ignored",
                    true);
            }
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        // Files on disk must be unchanged
        var afterAgentService = await project.ReadSourceAsync("Services/AgentService.cs");
        var afterProgram = await project.ReadSourceAsync("Program.cs");
        Assert.Equal(originalAgentService, afterAgentService);
        Assert.Equal(originalProgram, afterProgram);

        // Output should mention the diff
        var diffOutput = output.ToString();
        Assert.Contains("WrongNamespace", diffOutput);
    }
}
