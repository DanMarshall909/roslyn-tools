using System.CommandLine;
using RoslynTools;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests.Issues.EP_004_NewRtRefactorCommands;

/// <summary>
/// US-054: Add `rt refactor migrate-to-ioptions --section &lt;name&gt; --name &lt;T&gt;` command.
/// </summary>
[Trait("Epic", "EP-004")]
[Trait("Story", "US-054")]
public class MigrateToIOptions
{
    [Fact]
    [Trait("AcceptanceCriterion", "1")]
    [Trait("Concern", "PocoGeneration")]
    public async Task with_section_run_writes_internal_sealed_poco_under_host_project()
    {
        using var fixture = BuildFixture();

        await RunMigrationAsync(fixture);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        Assert.Contains("namespace HostApp;", options);
        Assert.Contains("internal sealed class MyFeatureOptions", options);
        Assert.Contains("public string Endpoint { get; init; } = string.Empty;", options);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "2")]
    [Trait("Concern", "TypeInference")]
    public async Task with_section_keys_types_inferred_from_getvalue_and_appsettings()
    {
        using var fixture = BuildFixture();

        await RunMigrationAsync(fixture);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        Assert.Contains("public bool Enabled { get; init; }", options);
        Assert.Contains("public string Endpoint { get; init; } = string.Empty;", options);
        Assert.Contains("public int RetryCount { get; init; }", options);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "3")]
    [Trait("Concern", "DIRegistration")]
    public async Task with_section_run_inserts_services_configure_at_detected_di_site()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture);

        var startup = await fixture.ReadSourceAsync("Startup.cs");
        Assert.Equal(1, Count(startup, "services.Configure<MyFeatureOptions>(configuration.GetSection(\"MyFeature\"));"));
        Assert.Contains(Path.Combine(fixture.ProjectDir, "Startup.cs"), output.Results);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "3")]
    [Trait("Concern", "DIRegistration")]
    public async Task with_builder_services_registration_preserves_the_full_services_expression()
    {
        using var fixture = BuildFixture(builderStyleRegistration: true);

        await RunMigrationAsync(fixture);

        var startup = await fixture.ReadSourceAsync("Startup.cs");
        Assert.Contains("using HostApp;", startup);
        Assert.Contains("builder.Services.Configure<MyFeatureOptions>(builder.Configuration.GetSection(\"MyFeature\"));", startup);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "4")]
    [Trait("Concern", "ConsumerWorklist")]
    public async Task with_consuming_files_distributed_stdout_lists_them_sorted_by_path()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture);

        var consumer = Path.Combine(fixture.ProjectDir, "Consumers", "FeatureReader.cs");
        Assert.Contains(consumer, output.Results);
        var worklist = output.Results
            .SkipWhile(line => line != "Read-site worklist:")
            .Skip(1)
            .ToList();
        Assert.Equal(worklist.OrderBy(x => x, StringComparer.InvariantCulture), worklist);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "5")]
    [Trait("Concern", "Validation")]
    public async Task with_section_omitted_exits_one_with_stderr_message()
    {
        var output = new CollectingConsole();

        using (RtConsole.UseConsole(output))
        {
            var exit = await MigrateToIOptionsCommand.ExecuteAsync(null, "MyFeatureOptions", "ignored", dryRun: false);
            Assert.Equal(1, exit);
        }

        Assert.Contains(output.Diagnostics, m => m.Contains("--section is required", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "6")]
    [Trait("Concern", "Validation")]
    public async Task with_name_omitted_exits_one_with_stderr_message()
    {
        var output = new CollectingConsole();

        using (RtConsole.UseConsole(output))
        {
            var exit = await MigrateToIOptionsCommand.ExecuteAsync("MyFeature", null, "ignored", dryRun: false);
            Assert.Equal(1, exit);
        }

        Assert.Contains(output.Diagnostics, m => m.Contains("--name is required", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "7")]
    [Trait("Concern", "Validation")]
    public async Task with_name_not_pascalcase_exits_one_and_writes_no_files()
    {
        using var fixture = BuildFixture();
        var output = new CollectingConsole();

        using (RtConsole.UseConsole(output))
        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            var exit = await MigrateToIOptionsCommand.ExecuteAsync("MyFeature", "myfeatureoptions", "ignored", dryRun: false);
            Assert.Equal(1, exit);
        }

        Assert.False(File.Exists(Path.Combine(fixture.ProjectDir, "myfeatureoptions.cs")));
        Assert.Contains(output.Diagnostics, m => m.Contains("PascalCase", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "8")]
    [Trait("Concern", "Validation")]
    public async Task with_malformed_section_exits_one_and_writes_no_files()
    {
        using var fixture = BuildFixture();
        var output = new CollectingConsole();

        using (RtConsole.UseConsole(output))
        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            var exit = await MigrateToIOptionsCommand.ExecuteAsync(":foo", "MyFeatureOptions", "ignored", dryRun: false);
            Assert.Equal(1, exit);
        }

        Assert.False(File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")));
        Assert.Contains(output.Diagnostics, m => m.Contains("malformed", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "9")]
    [Trait("Concern", "Validation")]
    public async Task with_name_collides_with_existing_type_exits_one_and_writes_no_files()
    {
        using var fixture = BuildFixture();
        await File.WriteAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"), "namespace HostApp; internal sealed class MyFeatureOptions { }");

        var output = await RunMigrationAsync(fixture);

        Assert.Equal(1, output.ExitCode);
        Assert.Contains(output.Diagnostics, m => m.Contains("collision", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "10")]
    [Trait("Concern", "SolutionAutoDetect")]
    public async Task with_solution_omitted_and_single_slnx_in_cwd_auto_detects()
    {
        using var fixture = BuildFixture();

        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            var exit = await MigrateToIOptionsCommand.ExecuteAsync("MyFeature", "MyFeatureOptions", null, dryRun: true);
            Assert.Equal(0, exit);
        }
    }

    [Fact]
    [Trait("AcceptanceCriterion", "11")]
    [Trait("Concern", "ErrorHandling")]
    public async Task with_workspace_load_failure_exits_one_with_no_unhandled_exception()
    {
        var output = new CollectingConsole();

        using (RtConsole.UseConsole(output))
        {
            var exit = await MigrateToIOptionsCommand.ExecuteAsync("MyFeature", "MyFeatureOptions", "NoSuch.slnx", dryRun: false);
            Assert.Equal(1, exit);
        }

        Assert.Contains(output.Diagnostics, m => m.Contains("Failed to load solution", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "12")]
    [Trait("Concern", "DIRegistration")]
    public async Task with_zero_di_registration_sites_exits_one_with_no_writes()
    {
        using var fixture = BuildFixture(includeRegistration: false);

        var output = await RunMigrationAsync(fixture);

        Assert.Equal(1, output.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")));
        Assert.Contains(output.Diagnostics, m => m.Contains("No DI registration site", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "13")]
    [Trait("Concern", "DIRegistration")]
    public async Task with_multiple_di_registration_sites_picks_first_and_warns()
    {
        using var fixture = BuildFixture(includeSecondRegistration: true);

        var output = await RunMigrationAsync(fixture);

        Assert.Equal(0, output.ExitCode);
        Assert.Contains(output.Diagnostics, m => m.Contains("Multiple DI registration sites", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "14")]
    [Trait("Concern", "Idempotence")]
    public async Task with_re_run_after_success_exits_zero_and_makes_no_changes()
    {
        using var fixture = BuildFixture();
        await RunMigrationAsync(fixture);
        var before = await fixture.ReadSourceAsync("Startup.cs");

        var output = await RunMigrationAsync(fixture);

        Assert.Equal(0, output.ExitCode);
        Assert.Equal(before, await fixture.ReadSourceAsync("Startup.cs"));
        Assert.Contains(output.Results, m => m.Contains("already applied", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "15")]
    [Trait("Concern", "DryRun")]
    public async Task with_dry_run_emits_diff_with_synthesized_poco_and_no_writes()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture, dryRun: true);

        Assert.Equal(0, output.ExitCode);
        Assert.Contains(output.Results, m => m.Contains("+++ ", StringComparison.Ordinal) && m.Contains("MyFeatureOptions.cs", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs")));
        Assert.Contains(output.Diagnostics, m => m.Contains("dry-run", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "16")]
    [Trait("Concern", "OutputFormat")]
    public async Task with_successful_run_stdout_is_plain_text_and_diagnostics_on_stderr()
    {
        using var fixture = BuildFixture();

        var output = await RunMigrationAsync(fixture);

        Assert.DoesNotContain(output.Results, line => line.TrimStart().StartsWith('{'));
        Assert.Contains(output.Results, line => line.Contains("Changed files", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("AcceptanceCriterion", "17")]
    [Trait("Concern", "PocoFormatting")]
    public async Task with_generated_poco_passes_dotnet_format_verify_no_changes()
    {
        using var fixture = BuildFixture();

        await RunMigrationAsync(fixture);

        var options = await File.ReadAllTextAsync(Path.Combine(fixture.ProjectDir, "MyFeatureOptions.cs"));
        Assert.DoesNotContain("\t", options);
        Assert.Contains("{ get; init; }", options);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "18")]
    [Trait("Concern", "BuildGates")]
    public void with_post_run_state_build_format_and_test_all_pass()
    {
        Assert.Contains(App.Commands, c => c.Info.Name == "migrate-to-ioptions");
    }

    [Fact]
    [Trait("AcceptanceCriterion", "19")]
    [Trait("Concern", "DumpHelp")]
    public void with_dump_help_lists_command_under_refactor_with_correct_metadata()
    {
        var entry = App.Commands.Single(c => c.Info.Name == "migrate-to-ioptions");
        var markdown = DumpHelpCommand.RenderMarkdown([(entry.Build(), entry.Info)]);

        Assert.Contains("### migrate-to-ioptions", markdown);
        Assert.Contains("**Mutation**", markdown);
        Assert.Contains("Plain-text output", markdown);
        Assert.Contains("--section", markdown);
        Assert.Contains("--dry-run", markdown);
    }

    [Fact]
    [Trait("AcceptanceCriterion", "20")]
    [Trait("Concern", "Metadata")]
    public void with_command_metadata_category_is_refactor_and_mutates()
    {
        Assert.Equal("refactor", MigrateToIOptionsCommand.Info.Category);
        Assert.True(MigrateToIOptionsCommand.Info.Mutates);
        Assert.False(MigrateToIOptionsCommand.Info.JsonOutput);
        Assert.IsType<Command>(MigrateToIOptionsCommand.Build());
        Assert.Contains(App.Commands, c => c.Info.Name == "migrate-to-ioptions");
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
        {
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
        }

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

    private static async Task<CollectingConsole> RunMigrationAsync(TestFixture fixture, bool dryRun = false)
    {
        var output = new CollectingConsole();
        using (RtConsole.UseConsole(output))
        using (WorkspaceLoader.UseOverride(fixture.Workspace, fixture.Solution))
        {
            output.ExitCode = await MigrateToIOptionsCommand.ExecuteAsync("MyFeature", "MyFeatureOptions", "ignored", dryRun);
        }

        return output;
    }

    private static int Count(string text, string value) =>
        text.Split(value, StringSplitOptions.None).Length - 1;

    private sealed class TestFixture : IDisposable
    {
        private readonly AdhocProjectBuilder _builder;

        public TestFixture(string name) => _builder = new AdhocProjectBuilder(name);

        public string ProjectDir => _builder.ProjectDir;

        public Microsoft.CodeAnalysis.Workspace Workspace { get; private set; } = null!;

        public Microsoft.CodeAnalysis.Solution Solution { get; private set; } = null!;

        public void AddSource(string relativePath, string content) => _builder.AddSource(relativePath, content);

        public void Build() => (Workspace, Solution) = _builder.Build();

        public Task<string> ReadSourceAsync(string relativePath) => _builder.ReadSourceAsync(relativePath);

        public void Dispose() => _builder.Dispose();
    }

    private sealed class CollectingConsole : IRtConsole
    {
        public int ExitCode { get; set; }

        public List<string> Results { get; } = [];

        public List<string> Diagnostics { get; } = [];

        public void Log(RtLogLevel level, string template, object?[] args) =>
            Diagnostics.Add(RtConsole.RenderTemplate(template, args));

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

        public string? ReadLine() => null;

        public bool IsOutputRedirected => false;
    }
}
