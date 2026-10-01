using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ReplaceCall : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ReplaceCall(RtServicesFixture services) => _services = services;

    private const string LogAppSource = """
                                        class Logger {
                                          public static void Log(string msg) { }
                                          public static void LogInfo(string msg) { }
                                        }
                                        class App {
                                          void Run() { Logger.Log("a"); Logger.Log("b"); Logger.Log("c"); }
                                        }
                                        """;

    private static (AdhocProjectBuilder Project, Workspace Workspace, Solution Solution) BuildFixture()
    {
        var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", LogAppSource);
        var (workspace, sln) = project.Build();
        return (project, workspace, sln);
    }

    [Fact]
    public async Task replaces_all_invocations()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Logger.Log", "LogInfo");

            var content = await project.ReadSourceAsync("LogApp.cs");

            content.ShouldNotContain("Logger.Log(", Case.Sensitive);
            CountOccurrences(content, "Logger.LogInfo(").ShouldBe(3);
        }
    }

    [Fact]
    public async Task preserves_existing_arguments()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class Logger {
                                         public static void Log(string msg) { }
                                         public static void LogInfo(string msg) { }
                                       }
                                       class App {
                                         void Run() { Logger.Log(string.Format("x={0}", 1)); }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync("Logger.Log", "LogInfo");
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("Logger.LogInfo(string.Format(\"x={0}\", 1))", Case.Sensitive);
    }

    [Fact]
    public async Task reports_when_no_call_sites_are_found()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class Logger {
                                         public static void Log(string msg) { }
                                         public static void LogInfo(string msg) { }
                                       }
                                       class App {
                                         void Run() { }
                                       }
                                       """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            var execution = await ExecuteAsync("Logger.Log", "LogInfo");
            execution.Status.ShouldBe(MutationExecutionStatus.NoOp);
            execution.Diagnostics.ShouldContain(diagnostic =>
                diagnostic.Message.Contains("Logger.Log", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            var originalContent = await project.ReadSourceAsync("LogApp.cs");

            var execution = await ExecuteAsync("Logger.Log", "LogInfo", dryRun: true);

            var afterContent = await project.ReadSourceAsync("LogApp.cs");
            afterContent.ShouldBe(originalContent);

            execution.Status.ShouldBe(MutationExecutionStatus.DryRun);
            execution.ChangedFiles.ShouldHaveSingleItem().ShouldEndWith("LogApp.cs");
        }
    }

    [Fact]
    public async Task if_arg_literal_only_replaces_matching_calls()
    {
        using var project = new AdhocProjectBuilder("FactoryApp");
        project.AddSource("FactoryApp.cs", """
                                           class Factory {
                                             public static Option<string> CreateStringOption(string name, string help) => null;
                                             public static Option<string> CreateMethodOption(string help) => null;
                                           }
                                           class App {
                                             void Build() {
                                               var methodOpt = Factory.CreateStringOption("--method", "Method to process");
                                               var fileOpt = Factory.CreateStringOption("--file", "File to process");
                                               var anotherMethod = Factory.CreateStringOption("--method", "Another method");
                                             }
                                           }
                                           """);
        var (workspace, sln) = project.Build();

        using (WorkspaceLoader.UseTrustedOverride(workspace, sln, project.Directory))
        {
            await ExecuteAsync(
                "Factory.CreateStringOption",
                "Factory.CreateMethodOption",
                ifArgIndex: 0,
                ifArgLiteral: "--method");
        }

        var content = await project.ReadSourceAsync("FactoryApp.cs");

        // Should replace only the --method calls, not --file
        CountOccurrences(content, "Factory.CreateMethodOption(").ShouldBe(2);
        CountOccurrences(content, "Factory.CreateStringOption(\"--file\"").ShouldBe(1);
        content.ShouldNotContain("CreateStringOption(\"--method\"", Case.Sensitive);
    }

    private async Task<MutationExecutionResult<ReplaceCallResult>> ExecuteAsync(
        string from,
        string to,
        bool dryRun = false,
        int? ifArgIndex = null,
        string? ifArgLiteral = null)
    {
        var result = await _services.Resolve<ReplaceCallOperation>().ExecuteAsync(
            new ReplaceCallRequest(from, to, "ignored", dryRun, ifArgIndex, ifArgLiteral));
        return result.Unwrap();
    }

    private static int CountOccurrences(string text, string pattern)
    {
        int count = 0, idx = 0;
        while ((idx = text.IndexOf(pattern, idx, StringComparison.Ordinal)) >= 0)
        {
            count++;
            idx += pattern.Length;
        }

        return count;
    }
}
