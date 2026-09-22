using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Configuration;

internal static class HostConfigurationEnvironment
{
    internal const string DaemonEnabled = "RT_DAEMON_EXPERIMENT";
    internal const string DaemonAutoStart = "RT_DAEMON_AUTO_START";
    internal const string DaemonIdleTimeoutSeconds = "RT_DAEMON_IDLE_TIMEOUT_SECONDS";
}

internal sealed record HostConfigurationDiagnostic(string Path, string Message);

internal sealed record ConfiguredMcpToolName(string Name, string Path);

internal sealed record HostConfigurationFileState(
    string? Path,
    bool Exists,
    bool IsValid,
    string? Error = null);

internal sealed record EffectiveDaemonConfiguration(
    bool Enabled,
    string EnabledSource,
    bool AutoStart,
    string AutoStartSource,
    TimeSpan IdleTimeout,
    string IdleTimeoutSource)
{
    public double IdleTimeoutSeconds => IdleTimeout.TotalSeconds;
}

internal sealed class EffectiveMcpConfiguration
{
    internal EffectiveMcpConfiguration(
        IReadOnlyCollection<string>? enabledTools,
        IReadOnlyCollection<string> disabledTools)
    {
        EnabledTools = enabledTools?.Order(StringComparer.Ordinal).ToArray();
        DisabledTools = disabledTools.Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<string>? EnabledTools { get; }
    public IReadOnlyList<string> DisabledTools { get; }

    internal bool Allows(string toolName) =>
        (EnabledTools is null || EnabledTools.Contains(toolName, StringComparer.Ordinal))
        && !DisabledTools.Contains(toolName, StringComparer.Ordinal);
}

internal sealed record EffectiveHostConfiguration(
    EffectiveDaemonConfiguration Daemon,
    EffectiveMcpConfiguration Mcp)
{
    internal static EffectiveHostConfiguration Default { get; } = new(
        new EffectiveDaemonConfiguration(
            Enabled: false,
            EnabledSource: HostConfigurationLoader.DefaultSource,
            AutoStart: true,
            AutoStartSource: HostConfigurationLoader.DefaultSource,
            IdleTimeout: TimeSpan.FromMinutes(10),
            IdleTimeoutSource: HostConfigurationLoader.DefaultSource),
        new EffectiveMcpConfiguration(null, []));
}

internal sealed record HostConfigurationResolution(
    string InvocationDirectory,
    string? WorktreeRoot,
    HostConfigurationFileState GlobalFile,
    HostConfigurationFileState RepositoryFile,
    EffectiveHostConfiguration Effective,
    IReadOnlyList<ConfiguredMcpToolName> ConfiguredMcpTools,
    IReadOnlyList<HostConfigurationDiagnostic> Diagnostics)
{
    public bool IsValid => Diagnostics.Count == 0;

    internal static HostConfigurationResolution Default { get; } = new(
        Directory.GetCurrentDirectory(),
        null,
        new HostConfigurationFileState(null, false, true),
        new HostConfigurationFileState(null, false, true),
        EffectiveHostConfiguration.Default,
        [],
        []);
}

/// <summary>Loads strict user and worktree host configuration without changing process state.</summary>
internal sealed class HostConfigurationLoader(
    Func<string, string?>? getEnvironmentVariable = null,
    Func<string, bool>? isGitMarker = null)
{
    internal const string DefaultSource = "default";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly Func<string, string?> _getEnvironmentVariable =
        getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    private readonly Func<string, bool> _isGitMarker = isGitMarker
        ?? (path => Directory.Exists(path) || File.Exists(path));

    internal HostConfigurationResolution ResolveCurrent(string invocationDirectory)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Resolve(invocationDirectory, home);
    }

    internal HostConfigurationResolution Resolve(string invocationDirectory, string userProfileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfileDirectory);

