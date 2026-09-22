using RoslynTools.Configuration;
using Shouldly;

namespace RoslynTools.Tests.Configuration;

public sealed class HostConfigurationTests
{
    [Fact]
    public void repository_configuration_is_resolved_only_from_the_git_worktree_root()
    {
        using var fixture = new ConfigurationFixture();
        var repository = fixture.CreateRepository("repo");
        var nested = Directory.CreateDirectory(Path.Combine(repository, "src", "nested")).FullName;
        ConfigurationFixture.WriteJson(Path.Combine(repository, ".rt", "config.json"),
            """{"schemaVersion":1,"daemon":{"enabled":true}}""");
        ConfigurationFixture.WriteJson(Path.Combine(fixture.Root, ".rt", "config.json"),
            """{"schemaVersion":1,"daemon":{"enabled":false}}""");

        var result = fixture.Loader.Resolve(nested, fixture.Home);

        result.IsValid.ShouldBeTrue();
        result.WorktreeRoot.ShouldBe(repository);
        result.RepositoryFile.Path.ShouldBe(Path.Combine(repository, ".rt", "config.json"));
        result.RepositoryFile.Exists.ShouldBeTrue();
        result.Effective.Daemon.Enabled.ShouldBeTrue();

        var outsideGit = fixture.Loader.Resolve(fixture.Root, fixture.Home);
        outsideGit.WorktreeRoot.ShouldBeNull();
        outsideGit.RepositoryFile.Path.ShouldBeNull();
        outsideGit.Effective.Daemon.Enabled.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{ invalid json")]
    [InlineData("{\"schemaVersion\":2}")]
    [InlineData("{\"schemaVersion\":1,\"unexpected\":true}")]
    [InlineData("{\"schemaVersion\":1,\"daemon\":{\"idleTimeoutSeconds\":0}}")]
    [InlineData("{\"schemaVersion\":1,\"daemon\":{\"idleTimeoutSeconds\":1e308}}")]
    [InlineData("{\"schemaVersion\":1,\"daemon\":{\"idleTimeoutSeconds\":1e-300}}")]
    [InlineData("{\"schemaVersion\":1,\"mcp\":{\"enabledTools\":[\"\"]}}")]
    public void invalid_configuration_fails_with_the_exact_file_path(string contents)
    {
        using var fixture = new ConfigurationFixture();
        var globalPath = Path.Combine(fixture.Home, ".rt", "config.json");
        ConfigurationFixture.WriteJson(globalPath, contents);

        var result = fixture.Loader.Resolve(fixture.Root, fixture.Home);

        result.IsValid.ShouldBeFalse();
        result.GlobalFile.Path.ShouldBe(globalPath);
        result.GlobalFile.Exists.ShouldBeTrue();
        result.GlobalFile.IsValid.ShouldBeFalse();
        result.Diagnostics.ShouldContain(diagnostic =>
            diagnostic.Path == globalPath && !string.IsNullOrWhiteSpace(diagnostic.Message));
    }

    [Fact]
    public void daemon_precedence_and_mcp_policy_composition_are_deterministic()
    {
        using var fixture = new ConfigurationFixture(new Dictionary<string, string?>
        {
            [HostConfigurationEnvironment.DaemonEnabled] = "true",
            [HostConfigurationEnvironment.DaemonAutoStart] = "true",
            [HostConfigurationEnvironment.DaemonIdleTimeoutSeconds] = "300"
        });
        var repository = fixture.CreateRepository("repo");
        ConfigurationFixture.WriteJson(Path.Combine(fixture.Home, ".rt", "config.json"),
            """
            {
              "schemaVersion": 1,
              "daemon": { "enabled": false, "autoStart": true, "idleTimeoutSeconds": 100 },
              "mcp": {
                "enabledTools": ["outline", "find_unused"],
                "disabledTools": ["find_unused"]
              }
            }
            """);
        ConfigurationFixture.WriteJson(Path.Combine(repository, ".rt", "config.json"),
            """
            {
              "schemaVersion": 1,
              "daemon": { "enabled": false, "autoStart": false, "idleTimeoutSeconds": 200 },
              "mcp": {
                "enabledTools": ["outline", "source"],
                "disabledTools": ["source"]
              }
            }
            """);

        var result = fixture.Loader.Resolve(repository, fixture.Home);

        result.IsValid.ShouldBeTrue();
        result.Effective.Daemon.Enabled.ShouldBeTrue();
        result.Effective.Daemon.EnabledSource.ShouldBe(
            $"environment:{HostConfigurationEnvironment.DaemonEnabled}");
        result.Effective.Daemon.AutoStart.ShouldBeTrue();
        result.Effective.Daemon.AutoStartSource.ShouldBe(
            $"environment:{HostConfigurationEnvironment.DaemonAutoStart}");
        result.Effective.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(300));
        result.Effective.Daemon.IdleTimeoutSource.ShouldBe(
            $"environment:{HostConfigurationEnvironment.DaemonIdleTimeoutSeconds}");
        result.Effective.Mcp.EnabledTools.ShouldBe(["outline"]);
        result.Effective.Mcp.DisabledTools.ShouldBe(["find_unused", "source"], ignoreOrder: true);
        result.Effective.Mcp.Allows("outline").ShouldBeTrue();
        result.Effective.Mcp.Allows("find_unused").ShouldBeFalse();
        result.Effective.Mcp.Allows("source").ShouldBeFalse();
        result.Effective.Mcp.Allows("search_symbols").ShouldBeFalse();
    }

    [Fact]
    public void repository_daemon_scalars_override_global_values_without_environment_overrides()
    {
        using var fixture = new ConfigurationFixture();
        var repository = fixture.CreateRepository("repo");
        ConfigurationFixture.WriteJson(Path.Combine(fixture.Home, ".rt", "config.json"),
            """
            {
              "schemaVersion": 1,
              "daemon": { "enabled": true, "autoStart": true, "idleTimeoutSeconds": 100 }
            }
            """);
        ConfigurationFixture.WriteJson(Path.Combine(repository, ".rt", "config.json"),
            """
            {
              "schemaVersion": 1,
              "daemon": { "enabled": false, "autoStart": false, "idleTimeoutSeconds": 200 }
            }
            """);

        var result = fixture.Loader.Resolve(repository, fixture.Home);

        result.IsValid.ShouldBeTrue();
        result.Effective.Daemon.Enabled.ShouldBeFalse();
        result.Effective.Daemon.EnabledSource.ShouldBe(result.RepositoryFile.Path);
        result.Effective.Daemon.AutoStart.ShouldBeFalse();
        result.Effective.Daemon.AutoStartSource.ShouldBe(result.RepositoryFile.Path);
        result.Effective.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromSeconds(200));
        result.Effective.Daemon.IdleTimeoutSource.ShouldBe(result.RepositoryFile.Path);
    }

