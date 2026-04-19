using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class MakeAsyncTests
{
    private const string AgentServiceSource = """
        using System.Threading.Tasks;

        public class AgentService
        {
            public string GetAgent(int id) => id.ToString();
            public string ProcessAgent(int id) => GetAgent(id).ToUpper();
            public async System.Threading.Tasks.Task<string> AlreadyAsyncMethod() => await System.Threading.Tasks.Task.FromResult("x");
        }

        public class Caller
        {
            private readonly AgentService _svc = new AgentService();
            public void UseAgent()
            {
                var result = _svc.GetAgent(1);
            }
            public async System.Threading.Tasks.Task AsyncCaller()
            {
                var result = _svc.GetAgent(2);
            }
        }
        """;

    [Fact]
    public async Task MakeAsync_MethodRenamedWithAsync()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeAsyncCommand.ExecuteAsync("AgentService", "GetAgent", noRename: false, "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("GetAgentAsync", content);
        Assert.DoesNotContain("public string GetAgent(", content);
    }

    [Fact]
    public async Task MakeAsync_ReturnTypeWrapped()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeAsyncCommand.ExecuteAsync("AgentService", "GetAgent", noRename: false, "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("Task<string>", content);
        Assert.Contains("GetAgentAsync", content);
    }

    [Fact]
    public async Task MakeAsync_AsyncKeywordAdded()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeAsyncCommand.ExecuteAsync("AgentService", "GetAgent", noRename: false, "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Matches(@"async\s+Task<string>\s+GetAgentAsync", content);
    }

    [Fact]
    public async Task MakeAsync_DirectCallerUpdated()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeAsyncCommand.ExecuteAsync("AgentService", "GetAgent", noRename: false, "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("await _svc.GetAgentAsync(", content);
    }

    [Fact]
    public async Task MakeAsync_AlreadyAsync_NoOp()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("AgentService.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
            await MakeAsyncCommand.ExecuteAsync("AgentService", "AlreadyAsyncMethod", noRename: false, "ignored", dryRun: false);

        var afterContent = await project.ReadSourceAsync("AgentService.cs");
        Assert.Equal(originalContent, afterContent);
    }
}