        var invocation = Path.TrimEndingDirectorySeparator(Path.GetFullPath(invocationDirectory));
        var home = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userProfileDirectory));
        var worktreeRoot = FindWorktreeRoot(invocation);
        var globalPath = Path.Combine(home, ".rt", "config.json");
        var repositoryPath = worktreeRoot is null
            ? null
            : Path.Combine(worktreeRoot, ".rt", "config.json");
        var diagnostics = new List<HostConfigurationDiagnostic>();

        var global = Read(globalPath, diagnostics);
        var repository = repositoryPath is null
            ? ConfigurationRead.Absent(null)
            : Read(repositoryPath, diagnostics);
        var effective = Merge(global.Document, repository.Document);
        var configuredMcpTools = CollectConfiguredMcpTools(global.Document, repository.Document);

        return new HostConfigurationResolution(
            invocation,
            worktreeRoot,
            global.State,
            repository.State,
            effective,
            configuredMcpTools,
            diagnostics);
    }

    private static ConfiguredMcpToolName[] CollectConfiguredMcpTools(
        params HostConfigurationDocument?[] documents) => documents
        .Where(document => document?.SourcePath is not null)
        .SelectMany(document => (document!.Mcp?.EnabledTools ?? [])
            .Concat(document.Mcp?.DisabledTools ?? [])
            .Select(name => new ConfiguredMcpToolName(name, document.SourcePath!)))
        .Distinct()
        .OrderBy(reference => reference.Path, StringComparer.Ordinal)
        .ThenBy(reference => reference.Name, StringComparer.Ordinal)
        .ToArray();

    private EffectiveHostConfiguration Merge(
        HostConfigurationDocument? global,
        HostConfigurationDocument? repository)
    {
        var enabled = false;
        var enabledSource = DefaultSource;
        var autoStart = true;
        var autoStartSource = DefaultSource;
        var idleTimeout = TimeSpan.FromMinutes(10);
        var idleTimeoutSource = DefaultSource;
        IReadOnlySet<string>? enabledTools = null;
        var disabledTools = new HashSet<string>(StringComparer.Ordinal);

        Overlay(global, "global");
        Overlay(repository, "repository");

        if (TryParseBoolean(_getEnvironmentVariable(HostConfigurationEnvironment.DaemonEnabled), out var environmentEnabled))
        {
            enabled = environmentEnabled;
            enabledSource = $"environment:{HostConfigurationEnvironment.DaemonEnabled}";
        }

        if (TryParseBoolean(_getEnvironmentVariable(HostConfigurationEnvironment.DaemonAutoStart), out var environmentAutoStart))
        {
            autoStart = environmentAutoStart;
            autoStartSource = $"environment:{HostConfigurationEnvironment.DaemonAutoStart}";
        }

        if (TryParseSeconds(
                _getEnvironmentVariable(HostConfigurationEnvironment.DaemonIdleTimeoutSeconds),
                out var environmentIdleTimeout))
        {
            idleTimeout = environmentIdleTimeout;
            idleTimeoutSource = $"environment:{HostConfigurationEnvironment.DaemonIdleTimeoutSeconds}";
        }

        return new EffectiveHostConfiguration(
            new EffectiveDaemonConfiguration(
                enabled,
                enabledSource,
                autoStart,
                autoStartSource,
                idleTimeout,
                idleTimeoutSource),
            new EffectiveMcpConfiguration(enabledTools, disabledTools));

        void Overlay(HostConfigurationDocument? document, string fallbackSource)
        {
            if (document is null)
                return;

            var source = document.SourcePath ?? fallbackSource;
            if (document.Daemon?.Enabled is { } configuredEnabled)
            {
                enabled = configuredEnabled;
                enabledSource = source;
            }
            if (document.Daemon?.AutoStart is { } configuredAutoStart)
            {
                autoStart = configuredAutoStart;
                autoStartSource = source;
            }
            if (document.Daemon?.IdleTimeoutSeconds is { } configuredIdleTimeout)
            {
                idleTimeout = TimeSpan.FromSeconds(configuredIdleTimeout);
                idleTimeoutSource = source;
            }

            if (document.Mcp?.EnabledTools is { } configuredTools)
            {
                var layer = configuredTools.ToHashSet(StringComparer.Ordinal);
                enabledTools = enabledTools is null
                    ? layer
                    : enabledTools.Intersect(layer, StringComparer.Ordinal)
                        .ToHashSet(StringComparer.Ordinal);
            }
            if (document.Mcp?.DisabledTools is { } configuredDisabled)
                disabledTools.UnionWith(configuredDisabled);
        }
    }

    private static ConfigurationRead Read(
        string path,
        List<HostConfigurationDiagnostic> diagnostics)
    {
        try
        {
            var document = JsonSerializer.Deserialize<HostConfigurationDocument>(
                               File.ReadAllText(path),
                               JsonOptions)
                           ?? throw new JsonException("The configuration document is empty.");
            document.SourcePath = path;
            Validate(document);
            return new ConfigurationRead(
                document,
                new HostConfigurationFileState(path, true, true));
        }
        catch (Exception exception) when (exception is FileNotFoundException
                                          or DirectoryNotFoundException)
        {
            return ConfigurationRead.Absent(path);
        }
        catch (Exception exception) when (exception is JsonException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException)
        {
            var message = $"Invalid rt configuration '{path}': {exception.Message}";
            diagnostics.Add(new HostConfigurationDiagnostic(path, message));
            return new ConfigurationRead(
                null,
                new HostConfigurationFileState(path, true, false, message));
        }
    }

    private static void Validate(HostConfigurationDocument document)
    {
        if (document.SchemaVersion != 1)
            throw new JsonException(
                $"Unsupported schemaVersion '{document.SchemaVersion}'. Expected schemaVersion 1.");
        if (document.Daemon?.IdleTimeoutSeconds is { } seconds
            && !TryCreateIdleTimeout(seconds, out _))
            throw new JsonException(
                "daemon.idleTimeoutSeconds must represent a finite duration greater than zero.");

        ValidateToolNames(document.Mcp?.EnabledTools, "mcp.enabledTools");
        ValidateToolNames(document.Mcp?.DisabledTools, "mcp.disabledTools");
    }

    private static void ValidateToolNames(List<string>? names, string property)
    {
        if (names is null)
            return;
        if (names.Any(string.IsNullOrWhiteSpace))
            throw new JsonException($"{property} entries must be non-empty exact MCP tool names.");
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Count)
            throw new JsonException($"{property} cannot contain duplicate MCP tool names.");
    }

    private string? FindWorktreeRoot(string invocationDirectory)
    {
        for (var current = new DirectoryInfo(invocationDirectory);
             current is not null;
             current = current.Parent)
        {
            var marker = Path.Combine(current.FullName, ".git");
            if (_isGitMarker(marker))
                return Path.TrimEndingDirectorySeparator(current.FullName);
        }

        return null;
    }

    private static bool TryParseBoolean(string? value, out bool result)
    {
        if (value is not null)
        {
            if (value.Equals("1", StringComparison.Ordinal)
                || value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                result = true;
                return true;
            }
            if (value.Equals("0", StringComparison.Ordinal)
                || value.Equals("false", StringComparison.OrdinalIgnoreCase))
            {
                result = false;
                return true;
            }
        }

        result = false;
        return false;
    }

    private static bool TryParseSeconds(string? value, out TimeSpan result)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds)
            && TryCreateIdleTimeout(seconds, out result))
            return true;

        result = default;
        return false;
    }

    private static bool TryCreateIdleTimeout(double seconds, out TimeSpan result)
    {
        if (double.IsFinite(seconds) && seconds > 0)
        {
            try
            {
                result = TimeSpan.FromSeconds(seconds);
                return result > TimeSpan.Zero;
            }
            catch (OverflowException)
            {
                // Values outside TimeSpan's representable range are invalid configuration.
            }
        }

        result = default;
        return false;
    }

    private sealed record ConfigurationRead(
        HostConfigurationDocument? Document,
        HostConfigurationFileState State)
    {
        internal static ConfigurationRead Absent(string? path) =>
            new(null, new HostConfigurationFileState(path, false, true));
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class HostConfigurationDocument
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("daemon")]
        public DaemonDocument? Daemon { get; init; }

        [JsonPropertyName("mcp")]
        public McpDocument? Mcp { get; init; }

        [JsonIgnore]
        public string? SourcePath { get; set; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class DaemonDocument
    {
        [JsonPropertyName("enabled")]
        public bool? Enabled { get; init; }

        [JsonPropertyName("autoStart")]
        public bool? AutoStart { get; init; }

        [JsonPropertyName("idleTimeoutSeconds")]
        public double? IdleTimeoutSeconds { get; init; }
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed class McpDocument
    {
        [JsonPropertyName("enabledTools")]
        public List<string>? EnabledTools { get; init; }

        [JsonPropertyName("disabledTools")]
        public List<string>? DisabledTools { get; init; }
    }
}
