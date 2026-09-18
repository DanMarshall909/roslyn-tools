using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Core;

internal sealed record RtDaemonSettings(
    bool Enabled,
    bool AutoStart,
    TimeSpan IdleTimeout)
{
    internal static RtDaemonSettings Default { get; } = new(
        Enabled: false,
        AutoStart: true,
        TimeSpan.FromMinutes(10));
}

internal sealed class McpToolPolicy
{
    private readonly IReadOnlySet<string>? _enabledTools;
    private readonly IReadOnlySet<string> _disabledTools;

    internal McpToolPolicy(
        IReadOnlySet<string>? enabledTools,
        IReadOnlySet<string> disabledTools,
        IReadOnlyList<string> sourceFiles)
    {
        _enabledTools = enabledTools;
        _disabledTools = disabledTools;
        SourceFiles = sourceFiles;
        ConfiguredTools = (enabledTools ?? new HashSet<string>(StringComparer.Ordinal))
            .Concat(disabledTools)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static McpToolPolicy Unrestricted { get; } = new(
        null,
        new HashSet<string>(StringComparer.Ordinal),
        []);

    internal IReadOnlySet<string> ConfiguredTools { get; }

    internal IReadOnlyList<string> SourceFiles { get; }

    internal IReadOnlySet<string>? EnabledTools => _enabledTools;

    internal IReadOnlySet<string> DisabledTools => _disabledTools;

    internal bool Allows(string toolName) =>
        (_enabledTools is null || _enabledTools.Contains(toolName))
        && !_disabledTools.Contains(toolName);
}

internal sealed record RtConfigurationFile(string? Path, bool Exists);

internal sealed record RtEffectiveConfiguration(
    RtDaemonSettings Daemon,
    McpToolPolicy Mcp,
    RtConfigurationFile GlobalFile,
    RtConfigurationFile RepositoryFile,
    string? RepositoryRoot);

internal sealed record RtConfigurationLoadResult(
    RtEffectiveConfiguration? Configuration,
    string? Error)
{
    internal bool IsSuccess => Configuration is not null;

    internal static RtConfigurationLoadResult Success(RtEffectiveConfiguration configuration) =>
        new(configuration, null);

    internal static RtConfigurationLoadResult Failure(string error) => new(null, error);
}

internal static class RtConfigurationLoader
{
    internal const string FileName = "config.json";
    internal const string DirectoryName = ".rt";
    internal const string DaemonEnabledEnvironmentVariable = "RT_DAEMON_EXPERIMENT";
    internal const string DaemonIdleTimeoutEnvironmentVariable = "RT_DAEMON_IDLE_TIMEOUT_SECONDS";
    private const int CurrentVersion = 1;
    private static readonly TimeSpan MaximumIdleTimeout = TimeSpan.FromDays(1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    internal static RtConfigurationLoadResult LoadDefault() => Load(
        Directory.GetCurrentDirectory(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable);

    internal static RtConfigurationLoadResult Load(
        string invocationDirectory,
        string userProfileDirectory,
        Func<string, string?> getEnvironmentVariable)
    {
        if (string.IsNullOrWhiteSpace(userProfileDirectory))
            return RtConfigurationLoadResult.Failure(
                "The current user's profile directory is unavailable; cannot resolve ~/.rt/config.json.");

        string fullInvocationDirectory;
        string globalPath;
        try
        {
            fullInvocationDirectory = Path.GetFullPath(invocationDirectory);
            globalPath = Path.GetFullPath(Path.Combine(
                userProfileDirectory,
                DirectoryName,
                FileName));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return RtConfigurationLoadResult.Failure(
                $"Configuration paths could not be resolved: {ex.Message}");
        }

        var repositoryRoot = FindRepositoryRoot(fullInvocationDirectory);
        var repositoryPath = repositoryRoot is null
            ? null
            : Path.Combine(repositoryRoot, DirectoryName, FileName);

        var global = Read(globalPath);
        if (global.Error is not null)
            return RtConfigurationLoadResult.Failure(global.Error);

        var repository = repositoryPath is null
            ? ConfigurationRead.Absent(null)
            : Read(repositoryPath);
        if (repository.Error is not null)
            return RtConfigurationLoadResult.Failure(repository.Error);

        var daemon = MergeDaemon(global.Document, repository.Document, getEnvironmentVariable);
        var mcp = MergeMcp(global, repository);
        return RtConfigurationLoadResult.Success(new RtEffectiveConfiguration(
            daemon,
            mcp,
            new RtConfigurationFile(global.Path, global.Exists),
            new RtConfigurationFile(repository.Path, repository.Exists),
            repositoryRoot));
    }

    internal static string? FindRepositoryRoot(string startDirectory)
    {
        for (var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));
             directory is not null;
             directory = directory.Parent)
        {
            var marker = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(marker) || File.Exists(marker))
                return Path.TrimEndingDirectorySeparator(directory.FullName);
        }

