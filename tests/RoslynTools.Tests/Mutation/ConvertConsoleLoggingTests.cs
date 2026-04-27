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
        Assert.Contains("_logger.LogError(\"failed: {ErrorMessage}\"", content);
        Assert.Contains("ex.Message", content);
        Assert.Contains("_logger.LogInformation(\"retrying\")", content);
    }

    [Fact]
    public async Task Uses_domain_oriented_placeholder_names_for_common_expressions()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run(Symbol symbol, string path)
                {
                    Console.Error.WriteLine($"Found {symbol.ToDisplayString()} in {Path.GetFileName(path)}");
                }
            }
            class Symbol
            {
                public string ToDisplayString() => "";
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
        Assert.Contains("\"Found {Symbol} in {FileName}\"", content);
    }

    [Fact]
    public async Task Infers_warning_and_info_levels_for_stderr_calls()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run()
                {
                    Console.Error.WriteLine("Warning: skipped item");
                    Console.Error.WriteLine("Finding matches...");
                    Console.Error.WriteLine("Error: failed");
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
        Assert.Contains("_logger.LogWarning(\"Warning: skipped item\")", content);
        Assert.Contains("_logger.LogInformation(\"Finding matches...\")", content);
        Assert.Contains("_logger.LogError(\"Error: failed\")", content);
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
        Assert.Contains("_logger.LogInformation(\"diagnostic\")", content);
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
        Assert.Contains("_logger.LogInformation(\"include\")", included);
        Assert.Contains("Console.Error.WriteLine(\"exclude\")", excluded);
    }

    [Fact]
    public async Task Review_summarizes_matches_without_modifying_files()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
            class App
            {
                void Run(object value)
                {
                    Console.WriteLine(value);
                    Console.Error.WriteLine("Warning: skipped");
                }
            }
            """);
        var (workspace, solution) = project.Build();
        var original = await project.ReadSourceAsync("LogApp.cs");

        using var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using (workspace)
            {
                await ConvertConsoleLoggingCommand.ExecuteCoreAsync(
                    solution,
                    dryRun: false,
                    stream: "all",
                    review: true);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Equal(original, content);

        var output = stdout.ToString();
        Assert.Contains("console logging review: 2 call site(s)", output);
        Assert.Contains("stdout: 1", output);
        Assert.Contains("stderr: 1", output);
        Assert.Contains("needs-review: 1", output);
    }
}
