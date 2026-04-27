using RoslynTools.Commands;

namespace RoslynTools.Tests.Mutation;

public class ConvertConsoleLoggingTests
{
    [Fact]
    public async Task Converts_console_write_line_to_information_log()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run()
                {
                    Console.WriteLine("started");
                    Console.Write("still running");
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, dryRun: false);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.DoesNotContain("Console.Write", content);
        Assert.Contains("_logger.LogInformation(\"started\")", content);
        Assert.Contains("_logger.LogInformation(\"still running\")", content);
    }

    [Fact]
    public async Task Converts_console_error_write_line_to_error_log()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run(Exception ex)
                {
                    Console.Error.WriteLine("failed: " + ex.Message);
                    System.Console.Error.Write("retrying");
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, dryRun: false);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogError(\"failed: {Message}\"", content);
        Assert.Contains("ex.Message", content);
        Assert.Contains("_logger.LogError(\"retrying\")", content);
    }

    [Fact]
    public async Task Converts_interpolated_strings_to_message_templates()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run(string name, int count)
                {
                    Console.WriteLine($"Processed {count} item(s) for {name}");
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, dryRun: false);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogInformation(\"Processed {Count} item(s) for {Name}\"", content);
        Assert.Contains("count", content);
        Assert.Contains("name", content);
    }

    [Fact]
    public async Task Converts_composite_format_strings_to_named_templates()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run(string name, int count)
                {
                    Console.WriteLine("Processed {0} item(s) for {1}", count, name);
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, dryRun: false);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogInformation(\"Processed {Count} item(s) for {Name}\"", content);
        Assert.Contains("count", content);
        Assert.Contains("name", content);
    }

    [Fact]
    public async Task Supports_custom_logger_and_method_names()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run()
                {
                    Console.WriteLine("started");
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(
                solution,
                dryRun: false,
                loggerExpression: "_log",
                informationMethod: "Info",
                errorMethod: "Error");
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_log.Info(\"started\")", content);
    }

    [Fact]
    public async Task Dry_run_does_not_modify_files()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run()
                {
                    Console.WriteLine("started");
                }
            }
            """);
        var (workspace, solution) = project.Build();
        var original = await project.ReadSourceAsync("LogApp.cs");

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, dryRun: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Equal(original, content);
    }

    [Fact]
    public async Task Can_rewrite_only_stderr_calls()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run()
                {
                    Console.WriteLine("payload");
                    Console.Error.WriteLine("diagnostic");
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(
                solution,
                dryRun: false,
                stream: "stderr");
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("Console.WriteLine(\"payload\")", content);
        Assert.Contains("_logger.LogError(\"diagnostic\")", content);
    }

    [Fact]
    public async Task Can_exclude_path_fragments()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("Include.cs", """
            class Include
            {
                void Run()
                {
                    Console.Error.WriteLine("include");
                }
            }
            """);
        project.AddSource("Exclude.cs", """
            class Exclude
            {
                void Run()
                {
                    Console.Error.WriteLine("exclude");
                }
            }
            """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(
                solution,
                dryRun: false,
                stream: "stderr",
                excludePaths: ["Exclude.cs"]);
        }

        var included = await project.ReadSourceAsync("Include.cs");
        var excluded = await project.ReadSourceAsync("Exclude.cs");
        Assert.Contains("_logger.LogError(\"include\")", included);
        Assert.Contains("Console.Error.WriteLine(\"exclude\")", excluded);
    }
}
