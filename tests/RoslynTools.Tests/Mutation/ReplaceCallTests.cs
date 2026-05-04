using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

public class ReplaceCallTests
{
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
    public async Task Replaces_all_invocations()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", "ignored", dryRun: false);

            var content = await project.ReadSourceAsync("LogApp.cs");

            Assert.DoesNotContain("Logger.Log(", content);
            Assert.Equal(3, CountOccurrences(content, "Logger.LogInfo("));
        }
    }

    [Fact]
    public async Task Preserves_existing_arguments()
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
            await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", "ignored", dryRun: false);

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("Logger.LogInfo(string.Format(\"x={0}\", 1))", content);
    }

    [Fact]
    public async Task Reports_when_no_call_sites_are_found()
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

        var sw = new System.IO.StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            using (WorkspaceLoader.UseOverride(workspace, sln))
                await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", "ignored", dryRun: false);
        }
        finally
        {
            Console.SetOut(previousOut);
        }

        var output = sw.ToString();
        Assert.Contains("Logger.Log", output);
    }

    [Fact]
    public async Task Does_not_modify_files()
    {
        var (project, workspace, sln) = BuildFixture();
        using (project)
        using (WorkspaceLoader.UseOverride(workspace, sln))
        {
            var originalContent = await project.ReadSourceAsync("LogApp.cs");

            var sw = new System.IO.StringWriter();
            var previousOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", "ignored", dryRun: true);
            }
            finally
            {
                Console.SetOut(previousOut);
            }

            var afterContent = await project.ReadSourceAsync("LogApp.cs");
            Assert.Equal(originalContent, afterContent);

            var diffOutput = sw.ToString();
            Assert.NotEmpty(diffOutput);
        }
    }

    [Fact]
    public async Task With_if_arg_literal_only_replaces_matching_calls()
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
            await ReplaceCallCommand.ExecuteAsync("Factory.CreateStringOption", "Factory.CreateMethodOption", "ignored", dryRun: false, ifArgIndex: 0, ifArgLiteral: "--method");

        var content = await project.ReadSourceAsync("FactoryApp.cs");

        // Should replace only the --method calls, not --file
        Assert.Equal(2, CountOccurrences(content, "Factory.CreateMethodOption("));
        Assert.Equal(1, CountOccurrences(content, "Factory.CreateStringOption(\"--file\""));
        Assert.DoesNotContain("CreateStringOption(\"--method\"", content);
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