    [Fact]
    public void absent_and_empty_allowlists_have_distinct_meanings()
    {
        using var fixture = new ConfigurationFixture();
        var unrestricted = fixture.Loader.Resolve(fixture.Root, fixture.Home);

        unrestricted.Effective.Mcp.EnabledTools.ShouldBeNull();
        unrestricted.Effective.Mcp.Allows("outline").ShouldBeTrue();
        unrestricted.Effective.Daemon.Enabled.ShouldBeFalse();
        unrestricted.Effective.Daemon.AutoStart.ShouldBeTrue();
        unrestricted.Effective.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(10));

        ConfigurationFixture.WriteJson(Path.Combine(fixture.Home, ".rt", "config.json"),
            """{"schemaVersion":1,"mcp":{"enabledTools":[]}}""");
        var empty = fixture.Loader.Resolve(fixture.Root, fixture.Home);

        empty.IsValid.ShouldBeTrue();
        empty.Effective.Mcp.EnabledTools.ShouldBeEmpty();
        empty.Effective.Mcp.Allows("outline").ShouldBeFalse();
    }

    [Fact]
    public void unrepresentable_environment_timeout_is_ignored_without_overflowing()
    {
        using var fixture = new ConfigurationFixture(new Dictionary<string, string?>
        {
            [HostConfigurationEnvironment.DaemonIdleTimeoutSeconds] = "1e308"
        });

        var result = fixture.Loader.Resolve(fixture.Root, fixture.Home);

        result.IsValid.ShouldBeTrue();
        result.Effective.Daemon.IdleTimeout.ShouldBe(TimeSpan.FromMinutes(10));
        result.Effective.Daemon.IdleTimeoutSource.ShouldBe(HostConfigurationLoader.DefaultSource);
    }

    [Fact]
    public void unreadable_selected_configuration_fails_closed_on_posix()
    {
        if (OperatingSystem.IsWindows())
            return;

        using var fixture = new ConfigurationFixture();
        var repository = fixture.CreateRepository("repo");
        var configurationPath = Path.Combine(repository, ".rt", "config.json");
        ConfigurationFixture.WriteJson(
            configurationPath,
            """{"schemaVersion":1,"mcp":{"enabledTools":[]}}""");
        var configurationDirectory = Path.GetDirectoryName(configurationPath)!;
        File.SetUnixFileMode(configurationDirectory, UnixFileMode.None);
        try
        {
            try
            {
                _ = File.ReadAllText(configurationPath);
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The test applies only when this identity is actually denied by the mode bits.
            }

            var result = fixture.Loader.Resolve(repository, fixture.Home);

            result.IsValid.ShouldBeFalse();
            result.RepositoryFile.Path.ShouldBe(configurationPath);
            result.RepositoryFile.Exists.ShouldBeTrue();
            result.RepositoryFile.IsValid.ShouldBeFalse();
            result.Diagnostics.ShouldContain(diagnostic => diagnostic.Path == configurationPath);
        }
        finally
        {
            File.SetUnixFileMode(
                configurationDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private sealed class ConfigurationFixture : IDisposable
    {
        private readonly IReadOnlyDictionary<string, string?> _environment;
        private readonly HashSet<string> _gitMarkers = new(StringComparer.Ordinal);

        internal ConfigurationFixture(IReadOnlyDictionary<string, string?>? environment = null)
        {
            Root = Path.Combine(Path.GetTempPath(), $"rt-config-{Guid.NewGuid():N}");
            Home = Path.Combine(Root, "home");
            Directory.CreateDirectory(Home);
            _environment = environment ?? new Dictionary<string, string?>();
            Loader = new HostConfigurationLoader(
                name => _environment.TryGetValue(name, out var value) ? value : null,
                path => _gitMarkers.Contains(path));
        }

        internal string Root { get; }
        internal string Home { get; }
        internal HostConfigurationLoader Loader { get; }

        internal string CreateRepository(string name)
        {
            var path = Path.Combine(Root, name);
            var marker = Path.Combine(path, ".git");
            Directory.CreateDirectory(marker);
            _gitMarkers.Add(marker);
            return path;
        }

        internal static void WriteJson(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
    }
}
