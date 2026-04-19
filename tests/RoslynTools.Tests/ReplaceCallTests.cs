using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests;

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
    public async Task ReplaceCall_AllInvocationsReplaced()
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
    public async Task ReplaceCall_ArgumentsPreserved()
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
    public async Task ReplaceCall_ZeroCallSites_PrintsMessage()
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
    public async Task ReplaceCall_DryRun_NoWrites()
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
