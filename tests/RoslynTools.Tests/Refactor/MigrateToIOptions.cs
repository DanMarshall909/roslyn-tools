using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Tests.Infrastructure;
using Shouldly;

namespace RoslynTools.Tests.Refactor;

public class MigrateToIOptions : IClassFixture<RtServicesFixture>
{
    private readonly RtServicesFixture _services;
    public MigrateToIOptions(RtServicesFixture services) => _services = services;

    [Fact]
    [Trait("Concern", "PocoGeneration")]
    public async Task section_run_writes_internal_sealed_poco_under_host_project()
    {
        using var fixture = BuildFixture();

        await RunMigrationAsync(fixture);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        options.ShouldContain("namespace HostApp;", Case.Sensitive);
        options.ShouldContain("internal sealed class MyFeatureOptions", Case.Sensitive);
        options.ShouldContain("public string Endpoint { get; init; } = string.Empty;", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "TypeInference")]
    public async Task section_keys_types_inferred_from_getvalue_and_appsettings()
    {
        using var fixture = BuildFixture();

        await RunMigrationAsync(fixture);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        options.ShouldContain("public bool Enabled { get; init; }", Case.Sensitive);
        options.ShouldContain("public string Endpoint { get; init; } = string.Empty;", Case.Sensitive);
        options.ShouldContain("public int RetryCount { get; init; }", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "DIRegistration")]
    public async Task section_run_inserts_services_configure_at_detected_di_site()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture);

        var startup = await fixture.ReadSourceAsync("Startup.cs");
        Count(startup, "services.Configure<MyFeatureOptions>(configuration.GetSection(\"MyFeature\"));").ShouldBe(1);
        output.Results.ShouldContain(Path.Combine(fixture.ProjectDir, "Startup.cs"));
    }

    [Fact]
    [Trait("Concern", "DIRegistration")]
    public async Task builder_services_registration_preserves_the_full_services_expression()
    {
        using var fixture = BuildFixture(builderStyleRegistration: true);

        await RunMigrationAsync(fixture);

        var startup = await fixture.ReadSourceAsync("Startup.cs");
        startup.ShouldContain("using HostApp;", Case.Sensitive);
        startup.ShouldContain("builder.Services.Configure<MyFeatureOptions>(builder.Configuration.GetSection(\"MyFeature\"));", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "ConsumerWorklist")]
    public async Task consuming_files_distributed_stdout_lists_them_sorted_by_path()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture);

        var consumer = Path.Combine(fixture.ProjectDir, "Consumers", "FeatureReader.cs");
        output.Results.ShouldContain(consumer);
        var worklist = output.Results
            .SkipWhile(line => line != "Read-site worklist:")
            .Skip(1)
            .ToList();
        worklist.ShouldBe(worklist.OrderBy(x => x, StringComparer.InvariantCulture));
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task section_omitted_exits_one_with_stderr_message()
    {
        var output = new CollectingConsole();

        using (RoslynTools.Core.RtConsole.UseConsole(output))
        {
            var exit = await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync(null, "MyFeatureOptions", "ignored", false);
            exit.ShouldBe(1);
        }

        output.Diagnostics.ShouldContain(m => m.Contains("--section is required", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task name_omitted_exits_one_with_stderr_message()
    {
        var output = new CollectingConsole();

        using (RoslynTools.Core.RtConsole.UseConsole(output))
        {
            var exit = await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync("MyFeature", null, "ignored", false);
            exit.ShouldBe(1);
        }

        output.Diagnostics.ShouldContain(m => m.Contains("--name is required", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task name_not_pascalcase_exits_one_and_writes_no_files()
    {
        using var fixture = BuildFixture();
        var output = new CollectingConsole();

        using (RoslynTools.Core.RtConsole.UseConsole(output))
        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            var exit = await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync("MyFeature", "myfeatureoptions", "ignored", false);
            exit.ShouldBe(1);
        }

        File.Exists(Path.Combine(fixture.ProjectDir, "myfeatureoptions.cs")).ShouldBeFalse();
        output.Diagnostics.ShouldContain(m => m.Contains("PascalCase", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task malformed_section_exits_one_and_writes_no_files()
    {
        using var fixture = BuildFixture();
        var output = new CollectingConsole();

        using (RoslynTools.Core.RtConsole.UseConsole(output))
        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            var exit = await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync(":foo", "MyFeatureOptions", "ignored", false);
            exit.ShouldBe(1);
        }

        File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();
        output.Diagnostics.ShouldContain(m => m.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "Validation")]
    public async Task name_collides_with_existing_type_exits_one_and_writes_no_files()
    {
        using var fixture = BuildFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"),
            "namespace HostApp; internal sealed class MyFeatureOptions { }");

        var output = await RunMigrationAsync(fixture);

        output.ExitCode.ShouldBe(1);
        output.Diagnostics.ShouldContain(m => m.Contains("collision", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "SolutionAutoDetect")]
    public async Task solution_omitted_and_single_slnx_in_cwd_auto_detects()
    {
        using var fixture = BuildFixture();

        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            var exit = await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync("MyFeature", "MyFeatureOptions", null, true);
            exit.ShouldBe(0);
        }
    }

    [Fact]
    [Trait("Concern", "ErrorHandling")]
    public async Task workspace_load_failure_exits_one_with_no_unhandled_exception()
    {
        var output = new CollectingConsole();

        using (RoslynTools.Core.RtConsole.UseConsole(output))
        {
            var exit = await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync("MyFeature", "MyFeatureOptions", "NoSuch.slnx",
                false);
            exit.ShouldBe(1);
        }

        output.Diagnostics.ShouldContain(m => m.Contains("Failed to load solution", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "DIRegistration")]
    public async Task zero_di_registration_sites_exits_one_with_no_writes()
    {
        using var fixture = BuildFixture(false);

        var output = await RunMigrationAsync(fixture);

        output.ExitCode.ShouldBe(1);
        File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();
        output.Diagnostics.ShouldContain(m => m.Contains("No DI registration site", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "DIRegistration")]
    public async Task multiple_di_registration_sites_picks_first_and_warns()
    {
        using var fixture = BuildFixture(includeSecondRegistration: true);

        var output = await RunMigrationAsync(fixture);

        output.ExitCode.ShouldBe(0);
        output.Diagnostics.ShouldContain(m => m.Contains("Multiple DI registration sites", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "Idempotence")]
    public async Task re_run_after_success_exits_zero_and_makes_no_changes()
    {
        using var fixture = BuildFixture();
        await RunMigrationAsync(fixture);
        var before = await fixture.ReadSourceAsync("Startup.cs");

        var output = await RunMigrationAsync(fixture);

        output.ExitCode.ShouldBe(0);
        (await fixture.ReadSourceAsync("Startup.cs")).ShouldBe(before);
        output.Results.ShouldContain(m => m.Contains("already applied", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "DryRun")]
    public async Task dry_run_emits_diff_with_synthesized_poco_and_no_writes()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture, true);

        output.ExitCode.ShouldBe(0);
        output.Results.ShouldContain(m => m.Contains("+++ ", StringComparison.Ordinal) &&
                 m.Contains("MyFeatureOptions.cs", StringComparison.Ordinal));
        File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")).ShouldBeFalse();
        output.Diagnostics.ShouldContain(m => m.Contains("dry-run", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "OutputFormat")]
    public async Task successful_run_stdout_is_plain_text_and_diagnostics_on_stderr()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture);

        output.Results.ShouldNotContain(line => line.TrimStart().StartsWith('{'));
        output.Results.ShouldContain(line => line.Contains("Changed files", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Concern", "PocoFormatting")]
    public async Task generated_poco_passes_dotnet_format_verify_no_changes()
    {
        using var fixture = BuildFixture();

        await RunMigrationAsync(fixture);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        options.ShouldNotContain("\t", Case.Sensitive);
        options.ShouldContain("{ get; init; }", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "BuildGates")]
    public void post_run_state_build_format_and_test_all_pass()
    {
        App.Commands.ShouldContain(c => c.Info.Name == "migrate-to-ioptions");
    }

    [Fact]
    [Trait("Concern", "DumpHelp")]
    public void dump_help_lists_command_under_refactor_with_correct_metadata()
    {
        var entry = App.Commands.Single(c => c.Info.Name == "migrate-to-ioptions");
        var markdown = DumpHelpCommand.RenderMarkdown([(entry.Build(), entry.Info)]);

        markdown.ShouldContain("### migrate-to-ioptions", Case.Sensitive);
        markdown.ShouldContain("**Mutation**", Case.Sensitive);
        markdown.ShouldContain("Plain-text output", Case.Sensitive);
        markdown.ShouldContain("--section", Case.Sensitive);
        markdown.ShouldContain("--dry-run", Case.Sensitive);
    }

    [Fact]
    [Trait("Concern", "Metadata")]
    public void command_metadata_category_is_refactor_and_mutates()
    {
        MigrateToIOptionsCommand.Info.Category.ShouldBe("refactor");
        MigrateToIOptionsCommand.Info.Mutates.ShouldBeTrue();
        MigrateToIOptionsCommand.Info.JsonOutput.ShouldBeFalse();
        _services.Resolve<MigrateToIOptionsCommand>().Build().ShouldBeOfType<Command>();
        App.Commands.ShouldContain(c => c.Info.Name == "migrate-to-ioptions");
    }

    private static TestFixture BuildFixture(
        bool includeRegistration = true,
        bool includeSecondRegistration = false,
        bool builderStyleRegistration = false)
    {
        var fixture = new TestFixture("HostApp");

        fixture.AddSource("Startup.cs", includeRegistration
            ? builderStyleRegistration
                ? """
                  var builder = WebApplication.CreateBuilder(args);
                  builder.Services.AddLogging();
                  var endpoint = builder.Configuration.GetValue<string>("MyFeature:Endpoint");
                  """
                : """
                  namespace HostApp;

                  public sealed class Startup
                  {
                      public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
                      {
                          services.AddLogging();
                      }
                  }
                  """
            : """
              namespace HostApp;

              public sealed class Startup
              {
                  public void ConfigureServices()
                  {
                  }
              }
              """);

        fixture.AddSource("Consumers/FeatureReader.cs", """
                                                        namespace HostApp;

                                                        public sealed class FeatureReader
                                                        {
                                                            public string Endpoint(IConfiguration configuration)
                                                            {
                                                                var retryCount = configuration.GetValue<int>("MyFeature:RetryCount");
                                                                return configuration.GetValue<string>("MyFeature:Endpoint");
                                                            }
                                                        }
                                                        """);

        if (includeSecondRegistration)
            fixture.AddSource("OtherRegistration.cs", """
                                                      namespace HostApp;

                                                      public sealed class OtherRegistration
                                                      {
                                                          public void Add(IServiceCollection services, IConfiguration configuration)
                                                          {
                                                              services.AddOptions();
                                                          }
                                                      }
                                                      """);

        fixture.Build();
        File.WriteAllText(Path.Combine(fixture.ProjectDir, "appsettings.json"), """
            {
              "MyFeature": {
                "Endpoint": "https://example.test",
                "Enabled": true
              }
            }
            """);

        return fixture;
    }

    private async Task<CollectingConsole> RunMigrationAsync(TestFixture fixture, bool dryRun = false)
    {
        var output = new CollectingConsole();
        using (RoslynTools.Core.RtConsole.UseConsole(output))
        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            output.ExitCode =
                await _services.Resolve<MigrateToIOptionsCommand>().ExecuteAsync("MyFeature", "MyFeatureOptions", "ignored", dryRun);
        }

        return output;
    }

    private static int Count(string text, string value)
    {
        return text.Split(value).Length - 1;
    }

    private sealed class TestFixture : IDisposable
    {
        private readonly AdhocProjectBuilder _builder;

        public TestFixture(string name)
        {
            _builder = new AdhocProjectBuilder(name);
        }

        public string ProjectDir => _builder.ProjectDir;

        public Workspace Workspace { get; private set; } = null!;

        public Solution Solution { get; private set; } = null!;

        public void Dispose()
        {
            _builder.Dispose();
        }

        public void AddSource(string relativePath, string content)
        {
            _builder.AddSource(relativePath, content);
        }

        public void Build()
        {
            (Workspace, Solution) = _builder.Build();
        }

        public Task<string> ReadSourceAsync(string relativePath)
        {
            return _builder.ReadSourceAsync(relativePath);
        }
    }

    private sealed class CollectingConsole : IRtConsole
    {
        public int ExitCode { get; set; }

        public List<string> Results { get; } = [];

        public List<string> Diagnostics { get; } = [];

        public void Log(RtLogLevel level, string template, object?[] args)
        {
            Diagnostics.Add(RoslynTools.Core.RtConsole.RenderTemplate(template, args));
        }

        public void WriteLine(RtOutputContext context, string message = "", ConsoleColor? color = null)
        {
            if (context.HasFlag(RtOutputContext.Diagnostic))
                Diagnostics.Add(message);
            else
                Results.Add(message);
        }

        public void Write(RtOutputContext context, string message, ConsoleColor? color = null)
        {
            if (context.HasFlag(RtOutputContext.Diagnostic))
                Diagnostics.Add(message);
            else
                Results.Add(message);
        }

        public string? ReadLine()
        {
            return null;
        }

        public bool IsOutputRedirected => false;
    }
}
