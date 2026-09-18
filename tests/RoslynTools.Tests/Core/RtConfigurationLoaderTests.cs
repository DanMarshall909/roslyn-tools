using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Configuration;

public sealed class RtConfigurationLoaderTests
{
    [Fact]
    public void Repository_configuration_overrides_global_daemon_values_and_narrows_tools()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteGlobal("""
            {
              "version": 1,
              "daemon": {
                "enabled": true,
                "autoStart": true,
                "idleTimeoutSeconds": 900
              },
              "mcp": {
                "enabledTools": ["outline", "source", "relationships"],
                "disabledTools": ["rename"]
              }
            }
            """);
        fixture.WriteRepository("""
            {
              "version": 1,
              "daemon": {
                "autoStart": false,
                "idleTimeoutSeconds": 120
              },
              "mcp": {
                "enabledTools": ["source", "relationships", "rename"],
                "disabledTools": ["relationships"]
              }
            }
            """);

        var result = fixture.Load();

        result.IsSuccess.ShouldBeTrue(result.Error);
        var configuration = result.Configuration!;
        configuration.RepositoryRoot.ShouldBe(fixture.RepositoryRoot);
        configuration.Daemon.Enabled.ShouldBeTrue();
        configuration.Daemon.AutoStart.ShouldBeFalse();
        configuration.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(120));
        configuration.Mcp.Allows("source").ShouldBeTrue();
        configuration.Mcp.Allows("outline").ShouldBeFalse();
        configuration.Mcp.Allows("relationships").ShouldBeFalse();
        configuration.Mcp.Allows("rename").ShouldBeFalse();
    }

    [Fact]
    public void Empty_repository_allowlist_disables_every_mcp_tool()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteRepository("""
            {
              "version": 1,
              "mcp": {
                "enabledTools": []
              }
            }
            """);

        var result = fixture.Load();

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Configuration!.Mcp.Allows("outline").ShouldBeFalse();
    }

    [Fact]
    public void Configuration_outside_a_git_worktree_is_not_loaded_as_repository_policy()
    {
        using var fixture = new ConfigurationFixture(createGitMarker: false);
        fixture.WriteRepository("""
            {
              "version": 1,
              "daemon": { "enabled": true }
            }
            """);

        var result = fixture.Load();

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Configuration!.RepositoryRoot.ShouldBeNull();
        result.Configuration.Daemon.Enabled.ShouldBeFalse();
        result.Configuration.RepositoryFile.Exists.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{ \"version\": 2 }")]
    [InlineData("{ \"version\": 1, \"unexpected\": true }")]
    [InlineData("{ \"version\": 1, \"daemon\": { \"idleTimeoutSeconds\": 0 } }")]
    [InlineData("{ invalid json")]
    public void Invalid_repository_configuration_fails_with_the_exact_file(string json)
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteRepository(json);

        var result = fixture.Load();

        result.IsSuccess.ShouldBeFalse();
        result.Error!.ShouldContain(fixture.RepositoryConfigurationPath);
        result.Configuration.ShouldBeNull();
    }

    [Fact]
    public void Existing_environment_variables_override_file_values()
    {
        using var fixture = new ConfigurationFixture();
        fixture.WriteGlobal("""
            {
              "version": 1,
              "daemon": {
                "enabled": false,
                "idleTimeoutSeconds": 900
              }
            }
            """);

        var result = fixture.Load(name => name switch
        {
            "RT_DAEMON_EXPERIMENT" => "true",
            "RT_DAEMON_IDLE_TIMEOUT_SECONDS" => "45",
            _ => null
        });

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Configuration!.Daemon.Enabled.ShouldBeTrue();
        result.Configuration.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(45));
    }

    [Fact]
    public void Absent_configuration_preserves_daemon_and_mcp_defaults()
    {
        using var fixture = new ConfigurationFixture();

        var result = fixture.Load();

        result.IsSuccess.ShouldBeTrue(result.Error);
        result.Configuration!.Daemon.Enabled.ShouldBeFalse();
        result.Configuration.Daemon.AutoStart.ShouldBeTrue();
        result.Configuration.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        result.Configuration.Mcp.Allows("any-public-tool").ShouldBeTrue();
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            $"rt-config-{Guid.NewGuid():N}");

        internal ConfigurationFixture(bool createGitMarker = true)
        {
            UserProfile = Path.Combine(_root, "user");
            RepositoryRoot = Path.Combine(_root, "repo");
            InvocationDirectory = Path.Combine(RepositoryRoot, "src", "feature");
            Directory.CreateDirectory(UserProfile);
            Directory.CreateDirectory(InvocationDirectory);
            if (createGitMarker)
                Directory.CreateDirectory(Path.Combine(RepositoryRoot, ".git"));
        }

        internal string UserProfile { get; }
        internal string RepositoryRoot { get; }
        internal string InvocationDirectory { get; }
        internal string RepositoryConfigurationPath =>
            Path.Combine(RepositoryRoot, ".rt", "config.json");

        internal void WriteGlobal(string json) => Write(
            Path.Combine(UserProfile, ".rt", "config.json"),
            json);

        internal void WriteRepository(string json) => Write(RepositoryConfigurationPath, json);

        internal RtConfigurationLoadResult Load(Func<string, string?>? environment = null) =>
            RtConfigurationLoader.Load(
                InvocationDirectory,
                UserProfile,
                environment ?? (_ => null));

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
