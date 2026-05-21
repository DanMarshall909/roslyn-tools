using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;

namespace RoslynTools.Tests.Mutation;

public class MakeAsync : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public MakeAsync(RtServicesFixture services) => _services = services;

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
    public async Task renames_the_method_with_an_async_suffix()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "GetAgent", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("GetAgentAsync", content);
        Assert.DoesNotContain("public string GetAgent(", content);
    }

    [Fact]
    public async Task wraps_the_return_type_in_a_task()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "GetAgent", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("Task<string>", content);
        Assert.Contains("GetAgentAsync", content);
    }

    [Fact]
    public async Task adds_the_async_keyword()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "GetAgent", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Matches(@"async\s+Task<string>\s+GetAgentAsync", content);
    }

    [Fact]
    public async Task updates_direct_callers_to_await_the_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "GetAgent", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("await _svc.GetAgentAsync(", content);
    }

    [Fact]
    public async Task leaves_existing_async_methods_unchanged()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        var originalContent = await project.ReadSourceAsync("AgentService.cs");

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "AlreadyAsyncMethod", false, "ignored", false);
        }

        var afterContent = await project.ReadSourceAsync("AgentService.cs");
        Assert.Equal(originalContent, afterContent);
    }

    [Fact]
    public async Task preserves_method_name_when_no_rename_is_true()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "GetAgent", true, "ignored", false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        Assert.Contains("async", content);
        Assert.DoesNotContain("GetAgentAsync", content);
        // Call sites keep the original name
        Assert.Contains("_svc.GetAgent(", content);
    }

    [Fact]
    public async Task converts_void_return_type_to_task()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Svc.cs", """
                                    public class Svc
                                    {
                                        public void Send() { }
                                    }
                                    """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("Svc", "Send", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("Svc.cs");
        Assert.Contains("async Task SendAsync", content);
        Assert.DoesNotContain("Task<void>", content);
    }

    [Fact]
    public async Task promotes_void_sync_caller_to_async_task()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "GetAgent", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("AgentService.cs");
        // UseAgent was void non-async; it must be promoted to async Task
        Assert.Contains("async Task UseAgent()", content);
    }

    [Fact]
    public async Task does_not_double_wrap_already_awaited_call_sites()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Svc.cs", """
                                    using System.Threading.Tasks;

                                    public class Processor
                                    {
                                        public string Run() => "ok";
                                    }

                                    public class Consumer
                                    {
                                        private readonly Processor _p = new();

                                        // Already written with await even though Run is currently sync
                                        public async Task Go()
                                        {
                                            var r = await Task.FromResult(_p.Run());
                                        }
                                    }
                                    """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("Processor", "Run", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("Svc.cs");
        // The call inside Task.FromResult is not a direct invocation of Run that returns Task,
        // so there should be no doubled-up await on the Run call itself
        Assert.DoesNotContain("await await", content);
    }

    [Fact]
    public async Task does_not_double_wrap_task_return_type_on_caller_promotion()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Svc.cs", """
                                    using System.Threading.Tasks;

                                    public class Worker
                                    {
                                        public string Fetch() => "data";
                                    }

                                    public class Consumer
                                    {
                                        private readonly Worker _w = new();
                                        public Task Run()
                                        {
                                            var result = _w.Fetch();
                                            return Task.CompletedTask;
                                        }
                                    }
                                    """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<MakeAsyncCommand>().ExecuteAsync("Worker", "Fetch", false, "ignored", false);
        }

        var content = await project.ReadSourceAsync("Svc.cs");
        // Run already returned Task — promotion must not produce Task<Task>
        Assert.DoesNotContain("Task<Task>", content);
        Assert.Contains("async Task", content);
    }

    [Fact]
    public async Task throws_when_method_not_found()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("AgentService.cs", AgentServiceSource);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                _services.Resolve<MakeAsyncCommand>().ExecuteAsync("AgentService", "NonExistent", false, "ignored", false));
        }
    }
}
