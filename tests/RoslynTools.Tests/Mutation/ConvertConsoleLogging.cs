using RoslynTools.Commands;

namespace RoslynTools.Tests.Mutation;

public class ConvertConsoleLogging
{
    [Fact]
    public async Task converts_console_write_line_to_information_log()
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
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.DoesNotContain("Console.Write", content);
        Assert.Contains("_logger.LogInformation(\"started\")", content);
        Assert.Contains("_logger.LogInformation(\"still running\")", content);
    }

    [Fact]
    public async Task converts_console_error_write_line_to_error_log()
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
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogError(\"failed: {ErrorMessage}\"", content);
        Assert.Contains("ex.Message", content);
        Assert.Contains("_logger.LogInformation(\"retrying\")", content);
    }

    [Fact]
    public async Task uses_domain_oriented_placeholder_names_for_common_expressions()
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
                false,
                stream: "stderr",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("\"Found {Symbol} in {FileName}\"", content);
    }

    [Fact]
    public async Task infers_warning_and_info_levels_for_stderr_calls()
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
                false,
                stream: "stderr",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogWarning(\"Warning: skipped item\")", content);
        Assert.Contains("_logger.LogInformation(\"Finding matches...\")", content);
        Assert.Contains("_logger.LogError(\"Error: failed\")", content);
    }

    [Fact]
    public async Task converts_interpolated_strings_to_message_templates()
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
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogInformation(\"Processed {Count} item(s) for {Name}\"", content);
        Assert.Contains("count", content);
        Assert.Contains("name", content);
    }

    [Fact]
    public async Task decomposes_interpolated_string_concatenated_with_literal_into_structured_template()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class App
                                       {
                                           void Run(int count, string name)
                                           {
                                               Console.WriteLine($"Processed {count} item(s) for {name}; " + "see logs for details.");
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains(
            "_logger.LogInformation(\"Processed {Count} item(s) for {Name}; see logs for details.\", count, name)",
            content);
        Assert.DoesNotContain("{Value1}", content);
        Assert.DoesNotContain("$\"", content);
    }

    [Fact]
    public async Task converts_composite_format_strings_to_named_templates()
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
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_logger.LogInformation(\"Processed {Count} item(s) for {Name}\"", content);
        Assert.Contains("count", content);
        Assert.Contains("name", content);
    }

    [Fact]
    public async Task supports_custom_logger_and_method_names()
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
                false,
                "_log",
                "Info",
                errorMethod: "Error",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("_log.Info(\"started\")", content);
    }

    [Fact]
    public async Task handles_dry_run_does_not_modify_files()
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
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Equal(original, content);
    }

    [Fact]
    public async Task handles_default_mode_reviews_without_modifying_files()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class App
                                       {
                                           void Run()
                                           {
                                               Console.Error.WriteLine("Starting");
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
                    false,
                    stream: "stderr");
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Equal(original, content);
        Assert.Contains("console logging review: 1 call site(s)", stdout.ToString());
    }

    [Fact]
    public async Task can_rewrite_only_stderr_calls()
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
                false,
                stream: "stderr",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("Console.WriteLine(\"payload\")", content);
        Assert.Contains("_logger.LogInformation(\"diagnostic\")", content);
    }

    [Fact]
    public async Task can_exclude_path_fragments()
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
                false,
                stream: "stderr",
                excludePaths: ["Exclude.cs"],
                apply: true);
        }

        var included = await project.ReadSourceAsync("Include.cs");
        var excluded = await project.ReadSourceAsync("Exclude.cs");
        Assert.Contains("_logger.LogInformation(\"include\")", included);
        Assert.Contains("Console.Error.WriteLine(\"exclude\")", excluded);
    }

    [Fact]
    public async Task summarizes_matches_without_modifying_files_in_review()
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
                    false,
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

    [Fact]
    public async Task can_reuse_add_dependency_to_inject_logger_before_rewriting()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       public interface ILogger<T>
                                       {
                                           void LogInformation(string message);
                                       }

                                       public class App
                                       {
                                           private readonly string _name;

                                           public App(string name)
                                           {
                                               _name = name;
                                           }

                                           public void Run()
                                           {
                                               Console.Error.WriteLine("Starting");
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(
                solution,
                false,
                stream: "stderr",
                apply: true,
                injectLogger: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.Contains("private readonly ILogger<App> _logger;", content);
        Assert.Contains("public App(", content);
        Assert.Contains("ILogger<App> logger", content);
        Assert.Contains("_logger = logger;", content);
        Assert.Contains("_logger.LogInformation(\"Starting\")", content);
    }

    [Fact]
    public async Task reports_logger_injection_targets_in_review()
    {
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       public class App
                                       {
                                           public void Run()
                                           {
                                               Console.Error.WriteLine("Starting");
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using var stdout = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(stdout);
        try
        {
            using (workspace)
            {
                await ConvertConsoleLoggingCommand.ExecuteCoreAsync(
                    solution,
                    false,
                    stream: "stderr",
                    review: true,
                    injectLogger: true);
            }
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        var output = stdout.ToString();
        Assert.Contains("logger injections: 1", output);
        Assert.Contains("App: ILogger<App>", output);
    }

    // US-001: string.Format decomposition tests

    [Fact]
    public async Task handles_string_format_single_arg_becomes_named_placeholder()
    {
        // AC#7a: Console.WriteLine(string.Format("x={0}", x))
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class App
                                       {
                                           void Run(int x)
                                           {
                                               Console.WriteLine(string.Format("x={0}", x));
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.DoesNotContain("string.Format", content);
        Assert.DoesNotContain("{Value}", content);
        Assert.Contains("_logger.LogInformation(\"x={X}\", x)", content);
    }

    [Fact]
    public async Task handles_string_format_two_args_with_console_write_becomes_named_placeholders()
    {
        // AC#7b: Console.Write with two string.Format args
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class App
                                       {
                                           void Run(string name, int count)
                                           {
                                               Console.Write(string.Format("Hello {0}, count={1}", name, count));
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.DoesNotContain("string.Format", content);
        Assert.DoesNotContain("{Value}", content);
        Assert.Contains("_logger.LogInformation(\"Hello {Name}, count={Count}\"", content);
        Assert.Contains("name", content);
        Assert.Contains("count", content);
    }

    [Fact]
    public async Task handles_string_format_with_no_args_treated_as_plain_string()
    {
        // AC#7c: string.Format with no args (constant string) → plain string
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class App
                                       {
                                           void Run()
                                           {
                                               Console.WriteLine(string.Format("hello world"));
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.DoesNotContain("string.Format", content);
        Assert.Contains("_logger.LogInformation(\"hello world\")", content);
    }

    [Fact]
    public async Task handles_string_format_with_iformat_provider_degrades_gracefully()
    {
        // AC#7d: string.Format(IFormatProvider, string, params) — first arg is not a string literal
        // Expect graceful degradation: falls back to {Value} wrapping the entire string.Format call
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       using System.Globalization;
                                       class App
                                       {
                                           void Run(double price)
                                           {
                                               Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "price={0}", price));
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        // Falls back to {Value} wrapping the string.Format expression
        Assert.Contains("{Value}", content);
    }

    [Fact]
    public async Task rejects_non_string_format_invocation_still_uses_value_fallback()
    {
        // AC#7e: non-string.Format invocation as first arg → {Value} placeholder unchanged
        using var project = new AdhocProjectBuilder("LogApp");
        project.AddSource("LogApp.cs", """
                                       class App
                                       {
                                           string GetMessage() => "hello";

                                           void Run()
                                           {
                                               Console.WriteLine(GetMessage());
                                           }
                                       }
                                       """);
        var (workspace, solution) = project.Build();

        using (workspace)
        {
            await ConvertConsoleLoggingCommand.ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        Assert.DoesNotContain("Console.WriteLine", content);
        Assert.Contains("{Value}", content);
        Assert.Contains("GetMessage()", content);
    }
}
