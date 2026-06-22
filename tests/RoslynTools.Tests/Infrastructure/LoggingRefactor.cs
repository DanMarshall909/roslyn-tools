using RoslynTools.Commands;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public class LoggingRefactor : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public LoggingRefactor(RtServicesFixture services) => _services = services;

    [Fact]
    public async Task captures_info_warning_and_error_without_redirecting_console()
    {
        var console = new CollectingConsole();

        using (Core.RtConsole.UseConsole(console))
        {
            using var recordProject = new AdhocProjectBuilder("RecordApp");
            recordProject.AddSource("Types.cs", """
                                                class WithMutableProp
                                                {
                                                    public int Id { get; set; }
                                                    public string Name { get; init; } = "";
                                                }
                                                """);

            var (recordWorkspace, recordSolution) = recordProject.Build();
            using (recordWorkspace)
            using (WorkspaceLoader.UseOverride(recordWorkspace, recordSolution))
            {
                await _services.Resolve<ConvertToRecordCommand>().ExecuteAsync("WithMutableProp", "ignored", false);
            }

            using var staticProject = new AdhocProjectBuilder("StaticApp");
            staticProject.AddSource("Service.cs", """
                                                  class Service
                                                  {
                                                      private int _value = 0;
                                                      public int GetValue() { return this._value; }
                                                  }
                                                  """);

            var (staticWorkspace, staticSolution) = staticProject.Build();
            using (staticWorkspace)
            using (WorkspaceLoader.UseOverride(staticWorkspace, staticSolution))
            {
                Environment.ExitCode = 0;
                await _services.Resolve<MakeStaticCommand>().ExecuteAsync("Service.GetValue", "ignored", false);
                Environment.ExitCode.ShouldBe(1);
                Environment.ExitCode = 0;
            }
        }

        console.LogEntries.ShouldContain(e => e.Level == RtLogLevel.Info && e.RenderedMessage.Contains("Converting", StringComparison.Ordinal));
        console.LogEntries.ShouldContain(e => e.Level == RtLogLevel.Warning &&
                 e.RenderedMessage.Contains("mutable", StringComparison.OrdinalIgnoreCase));
        console.LogEntries.ShouldContain(e => e.Level == RtLogLevel.Error &&
                 e.RenderedMessage.Contains("Cannot make static", StringComparison.Ordinal));
    }

    [Fact]
    public async Task reads_only_command_results_still_flow_to_stdout()
    {
        using var project = new AdhocProjectBuilder("GraphApp");
        project.AddSource("OrderService.cs", """
                                             namespace GraphApp;

                                             public class OrderService
                                             {
                                                 public InvoiceService Invoices { get; } = new();
                                             }

                                             public class InvoiceService
                                             {
                                             }
                                             """);

        var (workspace, solution) = project.Build();
        var console = new CollectingConsole();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (Core.RtConsole.UseConsole(console))
        {
            var exitCode = await App.RunAsync(
            [
                "dependency-graph",
                "--solution",
                "ignored"
            ]);

            exitCode.ShouldBe(0);
        }

        var resultOutput = string.Join("", console.WriteEntries
            .Where(e => e.Context.HasFlag(RtOutputContext.Result))
            .Select(e => e.Message));

        resultOutput.ShouldContain("\"nodes\"", Case.Sensitive);
        resultOutput.ShouldContain("OrderService", Case.Sensitive);
        string.Join("", console.LogEntries.Select(e => e.RenderedMessage)).ShouldNotContain("\"nodes\"", Case.Sensitive);
    }

    [Fact]
    public async Task mutation_dry_run_diff_output_remains_on_stdout()
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

        var console = new CollectingConsole();
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (Core.RtConsole.UseConsole(console))
        {
            await _services.Resolve<FixNamespaceCommand>().ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                true);
        }

        var diffOutput = string.Join("\n", console.WriteEntries
            .Where(e => e.Context.HasFlag(RtOutputContext.Diff))
            .Select(e => e.Message));

        diffOutput.ShouldContain("--- ", Case.Sensitive);
        diffOutput.ShouldContain("+++ ", Case.Sensitive);
        diffOutput.ShouldContain("[dry-run]", Case.Sensitive);

        console.LogEntries.ShouldContain(e => e.Level == RtLogLevel.Info &&
                 e.RenderedMessage.Contains("Fixing namespace", StringComparison.Ordinal));
        diffOutput.ShouldNotContain("Fixing namespace", Case.Sensitive);
    }

    [Fact]
    public async Task narrow_dependency_report_remains_stdout_result_output()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("IWideFacade.cs", """
                                            namespace MyApp;

                                            public interface IWideFacade
                                            {
                                                void Send(string message);
                                                void Receive();
                                            }
                                            """);
        project.AddSource("Consumers.cs", """
                                          namespace MyApp;

                                          public sealed class SenderOnlyConsumer
                                          {
                                              private readonly IWideFacade _facade;

                                              public SenderOnlyConsumer(IWideFacade facade)
                                              {
                                                  _facade = facade;
                                              }

                                              public void Run()
                                              {
                                                  _facade.Send("hi");
                                              }
                                          }

                                          public sealed class MixedConsumer
                                          {
                                              private readonly IWideFacade _facade;

                                              public MixedConsumer(IWideFacade facade)
                                              {
                                                  _facade = facade;
                                              }

                                              public void Run()
                                              {
                                                  _facade.Send("hi");
                                                  _facade.Receive();
                                              }
                                          }
                                          """);

        var console = new CollectingConsole();
        var (workspace, solution) = project.Build();
        using (workspace)
        using (WorkspaceLoader.UseOverride(workspace, solution))
        using (Core.RtConsole.UseConsole(console))
        {
            await _services.Resolve<NarrowDependencyCommand>().ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                false);
        }

        var allOutput = string.Join("", console.WriteEntries.Select(e => e.Message));
        allOutput.ShouldContain("Narrowed 1 consumer(s)", Case.Sensitive);
        allOutput.ShouldContain("SenderOnlyConsumer", Case.Sensitive);
        Assert.Contains("MixedConsumer: skipped", allOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DI registration", allOutput, StringComparison.OrdinalIgnoreCase);

        console.LogEntries.ShouldNotContain(e => e.RenderedMessage.Contains("Narrowed 1 consumer(s)", StringComparison.Ordinal));
        console.LogEntries.ShouldNotContain(e => e.RenderedMessage.Contains("DI registration", StringComparison.OrdinalIgnoreCase));
        console.LogEntries.ShouldNotContain(e => e.RenderedMessage.Contains("MixedConsumer", StringComparison.Ordinal));
    }

    [Fact]
    public void console_rt_console_routes_diagnostic_to_stderr()
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            Core.RtConsole.DefaultConsole.Log(RtLogLevel.Warning, "Test warning {Value}", "x");
            Core.RtConsole.DefaultConsole.Log(RtLogLevel.Error, "Test error");
            Core.RtConsole.DefaultConsole.WriteLine(RtOutputContext.Result, "result line");
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }

        stderr.ToString().ShouldContain("[WARNING] Test warning x", Case.Sensitive);
        stderr.ToString().ShouldContain("[ERROR] Test error", Case.Sensitive);
        stdout.ToString().ShouldContain("result line", Case.Sensitive);
        stdout.ToString().ShouldNotContain("[WARNING]", Case.Sensitive);
    }

    [Fact]
    public void restores_outer_console_after_inner_disposes()
    {
        var outer = new CollectingConsole();
        var inner = new CollectingConsole();

        using (Core.RtConsole.UseConsole(outer))
        {
            Core.RtConsole.Log(RtLogLevel.Info, "outer before");

            using (Core.RtConsole.UseConsole(inner))
            {
                Core.RtConsole.Log(RtLogLevel.Info, "inner");
            }

            Core.RtConsole.Log(RtLogLevel.Info, "outer after");
        }

        outer.LogEntries.Count.ShouldBe(2);
        outer.LogEntries.ShouldContain(e => e.RenderedMessage == "outer before");
        outer.LogEntries.ShouldContain(e => e.RenderedMessage == "outer after");
        Assert.Single(inner.LogEntries);
        inner.LogEntries[0].RenderedMessage.ShouldBe("inner");
    }

    [Fact]
    public void result_context_goes_to_stdout_not_diagnostic()
    {
        var console = new CollectingConsole();

        using (Core.RtConsole.UseConsole(console))
        {
            Core.RtConsole.WriteLine(RtOutputContext.Result, "json payload");
            Core.RtConsole.Log(RtLogLevel.Info, "diagnostic message");
        }

        Assert.Single(console.WriteEntries, e => e.Context.HasFlag(RtOutputContext.Result));
        console.WriteEntries.First(e => e.Context.HasFlag(RtOutputContext.Result)).Message.ShouldBe("json payload");
        Assert.Single(console.LogEntries);
        console.WriteEntries.ShouldNotContain(e => e.Context.HasFlag(RtOutputContext.Diagnostic));
    }

    internal sealed class CollectingConsole : IRtConsole
    {
        public List<LogEntry> LogEntries { get; } = [];
        public List<WriteEntry> WriteEntries { get; } = [];

        public void Log(RtLogLevel level, string template, object?[] args)
        {
            var rendered = Core.RtConsole.RenderTemplate(template, args);
            LogEntries.Add(new LogEntry(level, template, args, rendered));
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        {
            WriteEntries.Add(new WriteEntry(context, message, color));
        }

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        {
            WriteEntries.Add(new WriteEntry(context, message, color));
        }

        public bool IsOutputRedirected => false;

        public sealed record LogEntry(RtLogLevel Level, string Template, object?[] Args, string RenderedMessage);

        public sealed record WriteEntry(RtOutputContext Context, string Message, ConsoleColor? Color);
    }
}
