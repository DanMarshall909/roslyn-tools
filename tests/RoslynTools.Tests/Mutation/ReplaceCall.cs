using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;
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
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ReplaceCallCommand>().ExecuteAsync("Logger.Log", "LogInfo", "ignored", false);

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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ReplaceCallCommand>().ExecuteAsync("Logger.Log", "LogInfo", "ignored", false);
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

        var sw = new StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
            {
                await _services.Resolve<ReplaceCallCommand>().ExecuteAsync("Logger.Log", "LogInfo", "ignored", false);
            }
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        var output = sw.ToString();
        output.ShouldContain("Logger.Log", Case.Sensitive);
    }

    [Fact]
    public async Task does_not_modify_files()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var originalContent = await project.ReadSourceAsync("LogApp.cs");

            var sw = new StringWriter();
            var previousOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                await _services.Resolve<ReplaceCallCommand>().ExecuteAsync("Logger.Log", "LogInfo", "ignored", true);
            }
            finally
            {
                Console.SetOut(previousOut);
            }

            var afterContent = await project.ReadSourceAsync("LogApp.cs");
            afterContent.ShouldBe(originalContent);

            var diffOutput = sw.ToString();
            diffOutput.ShouldNotBeEmpty();
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

        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await _services.Resolve<ReplaceCallCommand>().ExecuteAsync("Factory.CreateStringOption", "Factory.CreateMethodOption", "ignored",
                false, 0, "--method");
        }

        var content = await project.ReadSourceAsync("FactoryApp.cs");

        // Should replace only the --method calls, not --file
        CountOccurrences(content, "Factory.CreateMethodOption(").ShouldBe(2);
        CountOccurrences(content, "Factory.CreateStringOption(\"--file\"").ShouldBe(1);
        content.ShouldNotContain("CreateStringOption(\"--method\"", Case.Sensitive);
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
