using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                false);
        }

        var content = await project.ReadSourceAsync("Services/AgentService.cs");
        content.ShouldContain("namespace MyApp.Services", Case.Sensitive);
        content.ShouldNotContain("WrongNamespace", Case.Sensitive);
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

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                false);
        }

        var programContent = await project.ReadSourceAsync("Program.cs");
        programContent.ShouldContain("using MyApp.Services;", Case.Sensitive);
        programContent.ShouldNotContain("using WrongNamespace;", Case.Sensitive);
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

        MutationExecutionResult<FixNamespaceResult> execution;
        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            execution = await ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                true);
        }

        // Files on disk must be unchanged
        var afterAgentService = await project.ReadSourceAsync("Services/AgentService.cs");
        var afterProgram = await project.ReadSourceAsync("Program.cs");
        afterAgentService.ShouldBe(originalAgentService);
        afterProgram.ShouldBe(originalProgram);

        execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
        execution.ChangedFiles.Count.ShouldBe(2);
    }

    private async Task<MutationExecutionResult<FixNamespaceResult>> ExecuteAsync(
        string file,
        string? solution,
        bool dryRun)
    {
        var result = await _services.Resolve<FixNamespaceOperation>().ExecuteAsync(
            new FixNamespaceRequest(file, solution, dryRun));
        return result.Unwrap();
    }
}
