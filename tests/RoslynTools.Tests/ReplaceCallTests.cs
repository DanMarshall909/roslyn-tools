using RoslynTools.Commands;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
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

    private static async Task<(TestProjectBuilder project, string slnPath)> BuildFixtureAsync()
    {
        var project = new TestProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", LogAppSource);
        var slnPath = await project.BuildAsync();
        return (project, slnPath);
    }

    [Fact]
    public async Task ReplaceCall_AllInvocationsReplaced()
    {
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", slnPath, dryRun: false);

            var content = await project.ReadSourceAsync("LogApp.cs");

            Assert.DoesNotContain("Logger.Log(", content);
            Assert.Equal(3, CountOccurrences(content, "Logger.LogInfo("));
        }
    }

    [Fact]
    public async Task ReplaceCall_ArgumentsPreserved()
    {
        using var project = new TestProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class Logger {
              public static void Log(string msg) { }
              public static void LogInfo(string msg) { }
            }
            class App {
              void Run() { Logger.Log(string.Format("x={0}", 1)); }
            }
            """);
        var slnPath = await project.BuildAsync();

        await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", slnPath, dryRun: false);

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("Logger.LogInfo(string.Format(\"x={0}\", 1))", content);
    }

    [Fact]
    public async Task ReplaceCall_ZeroCallSites_PrintsMessage()
    {
        using var project = new TestProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class Logger {
              public static void Log(string msg) { }
              public static void LogInfo(string msg) { }
            }
            class App {
              void Run() { }
            }
            """);
        var slnPath = await project.BuildAsync();

        var sw = new System.IO.StringWriter();
        var previousOut = Console.Out;
        Console.SetOut(sw);
        try
        {
            await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", slnPath, dryRun: false);
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
        var (project, slnPath) = await BuildFixtureAsync();
        using (project)
        {
            var originalContent = await project.ReadSourceAsync("LogApp.cs");

            var sw = new System.IO.StringWriter();
            var previousOut = Console.Out;
            Console.SetOut(sw);
            try
            {
                await ReplaceCallCommand.ExecuteAsync("Logger.Log", "LogInfo", slnPath, dryRun: true);
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
