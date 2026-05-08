using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Infrastructure;

public class LoggingRefactor
{
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
                await ConvertToRecordCommand.ExecuteAsync("WithMutableProp", "ignored", false);
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
                await MakeStaticCommand.ExecuteAsync("Service.GetValue", "ignored", false);
                Assert.Equal(1, Environment.ExitCode);
                Environment.ExitCode = 0;
            }
        }

        Assert.Contains(console.LogEntries,
            e => e.Level == RtLogLevel.Info && e.RenderedMessage.Contains("Converting", StringComparison.Ordinal));
        Assert.Contains(console.LogEntries,
            e => e.Level == RtLogLevel.Warning &&
                 e.RenderedMessage.Contains("mutable", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(console.LogEntries,
            e => e.Level == RtLogLevel.Error &&
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

            Assert.Equal(0, exitCode);
        }

        var resultOutput = string.Join("", console.WriteEntries
            .Where(e => e.Context.HasFlag(RtOutputContext.Result))
            .Select(e => e.Message));

        Assert.Contains("\"nodes\"", resultOutput);
        Assert.Contains("OrderService", resultOutput);
        Assert.DoesNotContain("\"nodes\"", string.Join("", console.LogEntries.Select(e => e.RenderedMessage)));
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
            await FixNamespaceCommand.ExecuteAsync(
                Path.Combine(project.ProjectDir, "Services", "AgentService.cs"),
                "ignored",
                true);
        }

        var diffOutput = string.Join("\n", console.WriteEntries
            .Where(e => e.Context.HasFlag(RtOutputContext.Diff))
            .Select(e => e.Message));

        Assert.Contains("--- ", diffOutput);
        Assert.Contains("+++ ", diffOutput);
        Assert.Contains("[dry-run]", diffOutput);

        Assert.Contains(console.LogEntries,
            e => e.Level == RtLogLevel.Info &&
                 e.RenderedMessage.Contains("Fixing namespace", StringComparison.Ordinal));
        Assert.DoesNotContain("Fixing namespace", diffOutput);
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
            await NarrowDependencyCommand.ExecuteAsync(
                "IWideFacade",
                ["Send"],
                "ISender",
                "ignored",
                false);
        }

        var allOutput = string.Join("", console.WriteEntries.Select(e => e.Message));
        Assert.Contains("Narrowed 1 consumer(s)", allOutput);
        Assert.Contains("SenderOnlyConsumer", allOutput);
        Assert.Contains("MixedConsumer: skipped", allOutput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DI registration", allOutput, StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(console.LogEntries,
            e => e.RenderedMessage.Contains("Narrowed 1 consumer(s)", StringComparison.Ordinal));
        Assert.DoesNotContain(console.LogEntries,
            e => e.RenderedMessage.Contains("DI registration", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(console.LogEntries,
            e => e.RenderedMessage.Contains("MixedConsumer", StringComparison.Ordinal));
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

        Assert.Contains("[WARNING] Test warning x", stderr.ToString());
        Assert.Contains("[ERROR] Test error", stderr.ToString());
        Assert.Contains("result line", stdout.ToString());
        Assert.DoesNotContain("[WARNING]", stdout.ToString());
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

        Assert.Equal(2, outer.LogEntries.Count);
        Assert.Contains(outer.LogEntries, e => e.RenderedMessage == "outer before");
        Assert.Contains(outer.LogEntries, e => e.RenderedMessage == "outer after");
        Assert.Single(inner.LogEntries);
        Assert.Equal("inner", inner.LogEntries[0].RenderedMessage);
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
        Assert.Equal("json payload",
            console.WriteEntries.First(e => e.Context.HasFlag(RtOutputContext.Result)).Message);
        Assert.Single(console.LogEntries);
        Assert.DoesNotContain(console.WriteEntries, e => e.Context.HasFlag(RtOutputContext.Diagnostic));
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

        public string? ReadLine()
        {
            return null;
        }

        public bool IsOutputRedirected => false;

        public sealed record LogEntry(RtLogLevel Level, string Template, object?[] Args, string RenderedMessage);

        public sealed record WriteEntry(RtOutputContext Context, string Message, ConsoleColor? Color);
    }
}
