using RoslynTools.Commands;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

public class ConvertConsoleLogging : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public ConvertConsoleLogging(RtServicesFixture services) => _services = services;

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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldNotContain("Console.Write", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"started\")", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"still running\")", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("_logger.LogError(\"failed: {ErrorMessage}\"", Case.Sensitive);
        content.ShouldContain("ex.Message", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"retrying\")", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
                solution,
                false,
                stream: "stderr",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("\"Found {Symbol} in {FileName}\"", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
                solution,
                false,
                stream: "stderr",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("_logger.LogWarning(\"Warning: skipped item\")", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"Finding matches...\")", Case.Sensitive);
        content.ShouldContain("_logger.LogError(\"Error: failed\")", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("_logger.LogInformation(\"Processed {Count} item(s) for {Name}\"", Case.Sensitive);
        content.ShouldContain("count", Case.Sensitive);
        content.ShouldContain("name", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("_logger.LogInformation(\"Processed {Count} item(s) for {Name}; see logs for details.\", count, name)", Case.Sensitive);
        content.ShouldNotContain("{Value1}", Case.Sensitive);
        content.ShouldNotContain("$\"", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("_logger.LogInformation(\"Processed {Count} item(s) for {Name}\"", Case.Sensitive);
        content.ShouldContain("count", Case.Sensitive);
        content.ShouldContain("name", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
                solution,
                false,
                "_log",
                "Info",
                errorMethod: "Error",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("_log.Info(\"started\")", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldBe(original);
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
                await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
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
        content.ShouldBe(original);
        stdout.ToString().ShouldContain("console logging review: 1 call site(s)", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
                solution,
                false,
                stream: "stderr",
                apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("Console.WriteLine(\"payload\")", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"diagnostic\")", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
                solution,
                false,
                stream: "stderr",
                excludePaths: ["Exclude.cs"],
                apply: true);
        }

        var included = await project.ReadSourceAsync("Include.cs");
        var excluded = await project.ReadSourceAsync("Exclude.cs");
        included.ShouldContain("_logger.LogInformation(\"include\")", Case.Sensitive);
        excluded.ShouldContain("Console.Error.WriteLine(\"exclude\")", Case.Sensitive);
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
                await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
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
        content.ShouldBe(original);

        var output = stdout.ToString();
        output.ShouldContain("console logging review: 2 call site(s)", Case.Sensitive);
        output.ShouldContain("stdout: 1", Case.Sensitive);
        output.ShouldContain("stderr: 1", Case.Sensitive);
        output.ShouldContain("needs-review: 1", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
                solution,
                false,
                stream: "stderr",
                apply: true,
                injectLogger: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldContain("private readonly ILogger<App> _logger;", Case.Sensitive);
        content.ShouldContain("public App(", Case.Sensitive);
        content.ShouldContain("ILogger<App> logger", Case.Sensitive);
        content.ShouldContain("_logger = logger;", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"Starting\")", Case.Sensitive);
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
                await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(
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
        output.ShouldContain("logger injections: 1", Case.Sensitive);
        output.ShouldContain("App: ILogger<App>", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldNotContain("string.Format", Case.Sensitive);
        content.ShouldNotContain("{Value}", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"x={X}\", x)", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldNotContain("string.Format", Case.Sensitive);
        content.ShouldNotContain("{Value}", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"Hello {Name}, count={Count}\"", Case.Sensitive);
        content.ShouldContain("name", Case.Sensitive);
        content.ShouldContain("count", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldNotContain("string.Format", Case.Sensitive);
        content.ShouldContain("_logger.LogInformation(\"hello world\")", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        // Falls back to {Value} wrapping the string.Format expression
        content.ShouldContain("{Value}", Case.Sensitive);
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
            await _services.Resolve<ConvertConsoleLoggingCommand>().ExecuteCoreAsync(solution, false, apply: true);
        }

        var content = await project.ReadSourceAsync("LogApp.cs");
        content.ShouldNotContain("Console.WriteLine", Case.Sensitive);
        content.ShouldContain("{Value}", Case.Sensitive);
        content.ShouldContain("GetMessage()", Case.Sensitive);
    }
}
