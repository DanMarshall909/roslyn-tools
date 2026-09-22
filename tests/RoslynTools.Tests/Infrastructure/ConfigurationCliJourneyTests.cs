using System.Text.Json;
using Shouldly;

namespace RoslynTools.Tests.Infrastructure;

public sealed class ConfigurationCliJourneyTests
{
    [Theory]
    [InlineData("--json config")]
    [InlineData("--json inspect config")]
    public async Task config_reports_invalid_repository_configuration_without_starting_msbuild(
        string arguments)
    {
        using var fixture = new ConfigurationCliFixture("{ invalid json");
        File.WriteAllText(
            Path.Combine(fixture.Repository, "global.json"),
            """{"sdk":{"version":"99.0.999","rollForward":"disable"}}""");

        var result = await RtCliHost.RunAsync(
            arguments,
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldBe(1);
        result.Stderr.ShouldBeEmpty();
        using var document = JsonDocument.Parse(result.Stdout);
        document.RootElement.GetProperty("isValid").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("repositoryFile").GetProperty("path").GetString()
            .ShouldBe(fixture.RepositoryConfigPath);
        document.RootElement.GetProperty("diagnostics").EnumerateArray()
            .ShouldContain(item => item.GetProperty("path").GetString() == fixture.RepositoryConfigPath);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"daemon\":{\"idleTimeoutSeconds\":0}}")]
    [InlineData("{\"schemaVersion\":1,\"daemon\":{\"idleTimeoutSeconds\":1e308}}")]
    public async Task invalid_configuration_stops_an_ordinary_command_with_an_actionable_path(
        string configuration)
    {
        using var fixture = new ConfigurationCliFixture(configuration);

        var result = await RtCliHost.RunAsync(
            "prime",
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldNotBe(0);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldContain(fixture.RepositoryConfigPath);
        result.Stderr.ShouldContain("idleTimeoutSeconds");
    }

    [Fact]
    public async Task config_named_as_an_extra_argument_does_not_bypass_configuration_validation()
    {
        using var fixture = new ConfigurationCliFixture(
            """{"schemaVersion":1,"daemon":{"idleTimeoutSeconds":0}}""");

        var result = await RtCliHost.RunAsync(
            "prime config",
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldNotBe(0);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldContain(fixture.RepositoryConfigPath);
        result.Stderr.ShouldContain("idleTimeoutSeconds");
    }

    [Theory]
    [InlineData("--solution config")]
    [InlineData("--unknown config")]
    public async Task option_prefix_cannot_bypass_configuration_validation(string arguments)
    {
        using var fixture = new ConfigurationCliFixture(
            """{"schemaVersion":1,"daemon":{"idleTimeoutSeconds":0}}""");

        var result = await RtCliHost.RunAsync(
            arguments,
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldNotBe(0);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldContain(fixture.RepositoryConfigPath);
        result.Stderr.ShouldContain("idleTimeoutSeconds");
    }

    [Fact]
    public async Task config_reports_effective_values_and_their_sources()
    {
        using var fixture = new ConfigurationCliFixture(
            """
            {
              "schemaVersion": 1,
              "daemon": { "enabled": false, "autoStart": false, "idleTimeoutSeconds": 45 },
              "mcp": { "enabledTools": ["outline"], "disabledTools": ["source"] }
            }
            """,
            new Dictionary<string, string?>
            {
                ["RT_DAEMON_EXPERIMENT"] = "true"
            });

        var result = await RtCliHost.RunAsync(
            "--json config",
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldBe(0, result.Stderr);
        result.Stderr.ShouldBeEmpty();
        using var document = JsonDocument.Parse(result.Stdout);
        var root = document.RootElement;
        root.GetProperty("isValid").GetBoolean().ShouldBeTrue();
        var daemon = root.GetProperty("effective").GetProperty("daemon");
        daemon.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        daemon.GetProperty("enabledSource").GetString().ShouldBe("environment:RT_DAEMON_EXPERIMENT");
        daemon.GetProperty("autoStart").GetBoolean().ShouldBeFalse();
        daemon.GetProperty("idleTimeoutSeconds").GetDouble().ShouldBe(45);
        root.GetProperty("effective").GetProperty("mcp").GetProperty("enabledTools")
            .EnumerateArray().Select(item => item.GetString()).ShouldBe(["outline"]);
    }

    [Fact]
    public async Task config_reports_an_unknown_exact_mcp_tool_as_an_invalid_file()
    {
        using var fixture = new ConfigurationCliFixture(
            """{"schemaVersion":1,"mcp":{"enabledTools":["missing_tool"]}}""");

        var result = await RtCliHost.RunAsync(
            "--json config",
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldBe(1);
        result.Stderr.ShouldBeEmpty();
        using var document = JsonDocument.Parse(result.Stdout);
        document.RootElement.GetProperty("isValid").GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("repositoryFile").GetProperty("isValid")
            .GetBoolean().ShouldBeFalse();
        document.RootElement.GetProperty("diagnostics").EnumerateArray()
            .ShouldContain(item => item.GetProperty("message").GetString()!
                .Contains("missing_tool", StringComparison.Ordinal));
    }

    [Fact]
    public async Task unknown_exact_mcp_tool_stops_an_ordinary_cli_command_before_msbuild()
    {
        using var fixture = new ConfigurationCliFixture(
            """{"schemaVersion":1,"mcp":{"disabledTools":["missing_tool"]}}""");
        File.WriteAllText(
            Path.Combine(fixture.Repository, "global.json"),
            """{"sdk":{"version":"99.0.999","rollForward":"disable"}}""");

        var result = await RtCliHost.RunAsync(
            "prime",
            fixture.Repository,
            fixture.Environment);

        result.ExitCode.ShouldBe(2);
        result.Stdout.ShouldBeEmpty();
        result.Stderr.ShouldContain(fixture.RepositoryConfigPath);
        result.Stderr.ShouldContain("missing_tool");
    }

    private sealed class ConfigurationCliFixture : IDisposable
    {
        internal ConfigurationCliFixture(
            string repositoryConfig,
            IReadOnlyDictionary<string, string?>? environment = null)
        {
            Root = Path.Combine(Path.GetTempPath(), $"rt-config-cli-{Guid.NewGuid():N}");
            Repository = Path.Combine(Root, "repo");
            Directory.CreateDirectory(Path.Combine(Repository, ".git"));
            RepositoryConfigPath = Path.Combine(Repository, ".rt", "config.json");
            Directory.CreateDirectory(Path.GetDirectoryName(RepositoryConfigPath)!);
            File.WriteAllText(RepositoryConfigPath, repositoryConfig);
            var profile = Path.Combine(Root, "profile");
            Directory.CreateDirectory(profile);
            Environment = new Dictionary<string, string?>(environment ?? new Dictionary<string, string?>())
            {
                ["HOME"] = profile,
                ["USERPROFILE"] = profile,
                ["XDG_CONFIG_HOME"] = Path.Combine(profile, ".config"),
                ["XDG_DATA_HOME"] = Path.Combine(profile, ".local", "share"),
                ["LOCALAPPDATA"] = Path.Combine(profile, ".local", "share"),
                ["RT_DAEMON_IDLE_TIMEOUT_SECONDS"] = null,
                ["RT_DAEMON_AUTO_START"] = null
            };
        }

        private string Root { get; }
        internal string Repository { get; }
        internal string RepositoryConfigPath { get; }
        internal IReadOnlyDictionary<string, string?> Environment { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