        return null;
    }

    private static ConfigurationRead Read(string path)
    {
        if (!File.Exists(path))
            return ConfigurationRead.Absent(path);

        try
        {
            var document = JsonSerializer.Deserialize<RtConfigurationDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document is null)
                return ConfigurationRead.Invalid(path, "the file contains no JSON object");

            var validation = Validate(document);
            return validation is null
                ? new ConfigurationRead(path, true, document, null)
                : ConfigurationRead.Invalid(path, validation);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return ConfigurationRead.Invalid(path, ex.Message);
        }
    }

    private static string? Validate(RtConfigurationDocument document)
    {
        if (document.Version != CurrentVersion)
            return $"version must be {CurrentVersion}";

        if (document.Daemon?.IdleTimeoutSeconds is { } seconds
            && (seconds <= 0 || seconds > MaximumIdleTimeout.TotalSeconds))
        {
            return $"daemon.idleTimeoutSeconds must be greater than 0 and no more than {(int)MaximumIdleTimeout.TotalSeconds}";
        }

        return ValidateTools(document.Mcp?.EnabledTools, "mcp.enabledTools")
               ?? ValidateTools(document.Mcp?.DisabledTools, "mcp.disabledTools");
    }

    private static string? ValidateTools(string[]? tools, string property)
    {
        if (tools is null)
            return null;
        if (tools.Any(string.IsNullOrWhiteSpace))
            return $"{property} must contain non-empty exact MCP tool names";
        if (tools.Distinct(StringComparer.Ordinal).Count() != tools.Length)
            return $"{property} must not contain duplicate MCP tool names";
        return null;
    }

    private static RtDaemonSettings MergeDaemon(
        RtConfigurationDocument? global,
        RtConfigurationDocument? repository,
        Func<string, string?> getEnvironmentVariable)
    {
        var enabled = repository?.Daemon?.Enabled
                      ?? global?.Daemon?.Enabled
                      ?? RtDaemonSettings.Default.Enabled;
        var autoStart = repository?.Daemon?.AutoStart
                        ?? global?.Daemon?.AutoStart
                        ?? RtDaemonSettings.Default.AutoStart;
        var idleTimeout = TimeSpan.FromSeconds(
            repository?.Daemon?.IdleTimeoutSeconds
            ?? global?.Daemon?.IdleTimeoutSeconds
            ?? RtDaemonSettings.Default.IdleTimeout.TotalSeconds);

        var enabledEnvironment = getEnvironmentVariable(DaemonEnabledEnvironmentVariable);
        if (enabledEnvironment is not null)
            enabled = enabledEnvironment is "1" or "true";

        var idleEnvironment = getEnvironmentVariable(DaemonIdleTimeoutEnvironmentVariable);
        if (idleEnvironment is not null)
        {
            idleTimeout = double.TryParse(
                              idleEnvironment,
                              NumberStyles.Float,
                              CultureInfo.InvariantCulture,
                              out var seconds)
                          && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : RtDaemonSettings.Default.IdleTimeout;
        }

        return new RtDaemonSettings(enabled, autoStart, idleTimeout);
    }

    private static McpToolPolicy MergeMcp(
        ConfigurationRead global,
        ConfigurationRead repository)
    {
        HashSet<string>? enabled = null;
        var disabled = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in new[] { global.Document, repository.Document })
        {
            if (document?.Mcp?.EnabledTools is { } layerEnabled)
            {
                if (enabled is null)
                    enabled = layerEnabled.ToHashSet(StringComparer.Ordinal);
                else
                    enabled.IntersectWith(layerEnabled);
            }

            if (document?.Mcp?.DisabledTools is { } layerDisabled)
                disabled.UnionWith(layerDisabled);
        }

        var sources = new[] { global, repository }
            .Where(read => read.Exists && read.Document?.Mcp is not null)
            .Select(read => read.Path!)
            .ToArray();
        return new McpToolPolicy(enabled, disabled, sources);
    }

    private sealed record ConfigurationRead(
        string? Path,
        bool Exists,
        RtConfigurationDocument? Document,
        string? Error)
    {
        internal static ConfigurationRead Absent(string? path) => new(path, false, null, null);

        internal static ConfigurationRead Invalid(string path, string reason) =>
            new(path, true, null, $"Invalid rt configuration '{path}': {reason}");
    }

    private sealed class RtConfigurationDocument
    {
        public int? Version { get; init; }
        public RtDaemonDocument? Daemon { get; init; }
        public RtMcpDocument? Mcp { get; init; }
    }

    private sealed class RtDaemonDocument
    {
        public bool? Enabled { get; init; }
        public bool? AutoStart { get; init; }
        public double? IdleTimeoutSeconds { get; init; }
    }

    private sealed class RtMcpDocument
    {
        public string[]? EnabledTools { get; init; }
        public string[]? DisabledTools { get; init; }
    }
}
