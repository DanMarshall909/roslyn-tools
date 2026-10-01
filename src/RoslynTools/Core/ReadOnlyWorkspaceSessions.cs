using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Core;

/// <summary>
///     Provides immutable semantic snapshots for read-only operations. Mutating operations
///     deliberately continue to depend on <see cref="IWorkspaceSessionFactory" />.
/// </summary>
internal interface IReadOnlyWorkspaceSessions
{
    Task<ReadOnlyWorkspaceLease> AcquireAsync(
        string? solutionPath,
        CancellationToken cancellationToken = default);
}

internal interface IWorkspaceSnapshotLoader
{
    Task<OwnedWorkspaceSnapshot> LoadOwnedWorkspaceAsync(
        string? solutionPath,
        CancellationToken cancellationToken = default);
}

internal interface IWorkspaceLoadConfiguration
{
    string? BaseIntermediateOutputPath { get; }
}

internal sealed record OwnedWorkspaceSnapshot(IDisposable Owner, Solution Solution);

internal sealed class ReadOnlyWorkspaceLease : IDisposable
{
    private Action? _release;
    private WorkspaceCompleteness? _completeness;

    internal ReadOnlyWorkspaceLease(Solution solution, string sourceRevision, Action? release = null)
    {
        Solution = solution;
        SourceRevision = sourceRevision;
        _release = release;
    }

    internal ReadOnlyWorkspaceLease(
        Solution solution,
        string sourceRevision,
        WorkspaceCompleteness completeness,
        Action? release = null)
        : this(solution, sourceRevision, release)
    {
        _completeness = completeness;
    }

    internal Solution Solution { get; }

    internal string SourceRevision { get; }

    internal WorkspaceCompleteness Completeness => _completeness
        ?? throw new InvalidOperationException("The workspace lease crossed the application boundary without completeness evidence.");

    internal void AttachCompleteness(WorkspaceCompleteness completeness)
    {
        ArgumentNullException.ThrowIfNull(completeness);
        if (Interlocked.CompareExchange(ref _completeness, completeness, null) is not null)
            throw new InvalidOperationException("Workspace completeness evidence is immutable for a lease.");
    }

    /// <summary>
    ///     Releases this query lease. Disposal is idempotent; a retired workspace is disposed
    ///     only after its final active lease is released.
    /// </summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}

internal interface IWorkspaceInputFingerprint
{
    Task<WorkspaceInputFingerprintResult> CaptureAsync(
        TrustedWorkspaceIdentity identity,
        Solution? solution,
        CancellationToken cancellationToken);

    string CaptureEnvironmentIdentity();
}

internal readonly record struct WorkspaceInputFingerprintResult(
    string Value,
    bool Reusable = true,
    bool SnapshotConsistent = true);

internal interface IWorkspaceEnvironmentIdentity
{
    string Capture();
}

internal sealed class WorkspaceEnvironmentIdentity(Func<string?> msBuildIdentity)
    : IWorkspaceEnvironmentIdentity
{
    string IWorkspaceEnvironmentIdentity.Capture() => msBuildIdentity() ?? "unregistered";
}

internal enum WorkspaceCacheEventKind
{
    Load,
    Hit,
    Refresh,
    Evict,
    Dispose
}

internal sealed record WorkspaceCacheEvent(
    WorkspaceCacheEventKind Kind,
    string IdentityHash,
    string? SourceRevision,
    long LoadCount);

internal interface IWorkspaceCacheTelemetry
{
    void Record(WorkspaceCacheEvent cacheEvent);
}

internal sealed class WorkspaceCacheTelemetry : IWorkspaceCacheTelemetry
{
    internal const string TracePathEnvironmentVariable = "RT_WORKSPACE_CACHE_TRACE";

    private static readonly object TraceLock = new();
    private static readonly JsonSerializerOptions TraceOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Func<string?> _getTracePath;
    private readonly Action<string, string> _appendLine;
    private readonly Action<Exception> _reportFailure;
    private readonly Action<WorkspaceCacheEvent> _reportEvent;

    internal WorkspaceCacheTelemetry(
        Action<WorkspaceCacheEvent> reportEvent,
        Action<Exception> reportFailure)
        : this(
            () => Environment.GetEnvironmentVariable(TracePathEnvironmentVariable),
            static (path, line) => File.AppendAllText(path, line, new UTF8Encoding(false)),
            reportFailure,
            reportEvent)
    {
    }

    internal WorkspaceCacheTelemetry(
        Func<string?> getTracePath,
        Action<string, string> appendLine,
        Action<Exception>? reportFailure = null,
        Action<WorkspaceCacheEvent>? reportEvent = null)
    {
        _getTracePath = getTracePath;
        _appendLine = appendLine;
        _reportFailure = reportFailure ?? (static _ => { });
        _reportEvent = reportEvent ?? (static _ => { });
    }

    void IWorkspaceCacheTelemetry.Record(WorkspaceCacheEvent cacheEvent)
    {
        _reportEvent(cacheEvent);

        var tracePath = _getTracePath();
        if (string.IsNullOrWhiteSpace(tracePath))
            return;

        try
        {
            var line = JsonSerializer.Serialize(cacheEvent, TraceOptions) + Environment.NewLine;
            lock (TraceLock)
                _appendLine(tracePath, line);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or ArgumentException
                                          or NotSupportedException)
        {
            _reportFailure(exception);
        }
    }
}

internal sealed class WorkspaceInputFingerprint : IWorkspaceInputFingerprint
{
    private static readonly Regex ExistsConditionRegex = new(
        "Exists\\s*\\(\\s*(['\\\"])(?<path>.*?)\\1\\s*\\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex MsBuildEscapeRegex = new(
        "%[0-9a-f]{2}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> SupportedBuiltInSdks = new(
        [
            "Microsoft.NET.Sdk",
            "Microsoft.NET.Sdk.BlazorWebAssembly",
            "Microsoft.NET.Sdk.Razor",
            "Microsoft.NET.Sdk.Web",
            "Microsoft.NET.Sdk.WindowsDesktop",
            "Microsoft.NET.Sdk.Worker"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] NonReusableEnvironmentOverrides =
    [
        "AllowedReferenceAssemblyFileExtensions",
        "AssemblyOriginatorKeyFile",
        "ArtifactsPath",
        "AssemblySearchPaths",
        "BaseIntermediateOutputPath",
        "BaseOutputPath",
        "CustomAfterDirectoryBuildProps",
        "CustomAfterDirectoryBuildTargets",
        "CustomAfterMicrosoftCommonProps",
        "CustomAfterMicrosoftCommonTargets",
        "CustomAfterMicrosoftCSharpTargets",
        "CustomAfterMicrosoftVisualBasicTargets",
        "CustomBeforeDirectoryBuildProps",
        "CustomBeforeDirectoryBuildTargets",
        "CustomBeforeMicrosoftCommonProps",
        "CustomBeforeMicrosoftCommonTargets",
        "CustomBeforeMicrosoftCSharpTargets",
        "CustomBeforeMicrosoftVisualBasicTargets",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_CEILING_DIRECTORIES",
        "GIT_COMMON_DIR",
        "GIT_CONFIG_COUNT",
        "GIT_CONFIG_GLOBAL",
        "GIT_CONFIG_NOSYSTEM",
        "GIT_CONFIG_PARAMETERS",
        "GIT_CONFIG_SYSTEM",
        "GIT_DIR",
        "GIT_DISCOVERY_ACROSS_FILESYSTEM",
        "GIT_NAMESPACE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_WORK_TREE",
        "CodeAnalysisRuleSet",
        "CodeAnalysisRuleSetDirectories",
        "CoreCompileDependsOn",
        "CryptoKeyContainer",
        "CryptoKeyFile",
        "DelaySign",
        "DefaultExcludesInProjectFolder",
        "DefaultItemExcludes",
        "DesignTimeAssemblySearchPaths",
        "DirectoryBuildPropsPath",
        "DirectoryBuildTargetsPath",
        "EnableDefaultCompileItems",
        "EnableDefaultItems",
        "IncludeProjectNameInArtifactsPaths",
        "KeyContainerName",
        "KeyOriginatorFile",
        "MicrosoftBuildTasksGitAssemblyFile",
        "MicrosoftNETBuildExtensionsTasksAssembly",
        "MSBuildProjectExtensionsPath",
        "OutputPath",
        "ProjectAssetsFile",
        "PublicSign",
        "ResolveAssemblyConflicts",
        "ResolvedCodeAnalysisRuleSet",
        "SignAssembly",
        "TargetFrameworkIdentifier",
        "UseArtifactsIntermediateOutput",
        "UseArtifactsOutput",
        "UsingMicrosoftNETSdk",
        "VSTestTaskAssemblyFile"
    ];

    private static readonly string[] EnvironmentVariables =
    [
        "Configuration",
        "ContinuousIntegrationBuild",
        "DOTNET_HOST_PATH",
        "DOTNET_ROOT",
        "HOME",
        "ImportByWildcardAfterMicrosoftCSharpTargets",
        "ImportByWildcardAfterMicrosoftCommonCrossTargetingTargets",
        "ImportByWildcardAfterMicrosoftCommonProps",
        "ImportByWildcardAfterMicrosoftCommonTargets",
        "ImportByWildcardAfterMicrosoftNETFrameworkProps",
        "ImportByWildcardAfterMicrosoftNETFrameworkTargets",
        "ImportByWildcardAfterMicrosoftVisualBasicTargets",
        "ImportByWildcardBeforeMicrosoftCSharpTargets",
        "ImportByWildcardBeforeMicrosoftCommonCrossTargetingTargets",
        "ImportByWildcardBeforeMicrosoftCommonProps",
        "ImportByWildcardBeforeMicrosoftCommonTargets",
        "ImportByWildcardBeforeMicrosoftNETFrameworkProps",
        "ImportByWildcardBeforeMicrosoftNETFrameworkTargets",
        "ImportByWildcardBeforeMicrosoftVisualBasicTargets",
        "ImportUserLocationsByWildcardAfterMicrosoftCSharpTargets",
        "ImportUserLocationsByWildcardAfterMicrosoftCommonProps",
        "ImportUserLocationsByWildcardAfterMicrosoftCommonTargets",
        "ImportUserLocationsByWildcardAfterMicrosoftNETFrameworkProps",
        "ImportUserLocationsByWildcardAfterMicrosoftNETFrameworkTargets",
        "ImportUserLocationsByWildcardAfterMicrosoftVisualBasicTargets",
        "ImportUserLocationsByWildcardBeforeMicrosoftCSharpTargets",
        "ImportUserLocationsByWildcardBeforeMicrosoftCommonProps",
        "ImportUserLocationsByWildcardBeforeMicrosoftCommonTargets",
        "ImportUserLocationsByWildcardBeforeMicrosoftNETFrameworkProps",
        "ImportUserLocationsByWildcardBeforeMicrosoftNETFrameworkTargets",
        "ImportUserLocationsByWildcardBeforeMicrosoftVisualBasicTargets",
        "MSBuildExtensionsPath",
        "MSBuildSDKsPath",
        "MSBuildToolsVersion",
        "MSBuildUserExtensionsPath",
        "MicrosoftNETBuildExtensionsTargets",
        "MICROSOFT_SOURCELINK_TEST_ENVIRONMENT_ETC_DIR",
        "GenerateAssemblyInfo",
        "GenerateAssemblyInformationalVersionAttribute",
        "IncludeSourceRevisionInInformationalVersion",
        "InformationalVersion",
        "NUGET_PACKAGES",
        "NuGetPackageRoot",
        "Platform",
        "PROGRAMDATA",
        "PATH",
        "RT_ISOLATED_OBJ",
        "RuntimeIdentifier",
        "SourceRevisionId",
        "TargetFramework",
        "TargetFrameworks",
        "USERPROFILE",
        "Version",
        "VersionPrefix",
        "VersionSuffix",
        "VSTestTargets",
        "XDG_CONFIG_HOME",
        .. NonReusableEnvironmentOverrides
    ];

    private static readonly HashSet<string> RelevantExtensions = new(
        [
            ".cs",
            ".cshtml",
            ".csx",
            ".razor",
            ".xaml",
            ".sln",
            ".slnx",
            ".csproj",
            ".vbproj",
            ".fsproj",
            ".props",
            ".targets",
            ".json",
            ".editorconfig",
            ".globalconfig",
            ".ruleset",
            ".config",
            ".dll",
            ".exe",
            ".winmd"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly string[] MsBuildWildcardHookFamilies =
    [
        Path.Combine("Imports", "Microsoft.Common.props"),
        "Microsoft.Common.CrossTargeting.targets",
        "Microsoft.Common.targets",
        "Microsoft.CSharp.targets",
        "Microsoft.VisualBasic.targets",
        "Microsoft.NETFramework.props",
        "Microsoft.NETFramework.targets"
    ];

    private static readonly string[] MsBuildWildcardHookPhases =
    [
        "ImportBefore",
        "ImportAfter"
    ];

    private static readonly HashSet<string> IgnoredDirectories = new(
        [
            ".git",
            ".hg",
            ".svn"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly IWorkspaceEnvironmentIdentity _environmentIdentity;
    private readonly IWorkspaceLoadConfiguration? _loadConfiguration;
    private readonly Func<IEnumerable<KeyValuePair<string, string?>>> _enumerateEnvironmentVariables;
    private readonly Func<string?> _getRegisteredMsBuildPath;
    private readonly Func<string?> _getUserProfileDirectory;
    private readonly object _systemHookBaselineLock = new();
    private MsBuildSystemHookBaseline _systemHookBaseline;

    internal WorkspaceInputFingerprint(IWorkspaceEnvironmentIdentity environmentIdentity)
        : this(
            environmentIdentity,
            loadConfiguration: null,
            Environment.GetEnvironmentVariable,
            getRegisteredMsBuildPath: static () => null,
            EnumerateCurrentEnvironmentVariables)
    {
    }

    internal WorkspaceInputFingerprint(
        IWorkspaceEnvironmentIdentity environmentIdentity,
        Func<string, string?> getEnvironmentVariable)
        : this(
            environmentIdentity,
            loadConfiguration: null,
            getEnvironmentVariable,
            getRegisteredMsBuildPath: static () => null,
            enumerateEnvironmentVariables: null)
    {
    }

    internal WorkspaceInputFingerprint(
        IWorkspaceEnvironmentIdentity environmentIdentity,
        IWorkspaceLoadConfiguration loadConfiguration)
        : this(
            environmentIdentity,
            loadConfiguration,
            Environment.GetEnvironmentVariable,
            getRegisteredMsBuildPath: static () => null,
            EnumerateCurrentEnvironmentVariables)
    {
    }

    internal WorkspaceInputFingerprint(
        IWorkspaceEnvironmentIdentity environmentIdentity,
        IWorkspaceLoadConfiguration loadConfiguration,
        Func<string?> getRegisteredMsBuildPath)
        : this(
            environmentIdentity,
            loadConfiguration,
            Environment.GetEnvironmentVariable,
            getRegisteredMsBuildPath,
            EnumerateCurrentEnvironmentVariables)
    {
    }

    internal WorkspaceInputFingerprint(
        IWorkspaceEnvironmentIdentity environmentIdentity,
        IWorkspaceLoadConfiguration? loadConfiguration,
        Func<string, string?> getEnvironmentVariable,
        Func<string?> getRegisteredMsBuildPath,
        Func<IEnumerable<KeyValuePair<string, string?>>>? enumerateEnvironmentVariables = null,
        Func<string?>? getUserProfileDirectory = null)
    {
        _environmentIdentity = environmentIdentity;
        _loadConfiguration = loadConfiguration;
        _enumerateEnvironmentVariables = enumerateEnvironmentVariables
                                         ?? (() => EnvironmentVariables.Select(variable =>
                                             new KeyValuePair<string, string?>(
                                                 variable,
                                                 getEnvironmentVariable(variable))));
        _getRegisteredMsBuildPath = getRegisteredMsBuildPath;
        _getUserProfileDirectory = getUserProfileDirectory ?? GetUserProfileDirectory;
        _systemHookBaseline = CaptureSystemHookBaseline();
    }

    internal static WorkspaceInputFingerprint CreateForAcquisition(string sdkIdentity)
    {
        var registeredPath = TryGetRegisteredPath(sdkIdentity);
        return new WorkspaceInputFingerprint(
            new WorkspaceEnvironmentIdentity(() => sdkIdentity),
            loadConfiguration: null,
            Environment.GetEnvironmentVariable,
            () => registeredPath,
            EnumerateCurrentEnvironmentVariables);

        static string? TryGetRegisteredPath(string identity)
        {
            var separator = identity.LastIndexOf('@');
            if (separator < 0 || separator == identity.Length - 1)
                return null;
            var sdkPath = identity[(separator + 1)..];
            return Path.GetFileName(sdkPath).Equals("Sdks", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(sdkPath)
                : sdkPath;
        }
    }

    internal static WorkspaceInputFingerprint CreateForDiscovery()
    {
        var toolsPath = Microsoft.Build.Evaluation.ProjectCollection.GlobalProjectCollection.Toolsets
            .Select(toolset => toolset.ToolsPath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        var sdkPath = toolsPath is null ? null : Path.Combine(toolsPath, "Sdks");
        return CreateForAcquisition(sdkPath is null ? "registered-msbuild" : $"registered-msbuild@{sdkPath}");
    }

    internal Task<WorkspaceInputFingerprintResult> CaptureAsync(
        TrustedWorkspaceIdentity identity,
        Solution? solution,
        CancellationToken cancellationToken)
        => ((IWorkspaceInputFingerprint)this).CaptureAsync(identity, solution, cancellationToken);

    internal Task<WorkspaceInputFingerprintResult> CaptureRepositoryAsync(
        TrustedWorkspaceIdentity identity,
        IEnumerable<string> projectPaths,
        CancellationToken cancellationToken)
        => CaptureCoreAsync(identity, solution: null, projectPaths, cancellationToken);

    private static string? GetUserProfileDirectory()
    {
        try
        {
            return Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolderOption.DoNotVerify);
        }
        catch
        {
            return null;
        }
    }

    string IWorkspaceInputFingerprint.CaptureEnvironmentIdentity() => CaptureEnvironmentIdentity();

    private string CaptureEnvironmentIdentity()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "rt.workspace.environment.v1");
        Append(hash, Environment.Version.ToString());
        Append(hash, RuntimeInformation.FrameworkDescription);
        Append(hash, RuntimeInformation.OSDescription);
        Append(hash, typeof(Workspace).Assembly.FullName);
        Append(hash, typeof(Workspace).Assembly.Location);
        Append(hash, typeof(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace).Assembly.FullName);
        Append(hash, typeof(Microsoft.CodeAnalysis.MSBuild.MSBuildWorkspace).Assembly.Location);
        Append(hash, _environmentIdentity.Capture());
        foreach (var variable in EnvironmentVariables)
        {
            Append(hash, variable);
            var matches = ReadEnvironmentPropertyEntries(variable);
            Append(hash, matches.Length.ToString(CultureInfo.InvariantCulture));
            foreach (var match in matches)
            {
                Append(hash, match.Key);
                Append(hash, match.Value);
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static IEnumerable<KeyValuePair<string, string?>> EnumerateCurrentEnvironmentVariables()
    {
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name)
            {
                yield return new KeyValuePair<string, string?>(
                    name,
                    entry.Value?.ToString());
            }
        }
    }

    private KeyValuePair<string, string?>[] ReadEnvironmentPropertyEntries(
        string propertyName) =>
        _enumerateEnvironmentVariables()
            .Where(entry => entry.Key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ThenBy(entry => entry.Value, StringComparer.Ordinal)
            .ToArray();

    private string? GetEnvironmentProperty(string propertyName)
    {
        var matches = ReadEnvironmentPropertyEntries(propertyName);
        return matches.Length == 1 ? matches[0].Value : null;
    }

    private bool HasAmbiguousEnvironmentProperties() =>
        EnvironmentVariables.Any(variable => ReadEnvironmentPropertyEntries(variable).Length > 1);

    async Task<WorkspaceInputFingerprintResult> IWorkspaceInputFingerprint.CaptureAsync(
        TrustedWorkspaceIdentity identity,
        Solution? solution,
        CancellationToken cancellationToken)
        => await CaptureCoreAsync(identity, solution, additionalProjectPaths: null, cancellationToken);

    private async Task<WorkspaceInputFingerprintResult> CaptureCoreAsync(
        TrustedWorkspaceIdentity identity,
        Solution? solution,
        IEnumerable<string>? additionalProjectPaths,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "rt.workspace.inputs.v1");
        Append(hash, identity.RepositoryId);
        Append(hash, identity.WorktreeRoot);
        Append(hash, identity.SolutionPath);
        Append(hash, CaptureEnvironmentIdentity());

        var discovery = await DiscoverSemanticInputsAsync(
            identity,
            solution,
            additionalProjectPaths,
            cancellationToken);
        Append(hash, discovery.Reusable ? "reusable" : "non-reusable");

        foreach (var path in discovery.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, path);
            if (File.Exists(path))
            {
                Append(hash, "file");
                await AppendStableFileAsync(hash, path, cancellationToken);
                continue;
            }

            Append(hash, Directory.Exists(path) ? "directory" : "missing");
        }

        foreach (var value in discovery.Values ?? Array.Empty<string>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, "semantic-value");
            Append(hash, value);
        }

        var snapshotConsistent = solution is null
            || await IsLoadedSolutionConsistentAsync(solution, cancellationToken);
        return new WorkspaceInputFingerprintResult(
            Convert.ToHexString(hash.GetHashAndReset()),
            discovery.Reusable,
            snapshotConsistent);
    }

    private async Task<SemanticInputDiscovery> DiscoverSemanticInputsAsync(
        TrustedWorkspaceIdentity identity,
        Solution? solution,
        IEnumerable<string>? additionalProjectPaths,
        CancellationToken cancellationToken)
    {
        var solutionProjects = ReadDeclaredSolutionProjects(identity.SolutionPath);
        var projectPaths = solutionProjects.Paths
            .Concat(additionalProjectPaths ?? [])
            .Concat(solution?.Projects
                .Select(project => project.FilePath)
                .Where(path => path is not null)
                .Cast<string>() ?? [])
            .Where(IsProjectInput)
            .Distinct(PathComparer)
            .ToArray();
        var defaultOutputDirectories = ReadDefaultProjectOutputDirectories(projectPaths);
        var paths = new HashSet<string>(PathComparer);
        foreach (var path in EnumerateRelevantRepositoryFiles(
                         identity.WorktreeRoot,
                         defaultOutputDirectories)
                     .Concat(solution is null ? [] : EnumerateSolutionInputs(solution))
                     .Append(identity.SolutionPath))
        {
            var fullPath = Path.GetFullPath(path);
            paths.Add(fullPath);
        }

        paths.UnionWith(solutionProjects.Paths);
        var gitInputs = await ReadGitSemanticInputsAsync(
            identity.WorktreeRoot,
            projectPaths.Select(Path.GetDirectoryName).Where(path => path is not null).Cast<string>(),
            cancellationToken);
        var hasUnprovenLoadedSigningInputs = await HasUnprovenLoadedSigningInputsAsync(
            solution,
            cancellationToken);
        var reusable = solutionProjects.Reusable
                       && gitInputs.Reusable
                       && !hasUnprovenLoadedSigningInputs
                       && !HasExistingProjectOutsideWorktree(identity, solution)
                       && !HasAmbiguousEnvironmentProperties()
                       && NonReusableEnvironmentOverrides.All(variable =>
                           string.IsNullOrWhiteSpace(GetEnvironmentProperty(variable)));
        foreach (var projectPath in projectPaths)
        {
            var implicitInputs = ReadImplicitRestoreInputs(projectPath);
            paths.UnionWith(implicitInputs.Paths);
            reusable &= implicitInputs.Reusable;
        }

        foreach (var startDirectory in paths
                     .Select(Path.GetDirectoryName)
                     .Where(directory => directory is not null)
                     .Distinct(PathComparer)
                     .ToArray())
        {
            AddAncestorConfiguration(paths, startDirectory!);
        }
        paths.UnionWith(gitInputs.Paths);

        var opaqueBuildInputs = new HashSet<string>(PathComparer);
        var environmentTargetFrameworks = GetEnvironmentProperty("TargetFrameworks")
                                          ?? GetEnvironmentProperty("TargetFramework");
        var canSkipLegacySdkExtensionImports = CanSkipLegacySdkExtensionImports(
            projectPaths,
            paths.Where(IsBuildInput).ToHashSet(PathComparer))
                                                && (string.IsNullOrWhiteSpace(
                                                        environmentTargetFrameworks)
                                                    || AreDefinitelyModernTargetFrameworks(
                                                        environmentTargetFrameworks));
        reusable &= AddDefaultMsBuildWildcardHookInputs(
            paths,
            opaqueBuildInputs,
            canSkipLegacySdkExtensionImports);

        var buildInputs = new Queue<string>();
        foreach (var path in paths.Where(path => IsBuildInput(path)
                                                 && !opaqueBuildInputs.Contains(path)))
        {
            if (ShouldDiscoverDeclaredInputs(path))
                buildInputs.Enqueue(path);
            else if (File.Exists(path))
                reusable = false;
        }

        var parsedBuildInputs = new HashSet<string>(PathComparer);
        while (buildInputs.TryDequeue(out var buildInput))
        {
            if (!File.Exists(buildInput) || !parsedBuildInputs.Add(buildInput))
                continue;

            var references = ReadDeclaredBuildInputs(buildInput);
            reusable &= references.Reusable;
            foreach (var referencedPath in references.Paths)
            {
                var added = paths.Add(referencedPath);
                if (!IsBuildInput(referencedPath))
                    continue;
                if (!ShouldDiscoverDeclaredInputs(referencedPath))
                {
                    if (File.Exists(referencedPath))
                        reusable = false;
                    continue;
                }

                if (added)
                    buildInputs.Enqueue(referencedPath);
            }
        }

        return new SemanticInputDiscovery(
            paths.OrderBy(path => path, PathComparer).ToArray(),
            reusable,
            gitInputs.Values);
    }

    private bool AddDefaultMsBuildWildcardHookInputs(
        HashSet<string> paths,
        HashSet<string> opaqueBuildInputs,
        bool canSkipLegacySdkExtensionImports)
    {
        if (!TryGetMsBuildToolsVersion(out var toolsVersion))
            return false;

        if (!TryGetUserExtensionsPath(out var userExtensionsPath))
            return false;

        var reusable = true;
        if (!string.IsNullOrWhiteSpace(GetEnvironmentProperty("VSTestTargets"))
            || !string.IsNullOrWhiteSpace(
                GetEnvironmentProperty("MicrosoftNETBuildExtensionsTargets")))
        {
            reusable = false;
        }

        foreach (var hookDirectory in EnumerateMsBuildHookDirectories(
                     userExtensionsPath,
                     toolsVersion))
        {
            paths.Add(hookDirectory);
            if (!Directory.Exists(hookDirectory))
                continue;

            foreach (var hookPath in Directory.EnumerateFiles(
                         hookDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                var fullPath = Path.GetFullPath(hookPath);
                paths.Add(fullPath);
                reusable &= IsBuildInput(fullPath);
            }
        }

        var systemExtensionsPath = GetSystemExtensionsPath();
        if (systemExtensionsPath is null)
            return reusable && _systemHookBaseline.Reusable;
        if (!TryNormalizeRoot(systemExtensionsPath, out var normalizedSystemRoot))
            return false;

        var systemHookBaseline = GetSystemHookBaseline();
        reusable &= systemHookBaseline.Reusable
                    && PathComparer.Equals(systemHookBaseline.Root, normalizedSystemRoot)
                    && string.Equals(
                        systemHookBaseline.ToolsVersion,
                        toolsVersion,
                        StringComparison.OrdinalIgnoreCase);
        var currentSystemFiles = new HashSet<string>(PathComparer);
        foreach (var hookDirectory in EnumerateMsBuildHookDirectories(
                     normalizedSystemRoot,
                     toolsVersion))
        {
            paths.Add(hookDirectory);
        }
        paths.Add(Path.Combine(normalizedSystemRoot, toolsVersion));
        paths.Add(Path.Combine(normalizedSystemRoot, $"v{toolsVersion}"));
        currentSystemFiles.UnionWith(EnumerateMsBuildSystemHookFiles(
            normalizedSystemRoot,
            toolsVersion));
        paths.UnionWith(currentSystemFiles);
        opaqueBuildInputs.UnionWith(currentSystemFiles);
        var pendingSystemInputs = new Queue<string>();

        foreach (var baselineFile in systemHookBaseline.FileHashes)
        {
            paths.Add(baselineFile.Key);
            opaqueBuildInputs.Add(baselineFile.Key);
            if (!currentSystemFiles.Contains(baselineFile.Key)
                || !TryCaptureFileHash(baselineFile.Key, out var currentHash)
                || !string.Equals(currentHash, baselineFile.Value, StringComparison.Ordinal))
            {
                reusable = false;
            }

            if (File.Exists(baselineFile.Key))
            {
                var dependencies = ReadSystemHookInputs(
                    baselineFile.Key,
                    normalizedSystemRoot,
                    canSkipLegacySdkExtensionImports);
                paths.UnionWith(dependencies.Paths);
                reusable &= dependencies.Reusable;
                EnqueueBuildInputs(dependencies);
            }
        }

        var parsedSystemInputs = new HashSet<string>(PathComparer);
        while (pendingSystemInputs.TryDequeue(out var pending))
        {
            paths.Add(pending);
            opaqueBuildInputs.Add(pending);
            if (!File.Exists(pending)
                || !parsedSystemInputs.Add(pending))
            {
                continue;
            }

            var dependencies = ReadSystemHookInputs(
                pending,
                normalizedSystemRoot,
                canSkipLegacySdkExtensionImports);
            paths.UnionWith(dependencies.Paths);
            reusable &= dependencies.Reusable;
            EnqueueBuildInputs(dependencies);
        }

        if (currentSystemFiles.Any(path =>
                !systemHookBaseline.FileHashes.ContainsKey(path)))
        {
            reusable = false;
        }

        return reusable;

        void EnqueueBuildInputs(SystemHookInputs dependencies)
        {
            foreach (var path in dependencies.Paths.Where(IsBuildInput))
                pendingSystemInputs.Enqueue(path);
        }
    }

    private MsBuildSystemHookBaseline GetSystemHookBaseline()
    {
        var baseline = _systemHookBaseline;
        if (baseline.Root is not null
            || !baseline.Reusable
            || GetSystemExtensionsPath() is null)
        {
            return baseline;
        }

        lock (_systemHookBaselineLock)
        {
            if (_systemHookBaseline.Root is null && _systemHookBaseline.Reusable)
                _systemHookBaseline = CaptureSystemHookBaseline();
            return _systemHookBaseline;
        }
    }

    private MsBuildSystemHookBaseline CaptureSystemHookBaseline()
    {
        var systemExtensionsPath = GetSystemExtensionsPath();
        if (systemExtensionsPath is null)
            return new MsBuildSystemHookBaseline(
                null,
                "Current",
                new Dictionary<string, string>(PathComparer),
                true);
        if (!TryNormalizeRoot(systemExtensionsPath, out var normalizedRoot)
            || !TryGetMsBuildToolsVersion(out var toolsVersion))
        {
            return new MsBuildSystemHookBaseline(
                null,
                "Current",
                new Dictionary<string, string>(PathComparer),
                false);
        }

        var hashes = new Dictionary<string, string>(PathComparer);
        try
        {
            foreach (var hookPath in EnumerateMsBuildSystemHookFiles(
                         normalizedRoot,
                         toolsVersion))
            {
                var fullPath = Path.GetFullPath(hookPath);
                if (!TryCaptureFileHash(fullPath, out var hash))
                {
                    return new MsBuildSystemHookBaseline(
                        null,
                        toolsVersion,
                        new Dictionary<string, string>(PathComparer),
                        false);
                }
                hashes[fullPath] = hash;
            }

            return new MsBuildSystemHookBaseline(
                normalizedRoot,
                toolsVersion,
                hashes,
                true);
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            return new MsBuildSystemHookBaseline(
                null,
                toolsVersion,
                new Dictionary<string, string>(PathComparer),
                false);
        }
    }

    private string? GetSystemExtensionsPath() =>
        GetEnvironmentProperty("MSBuildExtensionsPath") ?? _getRegisteredMsBuildPath();

    private bool TryGetUserExtensionsPath(out string path)
    {
        var configured = GetEnvironmentProperty("MSBuildUserExtensionsPath");
        if (string.IsNullOrWhiteSpace(configured))
        {
            var localApplicationData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localApplicationData))
            {
                path = string.Empty;
                return false;
            }

            configured = Path.Combine(localApplicationData, "Microsoft", "MSBuild");
        }

        return TryNormalizeRoot(configured, out path);
    }

    private bool TryGetMsBuildToolsVersion(out string toolsVersion)
    {
        toolsVersion = GetEnvironmentProperty("MSBuildToolsVersion") ?? "Current";
        return !string.IsNullOrWhiteSpace(toolsVersion)
               && toolsVersion.IndexOfAny(
                   [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) < 0;
    }

    private static IEnumerable<string> EnumerateMsBuildHookDirectories(
        string extensionRoot,
        string toolsVersion)
    {
        foreach (var family in MsBuildWildcardHookFamilies)
        {
            foreach (var phase in MsBuildWildcardHookPhases)
            {
                yield return Path.GetFullPath(Path.Combine(
                    extensionRoot,
                    toolsVersion,
                    family,
                    phase));
            }
        }
    }

    private static IEnumerable<string> EnumerateMsBuildSystemHookFiles(
        string extensionRoot,
        string toolsVersion)
    {
        foreach (var hookDirectory in EnumerateMsBuildHookDirectories(
                     extensionRoot,
                     toolsVersion))
        {
            if (!Directory.Exists(hookDirectory))
                continue;

            foreach (var hookPath in Directory.EnumerateFiles(
                         hookDirectory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                yield return Path.GetFullPath(hookPath);
            }
        }

        var toolsDirectory = Path.GetFullPath(Path.Combine(extensionRoot, toolsVersion));
        if (Directory.Exists(toolsDirectory))
        {
            foreach (var hookPath in Directory.EnumerateFiles(
                         toolsDirectory,
                         "Microsoft.VisualStudioVersion.v*.Common.props",
                         SearchOption.TopDirectoryOnly))
            {
                yield return Path.GetFullPath(hookPath);
            }
        }

        var customDirectory = Path.GetFullPath(Path.Combine(
            extensionRoot,
            $"v{toolsVersion}"));
        foreach (var fileName in new[]
                 {
                     "Custom.Before.Microsoft.Common.props",
                     "Custom.After.Microsoft.Common.props",
                     "Custom.Before.Microsoft.Common.targets",
                     "Custom.After.Microsoft.Common.targets",
                     "Custom.Before.Microsoft.CSharp.targets",
                     "Custom.After.Microsoft.CSharp.targets",
                     "Custom.Before.Microsoft.VisualBasic.targets",
                     "Custom.After.Microsoft.VisualBasic.targets"
                 })
        {
            var hookPath = Path.Combine(customDirectory, fileName);
            if (File.Exists(hookPath))
                yield return hookPath;
        }
    }

    private static bool TryNormalizeRoot(string candidate, out string root)
    {
        try
        {
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            root = string.Empty;
            return false;
        }
    }

    private static bool TryCaptureFileHash(string path, out string hash)
    {
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            hash = Convert.ToHexString(SHA256.HashData(stream));
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            hash = string.Empty;
            return false;
        }
    }

    private static SystemHookInputs ReadSystemHookInputs(
        string hookPath,
        string systemExtensionsRoot,
        bool canSkipLegacySdkExtensionImports)
    {
        try
        {
            using var stream = new FileStream(
                hookPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var directory = Path.GetDirectoryName(hookPath)!;
            var paths = new HashSet<string>(PathComparer);
            var reusable = HasOnlySupportedSdkDeclarations(document)
                           && !HasUnprovenInitialTargets(document);
            foreach (var element in document.Descendants())
            {
                var isKnownTestPlatformTaskInput = TryResolveKnownTestPlatformTaskInput(
                    hookPath,
                    systemExtensionsRoot,
                    element,
                    out var testPlatformTaskInput);
                if (testPlatformTaskInput is not null)
                    paths.Add(testPlatformTaskInput);

                if ((element.Nodes().OfType<XText>().Any(text =>
                         ContainsUnprovenPropertyFunction(text.Value))
                     || element.Attributes().Any(attribute =>
                         ContainsUnprovenPropertyFunction(attribute.Value)))
                    && !IsKnownSafeSystemHookPropertyFunction(
                        hookPath,
                        systemExtensionsRoot,
                        element))
                {
                    reusable = false;
                }

                if (IsUnsupportedTargetTask(element)
                    && !IsKnownSafeSystemHookTargetTask(
                        hookPath,
                        systemExtensionsRoot,
                        element))
                {
                    reusable = false;
                }

                if (IsImplicitSdkImportOverride(element))
                {
                    if (!TryResolveKnownSystemHookSelector(
                            hookPath,
                            element,
                            systemExtensionsRoot,
                            out var selectorPath))
                    {
                        reusable = false;
                    }
                    else
                    {
                        paths.Add(selectorPath);
                    }
                }

                if (IsArtifactsOutputCustomization(element)
                    || IsDefaultOutputCustomization(element)
                    || IsUnprovenCompilerInputCustomization(element)
                    || HasName(element, "MSBuildProjectExtensionsPath")
                    || HasName(element, "ProjectAssetsFile")
                    || HasName(element, "NuGetPackageRoot")
                    || IsUnprovenTargetFrameworkAssignment(element)
                    || IsSdkQualifiedImport(element)
                    || (IsTaskImplementationOrSchedulingCustomization(element)
                        && !isKnownTestPlatformTaskInput)
                    || (IsUnprovenAuthoredTaskSchedulingCustomization(element)
                        && !IsKnownSafeSystemHookScheduling(
                            hookPath,
                            systemExtensionsRoot,
                            element))
                    || (IsLegacySdkExtensionCustomization(element)
                        && !(canSkipLegacySdkExtensionImports
                             && IsKnownLegacySdkExtensionDefault(
                                 hookPath,
                                 systemExtensionsRoot,
                                 element))))
                {
                    reusable = false;
                }

                var isImport = HasName(element, "Import");
                if (isImport
                    && IsConditionalLegacySdkExtensionImport(
                        hookPath,
                        systemExtensionsRoot,
                        element))
                {
                    reusable &= canSkipLegacySdkExtensionImports;
                    continue;
                }

                var condition = GetAttributeValue(element, "Condition");
                if (!string.IsNullOrWhiteSpace(condition))
                {
                    var conditionInputs = ReadConditionInputs(
                        ReplaceKnownSystemHookSelectors(condition, directory),
                        hookPath,
                        directory,
                        projectDirectory: null,
                        nuGetPackageRoot: null);
                    paths.UnionWith(conditionInputs.Paths);
                    reusable &= conditionInputs.Reusable;
                }

                var isProjectRelativeItem = IsProjectRelativeItem(element);
                string? declaredPath;
                if (isImport)
                    declaredPath = GetAttributeValue(element, "Project");
                else if (isProjectRelativeItem && !HasName(element, "HintPath"))
                    declaredPath = GetAttributeValue(element, "Include");
                else if (HasName(element, "HintPath"))
                    declaredPath = element.Value;
                else
                    declaredPath = null;
                if (string.IsNullOrWhiteSpace(declaredPath))
                    continue;
                foreach (var candidate in declaredPath.Split(
                             ';',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (isImport && IsKnownSystemHookSelector(candidate))
                        continue;
                    if (isProjectRelativeItem
                        && IsEvaluationContextRelative(candidate))
                        reusable = false;
                    if (!TryResolveDeclaredPath(
                            candidate,
                            directory,
                            projectDirectory: null,
                            nuGetPackageRoot: null,
                            out var fullPath))
                    {
                        reusable = false;
                        continue;
                    }

                    paths.Add(fullPath);
                    if (isImport && !IsBuildInput(fullPath))
                        reusable = false;
                }
            }

            return new SystemHookInputs(paths, reusable);
        }
        catch (XmlException exception)
        {
            throw new IOException(
                $"Workspace system hook could not be parsed consistently: '{hookPath}'.",
                exception);
        }
    }

    private static bool IsKnownSystemHookSelector(string candidate) =>
        candidate.Equals("$(VSTestTargets)", StringComparison.OrdinalIgnoreCase)
        || candidate.Equals(
            "$(MicrosoftNETBuildExtensionsTargets)",
            StringComparison.OrdinalIgnoreCase);

    private static bool TryResolveKnownSystemHookSelector(
        string hookPath,
        XElement element,
        string systemExtensionsRoot,
        out string selectorPath)
    {
        selectorPath = string.Empty;
        string? expectedFileName;
        string? expectedValue;
        if (HasName(element, "VSTestTargets"))
        {
            expectedFileName = "Microsoft.TestPlatform.ImportAfter.targets";
            expectedValue = "$(MSBuildExtensionsPath)\\Microsoft.TestPlatform.targets";
        }
        else if (HasName(element, "MicrosoftNETBuildExtensionsTargets"))
        {
            expectedFileName = "Microsoft.NET.Build.Extensions.targets";
            expectedValue = "$(MSBuildExtensionsPath)\\Microsoft\\Microsoft.NET.Build.Extensions\\Microsoft.NET.Build.Extensions.targets";
        }
        else
        {
            return false;
        }

        var condition = GetAttributeValue(element, "Condition");
        var expectedEmptyCheck = $"'$({element.Name.LocalName})'";
        if (!IsExpectedWildcardHookPath(
                hookPath,
                systemExtensionsRoot,
                "Microsoft.Common.targets",
                expectedFileName)
            || !element.Value.Trim().Equals(expectedValue, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(condition)
            || !condition.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Equals(
                    $"{expectedEmptyCheck}==''",
                    StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var relative = expectedValue["$(MSBuildExtensionsPath)".Length..]
            .TrimStart('\\', '/');
        selectorPath = Path.GetFullPath(
            relative
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar),
            systemExtensionsRoot);
        return true;
    }

    private static bool IsKnownSafeSystemHookPropertyFunction(
        string hookPath,
        string systemExtensionsRoot,
        XElement element)
    {
        if (IsExpectedWildcardHookPath(
                hookPath,
                systemExtensionsRoot,
                "Microsoft.Common.CrossTargeting.targets",
                "Microsoft.TestPlatform.CrossTargeting.targets"))
        {
            return HasName(element, "_TestTfmsInParallel")
                   && element.Value.Trim().Equals(
                       "$([MSBuild]::ValueOrDefault('$(TestTfmsInParallel)', '$(BuildInParallel)'))",
                       StringComparison.Ordinal);
        }

        if (!IsExpectedSystemFile(
                hookPath,
                systemExtensionsRoot,
                "Microsoft.TestPlatform.targets"))
        {
            return false;
        }

        return element.Nodes().OfType<XText>()
                   .Select(text => text.Value)
                   .Concat(element.Attributes().Select(attribute => attribute.Value))
                   .Where(ContainsUnprovenPropertyFunction)
                   .All(ContainsOnlyKnownTestPlatformPropertyFunctions);
    }

    private static bool ContainsOnlyKnownTestPlatformPropertyFunctions(string value)
    {
        string[] allowedPrefixes =
        [
            "$([MSBuild]::ValueOrDefault(",
            "$([System.IO.Path]::Combine("
        ];
        for (var index = value.IndexOf("$([", StringComparison.Ordinal);
             index >= 0;
             index = value.IndexOf("$([", index + 3, StringComparison.Ordinal))
        {
            if (!allowedPrefixes.Any(prefix => value.AsSpan(index).StartsWith(
                    prefix,
                    StringComparison.Ordinal)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKnownSafeSystemHookTargetTask(
        string hookPath,
        string systemExtensionsRoot,
        XElement element)
    {
        if (IsExpectedWildcardHookPath(
                hookPath,
                systemExtensionsRoot,
                "Microsoft.Common.targets",
                "Microsoft.TestPlatform.ImportAfter.targets"))
        {
            return HasName(element, "MSBuild")
                   && GetAttributeValue(element, "Projects") == "$(MSBuildProjectFullPath)";
        }

        if (IsExpectedWildcardHookPath(
                hookPath,
                systemExtensionsRoot,
                "Microsoft.Common.CrossTargeting.targets",
                "Microsoft.TestPlatform.CrossTargeting.targets"))
        {
            var targetName = element.Parent is { } parent
                ? GetAttributeValue(parent, "Name")
                : null;
            if (HasName(element, "MSBuild"))
            {
                var projects = GetAttributeValue(element, "Projects");
                return projects == "@(_ProjectToTestWithTFM)"
                       || projects == "$(MSBuildProjectFullPath)";
            }

            return HasName(element, "CallTarget")
                       && targetName is "VSTest" or "BuildProject"
                   || HasName(element, "Microsoft.TestPlatform.Build.Tasks.VSTestLogsTask")
                       && targetName == "BuildProject";
        }

        if (IsExpectedSystemFile(
                hookPath,
                systemExtensionsRoot,
                "Microsoft.TestPlatform.targets"))
        {
            if (HasName(element, "MSBuild"))
            {
                return GetAttributeValue(element, "Projects")
                       == "$(MSBuildProjectFullPath)";
            }

            return HasName(element, "CallTarget")
                   || HasName(element, "VSTestTask")
                   || HasName(element, "VSTestTask2")
                   || HasName(element, "VSTestLogsTask")
                   || HasName(element, "Message");
        }

        return false;
    }

    private static bool IsExpectedWildcardHookPath(
        string hookPath,
        string systemExtensionsRoot,
        string family,
        string fileName)
    {
        var relative = Path.GetRelativePath(systemExtensionsRoot, hookPath);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 4
               && segments[1].Equals(family, StringComparison.OrdinalIgnoreCase)
               && segments[2].Equals("ImportAfter", StringComparison.OrdinalIgnoreCase)
               && segments[3].Equals(fileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedSystemFile(
        string path,
        string systemExtensionsRoot,
        string relativePath) =>
        PathComparer.Equals(
            Path.GetFullPath(path),
            Path.GetFullPath(relativePath, systemExtensionsRoot));

    private static bool IsConditionalLegacySdkExtensionImport(
        string hookPath,
        string systemExtensionsRoot,
        XElement element)
    {
        if (!IsExpectedSystemFile(
                hookPath,
                systemExtensionsRoot,
                Path.Combine(
                    "Microsoft",
                    "Microsoft.NET.Build.Extensions",
                    "Microsoft.NET.Build.Extensions.targets")))
        {
            return false;
        }

        var project = GetAttributeValue(element, "Project");
        var condition = GetAttributeValue(element, "Condition")?
            .Replace(" ", string.Empty, StringComparison.Ordinal);
        return project?.Equals(
                   "Microsoft.NET.Build.Extensions.NETFramework.targets",
                   StringComparison.OrdinalIgnoreCase) == true
               && condition?.Equals(
                   "'$(TargetFrameworkIdentifier)'=='.NETFramework'",
                   StringComparison.OrdinalIgnoreCase) == true
               || project?.Equals(
                   "Microsoft.NET.Build.Extensions.ConflictResolution.targets",
                   StringComparison.OrdinalIgnoreCase) == true
               && condition?.Equals(
                   "'$(ResolveAssemblyConflicts)'=='true'AND'$(UsingMicrosoftNETSdk)'!='true'",
                   StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsKnownLegacySdkExtensionDefault(
        string hookPath,
        string systemExtensionsRoot,
        XElement element) =>
        IsExpectedSystemFile(
            hookPath,
            systemExtensionsRoot,
            Path.Combine(
                "Microsoft",
                "Microsoft.NET.Build.Extensions",
                "Microsoft.NET.Build.Extensions.targets"))
        && (HasName(element, "MicrosoftNETBuildExtensionsTasksAssembly")
            || HasName(element, "ResolveAssemblyConflicts"));

    private static bool CanSkipLegacySdkExtensionImports(
        IReadOnlyList<string> projectPaths,
        IReadOnlySet<string> evaluatedBuildInputs)
    {
        foreach (var buildInput in evaluatedBuildInputs)
        {
            if (!File.Exists(buildInput))
                continue;

            XDocument document;
            try
            {
                using var stream = new FileStream(
                    buildInput,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                document = XDocument.Load(reader, LoadOptions.None);
            }
            catch (XmlException exception)
            {
                throw new IOException(
                    $"Workspace build input could not be parsed consistently: '{buildInput}'.",
                    exception);
            }

            if (document.Descendants().Any(IsLegacySdkExtensionCustomization))
                return false;

            foreach (var assignment in document.Descendants().Where(element =>
                         HasName(element, "TargetFramework")
                         || HasName(element, "TargetFrameworks")))
            {
                if (HasConditionalEvaluation(assignment)
                    || !AreDefinitelyModernTargetFrameworks(assignment.Value))
                {
                    return false;
                }
            }

            if (!projectPaths.Contains(buildInput, PathComparer))
                continue;

            var hasSdk = EnumerateSdkDeclarations(document).Any();
            if (hasSdk && !HasOnlySupportedSdkDeclarations(document))
                return false;
            if (hasSdk)
                continue;

            if (document.Descendants().Where(element => HasName(element, "Import"))
                .Select(element => GetAttributeValue(element, "Project"))
                .Any(import => import?.Contains(
                    "Microsoft.Common.targets",
                    StringComparison.OrdinalIgnoreCase) == true
                               || import?.Contains(
                                   "Microsoft.CSharp.targets",
                                   StringComparison.OrdinalIgnoreCase) == true
                               || import?.Contains(
                                   "Microsoft.VisualBasic.targets",
                                   StringComparison.OrdinalIgnoreCase) == true))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreDefinitelyModernTargetFrameworks(string value)
    {
        foreach (var candidate in value.Split(
                     ';',
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (candidate.Contains("$(", StringComparison.Ordinal)
                || candidate.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase))
            {
                if (candidate.Contains("$(", StringComparison.Ordinal))
                    return false;
                continue;
            }

            if (!candidate.StartsWith("net", StringComparison.OrdinalIgnoreCase)
                || !Version.TryParse(candidate[3..], out var version)
                || version.Major < 5)
            {
                return false;
            }
        }

        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryResolveKnownTestPlatformTaskInput(
        string hookPath,
        string systemExtensionsRoot,
        XElement element,
        out string? taskInput)
    {
        taskInput = null;
        var isTestPlatformTargets = IsExpectedSystemFile(
            hookPath,
            systemExtensionsRoot,
            "Microsoft.TestPlatform.targets");
        var isCrossTargeting = IsExpectedWildcardHookPath(
            hookPath,
            systemExtensionsRoot,
            "Microsoft.Common.CrossTargeting.targets",
            "Microsoft.TestPlatform.CrossTargeting.targets");
        if (!isTestPlatformTargets && !isCrossTargeting)
            return false;

        if (HasName(element, "VSTestTaskAssemblyFile"))
        {
            var expectedValue = isTestPlatformTargets
                ? "Microsoft.TestPlatform.Build.dll"
                : "$(MSBuildExtensionsPath)\\Microsoft.TestPlatform.Build.dll";
            var condition = GetAttributeValue(element, "Condition")?
                .Replace(" ", string.Empty, StringComparison.Ordinal);
            if (!element.Value.Trim().Equals(expectedValue, StringComparison.OrdinalIgnoreCase)
                || condition?.Equals(
                    "$(VSTestTaskAssemblyFile)==''",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                return false;
            }

            taskInput = Path.Combine(
                systemExtensionsRoot,
                "Microsoft.TestPlatform.Build.dll");
            return true;
        }

        if (!HasName(element, "UsingTask")
            || GetAttributeValue(element, "AssemblyFile")
            != "$(VSTestTaskAssemblyFile)")
        {
            return false;
        }

        var taskName = GetAttributeValue(element, "TaskName");
        var isKnownTask = isTestPlatformTargets
            ? taskName is "Microsoft.TestPlatform.Build.Tasks.VSTestTask"
                or "Microsoft.TestPlatform.Build.Tasks.VSTestLogsTask"
                or "Microsoft.TestPlatform.Build.Tasks.VSTestTask2"
            : taskName == "Microsoft.TestPlatform.Build.Tasks.VSTestLogsTask";
        if (!isKnownTask)
            return false;

        taskInput = Path.Combine(
            systemExtensionsRoot,
            "Microsoft.TestPlatform.Build.dll");
        return true;
    }

    private static bool HasOnlySupportedSdkDeclarations(XDocument document) =>
        EnumerateSdkDeclarations(document).All(declaration =>
            SupportedBuiltInSdks.Contains(
                declaration.Split('/', 2, StringSplitOptions.TrimEntries)[0]));

    private static bool HasUnprovenInitialTargets(XDocument document) =>
        document.Root?.Attributes().Any(attribute =>
            attribute.Name.LocalName.Equals("InitialTargets", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(attribute.Value)) == true;

    private static bool IsTaskImplementationOrSchedulingCustomization(XElement element)
    {
        if (HasName(element, "UsingTask"))
            return true;

        if (HasName(element, "VSTestTaskAssemblyFile")
            || HasName(element, "MicrosoftBuildTasksGitAssemblyFile"))
            return true;

        if (element.Name.LocalName.EndsWith("DependsOn", StringComparison.OrdinalIgnoreCase)
            && (element.Value.Contains("VSTest", StringComparison.OrdinalIgnoreCase)
                || element.Value.Contains("_MTPBuild", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return element.Attributes().Any(attribute =>
            attribute.Name.LocalName is "BeforeTargets" or "AfterTargets" or "DependsOnTargets"
            && (attribute.Value.Contains("VSTest", StringComparison.OrdinalIgnoreCase)
                || attribute.Value.Contains("_MTPBuild", StringComparison.OrdinalIgnoreCase)));
    }

    private static bool IsUnprovenAuthoredTaskSchedulingCustomization(XElement element) =>
        element.Name.LocalName.EndsWith("DependsOn", StringComparison.OrdinalIgnoreCase)
        || element.Attributes().Any(attribute =>
            attribute.Name.LocalName is "BeforeTargets" or "AfterTargets" or "DependsOnTargets");

    private static bool IsKnownSafeSystemHookScheduling(
        string hookPath,
        string systemExtensionsRoot,
        XElement element)
    {
        if (!HasName(element, "Target")
            || GetAttributeValue(element, "Name") != "VSTest"
            || element.Attributes().Any(attribute =>
                attribute.Name.LocalName is "BeforeTargets" or "AfterTargets"))
        {
            return false;
        }

        var dependsOnTargets = GetAttributeValue(element, "DependsOnTargets");
        return (IsExpectedSystemFile(
                    hookPath,
                    systemExtensionsRoot,
                    "Microsoft.TestPlatform.targets")
                && dependsOnTargets == "ShowInfoMessageIfProjectHasNoIsTestProjectProperty")
               || (IsExpectedWildcardHookPath(
                       hookPath,
                       systemExtensionsRoot,
                       "Microsoft.Common.CrossTargeting.targets",
                       "Microsoft.TestPlatform.CrossTargeting.targets")
                   && dependsOnTargets == "_ComputeTargetFrameworkItems");
    }

    private static bool IsSdkQualifiedImport(XElement element) =>
        HasName(element, "Import")
        && element.Attributes().Any(attribute =>
            attribute.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<string> EnumerateSdkDeclarations(XDocument document)
    {
        var project = document.Root;
        if (project is null)
            yield break;

        var attribute = project.Attributes().FirstOrDefault(candidate =>
            candidate.Name.LocalName.Equals("Sdk", StringComparison.OrdinalIgnoreCase));
        if (attribute is not null)
        {
            foreach (var declaration in attribute.Value.Split(
                         ';',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return declaration;
            }
        }

        foreach (var sdk in project.Elements().Where(element => HasName(element, "Sdk")))
        {
            var declaration = GetAttributeValue(sdk, "Name") ?? sdk.Value.Trim();
            if (!string.IsNullOrWhiteSpace(declaration))
                yield return declaration;
        }

        foreach (var import in document.Descendants().Where(IsSdkQualifiedImport))
        {
            var declaration = GetAttributeValue(import, "Sdk");
            if (!string.IsNullOrWhiteSpace(declaration))
                yield return declaration;
        }
    }

    private static string ReplaceKnownSystemHookSelectors(string condition, string directory) =>
        condition
            .Replace(
                "$(VSTestTargets)",
                Path.Combine(directory, ".rt-vstest-targets"),
                StringComparison.OrdinalIgnoreCase)
            .Replace(
                "$(MicrosoftNETBuildExtensionsTargets)",
                Path.Combine(directory, ".rt-net-build-extensions-targets"),
                StringComparison.OrdinalIgnoreCase);

    private sealed record MsBuildSystemHookBaseline(
        string? Root,
        string ToolsVersion,
        IReadOnlyDictionary<string, string> FileHashes,
        bool Reusable);

    private DeclaredBuildInputs ReadImplicitRestoreInputs(string projectPath)
    {
        var paths = new HashSet<string>(PathComparer);
        var isolatedObj = _loadConfiguration?.BaseIntermediateOutputPath;
        var reusable = isolatedObj is null;
        var projectDirectory = Path.GetDirectoryName(projectPath)!;
        var extensionsDirectory = isolatedObj ?? Path.Combine(projectDirectory, "obj");
        string? assetsPath = null;
        try
        {
            if (File.Exists(projectPath))
            {
                using var stream = new FileStream(
                    projectPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                var document = XDocument.Load(reader, LoadOptions.None);
                var projectExtensionsAssignments = document.Descendants()
                    .Where(element => HasName(element, "MSBuildProjectExtensionsPath"))
                    .ToArray();
                var baseIntermediateAssignments = document.Descendants()
                    .Where(element => HasName(element, "BaseIntermediateOutputPath"))
                    .ToArray();
                if (projectExtensionsAssignments.Any(HasConditionalEvaluation)
                    || baseIntermediateAssignments.Any(HasConditionalEvaluation))
                {
                    reusable = false;
                }

                var configured = projectExtensionsAssignments.LastOrDefault()
                                 ?? baseIntermediateAssignments.LastOrDefault();
                if (configured is not null)
                {
                    var value = configured.Value.Trim();
                    if (HasConditionalEvaluation(configured)
                        || IsUnsupportedDeclaredPath(value))
                    {
                        reusable = false;
                    }
                    else
                    {
                        extensionsDirectory = Path.GetFullPath(
                            value
                                .Replace('\\', Path.DirectorySeparatorChar)
                                .Replace('/', Path.DirectorySeparatorChar),
                            projectDirectory);
                    }
                }

                var configuredAssetAssignments = document.Descendants()
                    .Where(element => HasName(element, "ProjectAssetsFile"))
                    .ToArray();
                if (configuredAssetAssignments.Any(HasConditionalEvaluation))
                    reusable = false;
                var configuredAssets = configuredAssetAssignments.LastOrDefault();
                if (configuredAssets is not null)
                {
                    var value = configuredAssets.Value.Trim();
                    if (HasConditionalEvaluation(configuredAssets)
                        || IsUnsupportedDeclaredPath(value))
                    {
                        reusable = false;
                    }
                    else
                    {
                        assetsPath = Path.GetFullPath(
                            value
                                .Replace('\\', Path.DirectorySeparatorChar)
                                .Replace('/', Path.DirectorySeparatorChar),
                            projectDirectory);
                    }
                }
            }
        }
        catch (XmlException exception)
        {
            throw new IOException(
                $"Workspace build input could not be parsed consistently: '{projectPath}'.",
                exception);
        }

        var projectFileName = Path.GetFileName(projectPath);
        paths.Add(assetsPath ?? Path.Combine(extensionsDirectory, "project.assets.json"));
        paths.Add(Path.Combine(extensionsDirectory, $"{projectFileName}.nuget.g.props"));
        paths.Add(Path.Combine(extensionsDirectory, $"{projectFileName}.nuget.g.targets"));
        paths.Add(Path.Combine(extensionsDirectory, $"{projectFileName}.nuget.dgspec.json"));
        if (Directory.Exists(extensionsDirectory))
        {
            paths.UnionWith(Directory.EnumerateFiles(
                extensionsDirectory,
                $"{projectFileName}.*.props",
                SearchOption.TopDirectoryOnly));
            paths.UnionWith(Directory.EnumerateFiles(
                extensionsDirectory,
                $"{projectFileName}.*.targets",
                SearchOption.TopDirectoryOnly));
        }

        return new DeclaredBuildInputs(paths, reusable);
    }

    private static DeclaredBuildInputs ReadDeclaredSolutionProjects(string solutionPath)
    {
        var paths = new HashSet<string>(PathComparer);
        var reusable = true;
        var directory = Path.GetDirectoryName(solutionPath)!;
        try
        {
            if (Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new FileStream(
                    solutionPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                var document = XDocument.Load(reader, LoadOptions.None);
                foreach (var project in document.Descendants()
                             .Where(element => element.Name.LocalName.Equals(
                                 "Project",
                                 StringComparison.OrdinalIgnoreCase)))
                {
                    AddDeclaredProjectPath(project.Attribute("Path")?.Value);
                }
            }
            else if (Path.GetExtension(solutionPath).Equals(".sln", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var line in File.ReadLines(solutionPath))
                {
                    if (!line.TrimStart().StartsWith("Project(", StringComparison.Ordinal))
                        continue;

                    var segments = line.Split('"');
                    if (segments.Length > 5)
                        AddDeclaredProjectPath(segments[5]);
                }
            }

            return new DeclaredBuildInputs(paths, reusable);
        }
        catch (XmlException exception)
        {
            throw new IOException(
                $"Workspace solution input could not be parsed consistently: '{solutionPath}'.",
                exception);
        }

        void AddDeclaredProjectPath(string? declaredPath)
        {
            if (string.IsNullOrWhiteSpace(declaredPath))
                return;

            var normalized = declaredPath
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            if (normalized.Contains("$(", StringComparison.Ordinal)
                || normalized.IndexOfAny(['*', '?']) >= 0)
            {
                reusable = false;
                return;
            }

            var fullPath = Path.GetFullPath(normalized, directory);
            if (IsProjectInput(fullPath))
                paths.Add(fullPath);
        }
    }

    private DeclaredBuildInputs ReadDeclaredBuildInputs(string buildInput)
    {
        try
        {
            using var stream = new FileStream(
                buildInput,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader, LoadOptions.None);
            var directory = Path.GetDirectoryName(buildInput)!;
            var paths = new HashSet<string>(PathComparer);
            var reusable = HasOnlySupportedSdkDeclarations(document)
                           && !HasUnprovenInitialTargets(document);
            var diagnosticGuard = IsKnownDiagnosticOnlyDependencyGuard(document, buildInput);
            var nuGetPackageRoot = ResolveNuGetPackageRoot(
                document,
                buildInput,
                GetEnvironmentProperty("NuGetPackageRoot"));
            var projectDirectory = IsProjectInput(buildInput) ? directory : null;
            foreach (var element in document.Descendants())
            {
                var isImport = HasName(element, "Import");
                var isGeneratedAssetsAssignment = IsGeneratedNuGetBuildInput(buildInput)
                                                  && HasName(element, "ProjectAssetsFile");
                var conditionInputs = ReadConditionInputs(
                    element.Attribute("Condition")?.Value,
                    buildInput,
                    directory,
                    projectDirectory,
                    nuGetPackageRoot);
                paths.UnionWith(conditionInputs.Paths);
                reusable &= conditionInputs.Reusable;
                if (ContainsUnprovenPropertyFunction(element.Value)
                    || element.Attributes().Any(attribute =>
                        ContainsUnprovenPropertyFunction(attribute.Value)))
                {
                    reusable = false;
                }

                if (IsUnsupportedTargetTask(element)
                    && element.Parent != diagnosticGuard)
                    reusable = false;

                if (IsArtifactsOutputCustomization(element)
                    || IsImplicitSdkImportOverride(element)
                    || IsUnprovenCompilerInputCustomization(element)
                    || IsSdkQualifiedImport(element)
                    || IsLegacySdkExtensionCustomization(element)
                    || IsUnprovenTargetFrameworkAssignment(element)
                    || (IsTaskImplementationOrSchedulingCustomization(element)
                        && element != diagnosticGuard)
                    || (IsUnprovenAuthoredTaskSchedulingCustomization(element)
                        && element != diagnosticGuard))
                {
                    reusable = false;
                }

                if (HasName(element, "NuGetPackageRoot")
                    && !IsGeneratedNuGetBuildInput(buildInput))
                {
                    reusable = false;
                }

                if (!IsProjectInput(buildInput)
                    && IsDefaultOutputCustomization(element))
                {
                    reusable = false;
                }

                if (!IsProjectInput(buildInput)
                    && (HasName(element, "MSBuildProjectExtensionsPath")
                        || HasName(element, "BaseIntermediateOutputPath")))
                {
                    reusable = false;
                }

                if (!IsProjectInput(buildInput)
                    && HasName(element, "ProjectAssetsFile")
                    && !isGeneratedAssetsAssignment)
                {
                    reusable = false;
                }

                var isProjectRelativeItem = IsProjectRelativeItem(element);
                string? declaredPath;
                if (isImport)
                    declaredPath = GetAttributeValue(element, "Project");
                else if (isGeneratedAssetsAssignment)
                    declaredPath = element.Value;
                else if (isProjectRelativeItem && !HasName(element, "HintPath"))
                    declaredPath = GetAttributeValue(element, "Include");
                else if (HasName(element, "HintPath"))
                    declaredPath = element.Value;
                else
                    declaredPath = null;
                if (string.IsNullOrWhiteSpace(declaredPath))
                    continue;

                foreach (var candidate in declaredPath.Split(
                             ';',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!IsProjectInput(buildInput)
                        && (isProjectRelativeItem || isGeneratedAssetsAssignment)
                        && IsEvaluationContextRelative(candidate))
                    {
                        reusable = false;
                    }

                    if (!TryResolveDeclaredPath(
                            candidate,
                            directory,
                            projectDirectory,
                            nuGetPackageRoot,
                            out var fullPath))
                    {
                        reusable = false;
                        continue;
                    }

                    paths.Add(fullPath);
                    if (nuGetPackageRoot is not null
                        && TryAddNuGetPackageInputs(
                            paths,
                            nuGetPackageRoot,
                            fullPath))
                    {
                        continue;
                    }

                    if (isImport && !IsBuildInput(fullPath))
                        reusable = false;
                }
            }

            return new DeclaredBuildInputs(paths, reusable);
        }
        catch (XmlException exception)
        {
            throw new IOException(
                $"Workspace build input could not be parsed consistently: '{buildInput}'.",
                exception);
        }
    }

    private static XElement? IsKnownDiagnosticOnlyDependencyGuard(
        XDocument document,
        string buildInput)
    {
        if (!Path.GetFileName(buildInput).Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase)
            || document.Root is not { } root
            || !HasName(root, "Project")
            || root.Attributes().Any()
            || root.Elements().Count() != 1)
        {
            return null;
        }

        var target = root.Elements().Single();
        if (!HasName(target, "Target")
            || !HasOnlyAttributes(target, "Name", "BeforeTargets", "Condition")
            || GetAttributeValue(target, "Name") != "ValidateReusableDependencies"
            || GetAttributeValue(target, "BeforeTargets") != "CollectPackageReferences;ResolveReferences"
            || GetAttributeValue(target, "Condition") !=
                "'$(MSBuildProjectName)' == 'RoslynTools' Or '$(MSBuildProjectName)' == 'RoslynTools.Hosting'")
        {
            return null;
        }

        var children = target.Elements().ToArray();
        if (children.Length != 3
            || !HasName(children[0], "ItemGroup")
            || children[0].Attributes().Any()
            || !IsDiagnosticGuardItem(children[0].Elements().ElementAtOrDefault(0),
                "_ForbiddenReusablePackage", "@(PackageReference)")
            || !IsDiagnosticGuardItem(children[0].Elements().ElementAtOrDefault(1),
                "_ForbiddenReusableProject", "@(ProjectReference)")
            || children[0].Elements().Count() != 2
            || !IsDiagnosticGuardError(children[1], "RTDEP001", "_ForbiddenReusablePackage")
            || !IsDiagnosticGuardError(children[2], "RTDEP002", "_ForbiddenReusableProject")
            || target.DescendantNodes().OfType<XText>().Any(text =>
                !string.IsNullOrWhiteSpace(text.Value)))
        {
            return null;
        }

        return target;
    }

    private static bool IsDiagnosticGuardItem(XElement? element, string name, string source) =>
        element is not null
        && HasName(element, name)
        && HasOnlyAttributes(element, "Include", "Condition")
        && GetAttributeValue(element, "Include") == source
        && !string.IsNullOrWhiteSpace(GetAttributeValue(element, "Condition"))
        && !element.HasElements;

    private static bool IsDiagnosticGuardError(XElement element, string code, string itemName) =>
        HasName(element, "Error")
        && HasOnlyAttributes(element, "Code", "Condition", "Text")
        && GetAttributeValue(element, "Code") == code
        && GetAttributeValue(element, "Condition") == $"'@({itemName})' != ''"
        && !string.IsNullOrWhiteSpace(GetAttributeValue(element, "Text"))
        && !element.HasElements;

    private static bool HasOnlyAttributes(XElement element, params string[] names) =>
        element.Name.NamespaceName.Length == 0
        && element.Attributes().Count() == names.Length
        && names.All(name => element.Attribute(name) is not null);

    private static DeclaredBuildInputs ReadConditionInputs(
        string? condition,
        string buildInput,
        string directory,
        string? projectDirectory,
        string? nuGetPackageRoot)
    {
        var paths = new HashSet<string>(PathComparer);
        if (string.IsNullOrWhiteSpace(condition))
            return new DeclaredBuildInputs(paths, true);

        var reusable = !ContainsUnprovenPropertyFunction(condition);
        var matches = ExistsConditionRegex.Matches(condition);
        if (condition.Contains("Exists", StringComparison.OrdinalIgnoreCase)
            && ExistsConditionRegex.Replace(condition, string.Empty)
                .Contains("Exists", StringComparison.OrdinalIgnoreCase))
        {
            reusable = false;
        }

        foreach (Match match in matches)
        {
            var declaredPath = match.Groups["path"].Value;
            if (!IsProjectInput(buildInput)
                && IsEvaluationContextRelative(declaredPath))
            {
                reusable = false;
            }

            if (!TryResolveDeclaredPath(
                    declaredPath,
                    directory,
                    projectDirectory,
                    nuGetPackageRoot,
                    out var fullPath))
            {
                reusable = false;
                continue;
            }

            paths.Add(fullPath);
            if (nuGetPackageRoot is not null)
                TryAddNuGetPackageInputs(
                    paths,
                    nuGetPackageRoot,
                    fullPath);
        }

        return new DeclaredBuildInputs(paths, reusable);
    }

    private static bool ContainsUnprovenPropertyFunction(string? value) =>
        value?.Contains("$([", StringComparison.Ordinal) == true;

    private static bool HasConditionalEvaluation(XElement element) =>
        element.AncestorsAndSelf().Any(candidate =>
            candidate.Attribute("Condition") is not null
            || HasName(candidate, "Choose")
            || HasName(candidate, "When")
            || HasName(candidate, "Otherwise")
            || HasName(candidate, "Target"));

    private static bool IsUnsupportedTargetTask(XElement element) =>
        element.Parent is { } parent
        && HasName(parent, "Target")
        && !HasName(element, "PropertyGroup")
        && !HasName(element, "ItemGroup")
        && !HasName(element, "OnError");

    private static bool IsDefaultOutputCustomization(XElement element) =>
        HasName(element, "BaseOutputPath")
        || HasName(element, "OutputPath")
        || HasName(element, "BaseIntermediateOutputPath")
        || HasName(element, "MSBuildProjectExtensionsPath")
        || HasName(element, "DefaultItemExcludes")
        || HasName(element, "DefaultExcludesInProjectFolder")
        || HasName(element, "EnableDefaultItems")
        || HasName(element, "EnableDefaultCompileItems");

    private static bool IsArtifactsOutputCustomization(XElement element) =>
        element.Name.LocalName.StartsWith("Artifacts", StringComparison.OrdinalIgnoreCase)
        || HasName(element, "UseArtifactsOutput")
        || HasName(element, "UseArtifactsIntermediateOutput")
        || HasName(element, "IncludeProjectNameInArtifactsPaths");

    private static bool IsImplicitSdkImportOverride(XElement element) =>
        HasName(element, "DirectoryBuildPropsPath")
        || HasName(element, "DirectoryBuildTargetsPath")
        || HasName(element, "MSBuildExtensionsPath")
        || HasName(element, "MSBuildUserExtensionsPath")
        || HasName(element, "MSBuildToolsVersion")
        || HasName(element, "MicrosoftNETBuildExtensionsTargets")
        || HasName(element, "VSTestTargets")
        || element.Name.LocalName.StartsWith("CustomBefore", StringComparison.OrdinalIgnoreCase)
        || element.Name.LocalName.StartsWith("CustomAfter", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnprovenCompilerInputCustomization(XElement element) =>
        HasName(element, "AllowedReferenceAssemblyFileExtensions")
        || HasName(element, "AssemblySearchPaths")
        || HasName(element, "AssemblyOriginatorKeyFile")
        || HasName(element, "DesignTimeAssemblySearchPaths")
        || HasName(element, "CodeAnalysisRuleSet")
        || HasName(element, "CodeAnalysisRuleSetDirectories")
        || HasName(element, "CryptoKeyContainer")
        || HasName(element, "CryptoKeyFile")
        || HasName(element, "DelaySign")
        || HasName(element, "KeyContainerName")
        || HasName(element, "KeyOriginatorFile")
        || HasName(element, "PublicSign")
        || HasName(element, "ResolvedCodeAnalysisRuleSet")
        || HasName(element, "SignAssembly");

    private static async Task<bool> HasUnprovenLoadedSigningInputsAsync(
        Solution? solution,
        CancellationToken cancellationToken)
    {
        if (solution is null)
            return false;

        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = project.CompilationOptions;
            if (options is not null
                && (!string.IsNullOrWhiteSpace(options.CryptoKeyFile)
                    || !string.IsNullOrWhiteSpace(options.CryptoKeyContainer)
                    || !options.CryptoPublicKey.IsDefaultOrEmpty
                    || options.DelaySign == true
                    || options.PublicSign))
            {
                return true;
            }

            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
                continue;

            foreach (var attribute in compilation.Assembly.GetAttributes())
            {
                var attributeType = attribute.AttributeClass;
                if (attributeType is not null
                    && attributeType.ContainingNamespace.ToDisplayString().Equals(
                        "System.Reflection",
                        StringComparison.Ordinal)
                    && (attributeType.Name.Equals(
                            "AssemblyKeyFileAttribute",
                            StringComparison.Ordinal)
                        || attributeType.Name.Equals(
                            "AssemblyKeyNameAttribute",
                            StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsLegacySdkExtensionCustomization(XElement element) =>
        HasName(element, "TargetFrameworkIdentifier")
        || HasName(element, "ResolveAssemblyConflicts")
        || HasName(element, "UsingMicrosoftNETSdk")
        || HasName(element, "MicrosoftNETBuildExtensionsTasksAssembly")
        || HasName(element, "PackageConflictPlatformManifests");

    private static bool IsUnprovenTargetFrameworkAssignment(XElement element) =>
        (HasName(element, "TargetFramework") || HasName(element, "TargetFrameworks"))
        && (HasConditionalEvaluation(element)
            || !AreDefinitelyModernTargetFrameworks(element.Value));

    private static bool IsProjectRelativeItem(XElement element) =>
        HasName(element, "Analyzer")
        || HasName(element, "AdditionalFiles")
        || HasName(element, "EditorConfigFiles")
        || HasName(element, "Compile")
        || HasName(element, "ProjectReference")
        || HasName(element, "Content")
        || HasName(element, "None")
        || HasName(element, "HintPath");

    private static bool HasName(XElement element, string name) =>
        element.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static string? GetAttributeValue(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => attribute.Name.LocalName.Equals(
                name,
                StringComparison.OrdinalIgnoreCase))
            ?.Value;

    private static bool IsEvaluationContextRelative(string candidate)
    {
        var normalized = candidate
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        return !Path.IsPathFullyQualified(normalized)
               && !candidate.Contains("$(MSBuildThisFileDirectory)", StringComparison.OrdinalIgnoreCase)
               && !candidate.Contains("$(MSBuildProjectDirectory)", StringComparison.OrdinalIgnoreCase)
               && !candidate.Contains("$(NuGetPackageRoot)", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveDeclaredPath(
        string candidate,
        string directory,
        string? projectDirectory,
        string? nuGetPackageRoot,
        out string fullPath)
    {
        var expanded = candidate.Replace(
            "$(MSBuildThisFileDirectory)",
            directory + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
        if (expanded.Contains("$(MSBuildProjectDirectory)", StringComparison.OrdinalIgnoreCase))
        {
            if (projectDirectory is null)
            {
                fullPath = string.Empty;
                return false;
            }

            expanded = expanded.Replace(
                "$(MSBuildProjectDirectory)",
                projectDirectory,
                StringComparison.OrdinalIgnoreCase);
        }

        if (expanded.Contains("$(NuGetPackageRoot)", StringComparison.OrdinalIgnoreCase))
        {
            if (nuGetPackageRoot is null)
            {
                fullPath = string.Empty;
                return false;
            }

            expanded = expanded.Replace(
                "$(NuGetPackageRoot)",
                nuGetPackageRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }

        expanded = expanded
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (IsUnsupportedDeclaredPath(expanded))
        {
            fullPath = string.Empty;
            return false;
        }

        fullPath = Path.GetFullPath(expanded, directory);
        return true;
    }

    private static bool IsUnsupportedDeclaredPath(string? value) =>
        string.IsNullOrWhiteSpace(value)
        || value.Contains("$(", StringComparison.Ordinal)
        || value.Contains("@(", StringComparison.Ordinal)
        || value.Contains("%(", StringComparison.Ordinal)
        || MsBuildEscapeRegex.IsMatch(value)
        || value.IndexOfAny(['*', '?']) >= 0;

    private static string? ResolveNuGetPackageRoot(
        XDocument document,
        string buildInput,
        string? environmentPackageRoot)
    {
        var assignments = document.Descendants()
            .Where(element => element.Name.LocalName.Equals(
                "NuGetPackageRoot",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (IsGeneratedNuGetBuildInput(buildInput)
            && !string.IsNullOrWhiteSpace(environmentPackageRoot)
            && assignments.All(HasConditionalEvaluation))
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(environmentPackageRoot));
        }

        var value = assignments
            .Select(element => element.Value.Trim())
            .LastOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)
                                        && !candidate.Contains("$(", StringComparison.Ordinal));
        if (value is not null)
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));

        if (!string.IsNullOrWhiteSpace(environmentPackageRoot))
        {
            return Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(environmentPackageRoot));
        }

        if (!buildInput.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase))
            return null;

        var propsPath = buildInput[..^"targets".Length] + "props";
        if (!File.Exists(propsPath))
            return null;

        using var stream = new FileStream(
            propsPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        });
        var props = XDocument.Load(reader, LoadOptions.None);
        value = props.Descendants()
            .Where(element => element.Name.LocalName.Equals(
                "NuGetPackageRoot",
                StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Value.Trim())
            .LastOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate)
                                        && !candidate.Contains("$(", StringComparison.Ordinal));
        return value is null
            ? null
            : Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static bool TryAddNuGetPackageInputs(
        HashSet<string> paths,
        string nuGetPackageRoot,
        string referencedPath)
    {
        if (!IsPathWithinRoot(referencedPath, nuGetPackageRoot))
            return false;

        var relative = Path.GetRelativePath(nuGetPackageRoot, referencedPath);
        var segments = relative.Split(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar,
            StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
            return false;

        var packageDirectory = Path.Combine(nuGetPackageRoot, segments[0], segments[1]);
        if (!Directory.Exists(packageDirectory))
            return true;

        foreach (var path in Directory.EnumerateFiles(packageDirectory, "*", SearchOption.AllDirectories))
        {
            var fullPath = Path.GetFullPath(path);
            paths.Add(fullPath);
        }

        return true;
    }

    private static void AddAncestorConfiguration(HashSet<string> paths, string startDirectory)
    {
        string[] names =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "Directory.Packages.targets",
            "global.json",
            ".editorconfig",
            ".globalconfig",
            "NuGet.Config",
            "nuget.config"
        ];
        for (var directory = new DirectoryInfo(startDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            foreach (var name in names)
                paths.Add(Path.Combine(directory.FullName, name));
        }
    }

    private static bool IsBuildInput(string path) => IsProjectInput(path)
        || (Path.GetExtension(path) is var extension
            && (extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)));

    private static bool IsProjectInput(string path) => Path.GetExtension(path) is var extension
        && (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase));

    private static bool ShouldDiscoverDeclaredInputs(string path) => IsGeneratedNuGetBuildInput(path)
        || !path
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                            || segment.Equals("bin", StringComparison.OrdinalIgnoreCase));

    private static bool IsGeneratedNuGetBuildInput(string path) =>
        path.EndsWith(".nuget.g.props", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase);

    private async Task<SemanticInputDiscovery> ReadGitSemanticInputsAsync(
        string worktreeRoot,
        IEnumerable<string> projectDirectories,
        CancellationToken cancellationToken)
    {
        var paths = new HashSet<string>(PathComparer);
        var values = new HashSet<string>(StringComparer.Ordinal);
        var configurationDigests = new Dictionary<string, string?>(PathComparer);
        var sourceLinkConfigurations = new Dictionary<string, bool>(PathComparer);
        var reusable = true;
        foreach (var startDirectory in projectDirectories
                     .Append(worktreeRoot)
                     .Select(Path.GetFullPath)
                     .Distinct(PathComparer))
        {
            for (var current = new DirectoryInfo(startDirectory);
                 current is not null;
                 current = current.Parent)
            {
                var marker = Path.Combine(current.FullName, ".git");
                paths.Add(marker);
                if (!File.Exists(marker) && !Directory.Exists(marker))
                    continue;

                var state = AddGitRepositoryInputs(marker, paths, out var repository);
                if (state == GitRepositoryInputState.Invalid)
                    continue;
                if (state == GitRepositoryInputState.Unproven || repository is null)
                {
                    reusable = false;
                    break;
                }

                if (!sourceLinkConfigurations.TryGetValue(
                        repository.GitDirectory,
                        out var sourceLinkConfigurationReusable))
                {
                    sourceLinkConfigurationReusable = await AddSourceLinkConfigurationInputsAsync(
                        repository,
                        paths,
                        cancellationToken);
                    sourceLinkConfigurations.Add(
                        repository.GitDirectory,
                        sourceLinkConfigurationReusable);
                }
                reusable &= sourceLinkConfigurationReusable;

                if (!configurationDigests.TryGetValue(repository.GitDirectory, out var digest))
                {
                    digest = await CaptureEffectiveGitConfigurationAsync(
                        repository,
                        cancellationToken);
                    configurationDigests.Add(repository.GitDirectory, digest);
                }

                if (digest is null)
                    reusable = false;
                else
                    values.Add($"git-config\0{repository.GitDirectory}\0{digest}");
                break;
            }
        }

        return new SemanticInputDiscovery(
            paths.OrderBy(path => path, PathComparer).ToArray(),
            reusable,
            values.OrderBy(value => value, StringComparer.Ordinal).ToArray());
    }

    private static GitRepositoryInputState AddGitRepositoryInputs(
        string marker,
        HashSet<string> paths,
        out GitRepositoryLocation? repository)
    {
        repository = null;
        var gitDirectory = marker;
        if (File.Exists(marker))
        {
            if (!TryReadGitControlFile(marker, out var markerText))
                return GitRepositoryInputState.Unproven;
            if (!markerText.StartsWith("gitdir:", StringComparison.OrdinalIgnoreCase))
                return GitRepositoryInputState.Invalid;

            var configured = markerText["gitdir:".Length..].Trim();
            if (string.IsNullOrWhiteSpace(configured))
                return GitRepositoryInputState.Invalid;
            try
            {
                gitDirectory = Path.GetFullPath(configured, Path.GetDirectoryName(marker)!);
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
            {
                return GitRepositoryInputState.Invalid;
            }
        }

        paths.Add(gitDirectory);
        if (!Directory.Exists(gitDirectory))
            return GitRepositoryInputState.Invalid;

        var commonDirectory = gitDirectory;
        var commonDirectoryFile = Path.Combine(gitDirectory, "commondir");
        paths.Add(commonDirectoryFile);
        if (File.Exists(commonDirectoryFile))
        {
            if (!TryReadGitControlFile(commonDirectoryFile, out var configured))
                return GitRepositoryInputState.Unproven;
            if (string.IsNullOrWhiteSpace(configured))
                return GitRepositoryInputState.Invalid;

            try
            {
                commonDirectory = Path.GetFullPath(configured.Trim(), gitDirectory);
            }
            catch (Exception exception) when (exception is ArgumentException
                                               or NotSupportedException
                                               or PathTooLongException)
            {
                return GitRepositoryInputState.Invalid;
            }

            paths.Add(commonDirectory);
            if (!Directory.Exists(commonDirectory))
                return GitRepositoryInputState.Invalid;
        }

        var headPath = Path.Combine(gitDirectory, "HEAD");
        paths.Add(headPath);
        if (!File.Exists(headPath))
            return GitRepositoryInputState.Invalid;
        if (!TryReadGitControlFile(headPath, out var head))
            return GitRepositoryInputState.Unproven;
        if (string.IsNullOrWhiteSpace(head))
            return GitRepositoryInputState.Invalid;

        paths.Add(Path.Combine(gitDirectory, "packed-refs"));
        paths.Add(Path.Combine(commonDirectory, "packed-refs"));
        var gitReftable = Path.Combine(gitDirectory, "reftable");
        var commonReftable = Path.Combine(commonDirectory, "reftable");
        paths.Add(gitReftable);
        paths.Add(commonReftable);
        if (Directory.Exists(gitReftable) || Directory.Exists(commonReftable))
            return GitRepositoryInputState.Unproven;

        var trimmedHead = head.Trim();
        if (!trimmedHead.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsGitObjectId(trimmedHead))
                return GitRepositoryInputState.Invalid;

            repository = new GitRepositoryLocation(
                Path.GetDirectoryName(marker)!,
                gitDirectory,
                commonDirectory);
            return GitRepositoryInputState.Valid;
        }

        var pendingRefs = new Queue<(string Name, int Depth)>();
        pendingRefs.Enqueue((trimmedHead["ref:".Length..].Trim(), 0));
        var visitedRefs = new HashSet<string>(StringComparer.Ordinal);
        while (pendingRefs.TryDequeue(out var pending))
        {
            if (pending.Depth >= 8 || !visitedRefs.Add(pending.Name))
                return GitRepositoryInputState.Unproven;
            if (!TryResolveGitRefPaths(
                    pending.Name,
                    gitDirectory,
                    commonDirectory,
                    out var refPaths))
            {
                return GitRepositoryInputState.Invalid;
            }

            foreach (var refPath in refPaths)
            {
                paths.Add(refPath);
                if (!File.Exists(refPath))
                    continue;
                if (!TryReadGitControlFile(refPath, out var refText))
                    return GitRepositoryInputState.Unproven;
                var trimmedRef = refText.Trim();
                if (trimmedRef.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
                {
                    pendingRefs.Enqueue((
                        trimmedRef["ref:".Length..].Trim(),
                        pending.Depth + 1));
                }
                else if (!IsGitObjectId(trimmedRef))
                {
                    return GitRepositoryInputState.Invalid;
                }
            }
        }

        repository = new GitRepositoryLocation(
            Path.GetDirectoryName(marker)!,
            gitDirectory,
            commonDirectory);
        return GitRepositoryInputState.Valid;
    }

    private static bool IsGitObjectId(string value) =>
        (value.Length == 40 || value.Length == 64)
        && value.All(character => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F');

    private async Task<bool> AddSourceLinkConfigurationInputsAsync(
        GitRepositoryLocation repository,
        HashSet<string> paths,
        CancellationToken cancellationToken)
    {
        if (!TryGetSourceLinkConfigurationRoots(repository, paths, out var roots))
            return false;

        var processed = new HashSet<string>(PathComparer);
        var visiting = new HashSet<string>(PathComparer);
        foreach (var root in roots)
        {
            if (!await AddSourceLinkConfigurationGraphAsync(
                    root,
                    depth: 0,
                    paths,
                    processed,
                    visiting,
                    cancellationToken))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetSourceLinkConfigurationRoots(
        GitRepositoryLocation repository,
        HashSet<string> paths,
        out string[] roots)
    {
        var candidates = new List<string>();
        var userProfile = _getUserProfileDirectory();
        if (!string.IsNullOrWhiteSpace(userProfile)
            && !TryNormalizeAbsoluteDirectory(userProfile, out userProfile))
        {
            roots = [];
            return false;
        }

        if (!TryAddSourceLinkConfigurationRoot(
                candidates,
                GetEnvironmentProperty("PROGRAMDATA"),
                "git",
                "config"))
        {
            roots = [];
            return false;
        }

        if (!TryGetSourceLinkSystemConfigurationDirectory(paths, out var systemDirectory)
            || !TryAddSourceLinkConfigurationRoot(
                candidates,
                systemDirectory,
                "gitconfig"))
        {
            roots = [];
            return false;
        }

        var xdgConfigHome = GetEnvironmentProperty("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
        {
            if (!TryAddSourceLinkConfigurationRoot(
                    candidates,
                    xdgConfigHome,
                    "git",
                    "config"))
            {
                roots = [];
                return false;
            }
        }
        else if (!TryAddSourceLinkConfigurationRoot(
                     candidates,
                     userProfile,
                     ".config",
                     "git",
                     "config"))
        {
            roots = [];
            return false;
        }

        if (!TryAddSourceLinkConfigurationRoot(candidates, userProfile, ".gitconfig")
            || !TryAddSourceLinkConfigurationRoot(
                candidates,
                repository.CommonDirectory,
                "config"))
        {
            roots = [];
            return false;
        }

        roots = candidates.Distinct(PathComparer).ToArray();
        paths.UnionWith(roots);
        return true;
    }

    private bool TryGetSourceLinkSystemConfigurationDirectory(
        HashSet<string> paths,
        out string? systemDirectory)
    {
        if (!OperatingSystem.IsWindows())
        {
            var configured = GetEnvironmentProperty(
                "MICROSOFT_SOURCELINK_TEST_ENVIRONMENT_ETC_DIR");
            if (string.IsNullOrWhiteSpace(configured))
            {
                systemDirectory = "/etc";
                return true;
            }

            return TryNormalizeAbsoluteDirectory(configured, out systemDirectory);
        }

        var configuredPathEntries = (GetEnvironmentProperty("PATH") ?? string.Empty)
            .Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        var pathEntries = new string[configuredPathEntries.Length];
        for (var index = 0; index < configuredPathEntries.Length; index++)
        {
            if (!TryNormalizeAbsoluteDirectory(
                    configuredPathEntries[index],
                    out var normalizedPathEntry))
            {
                systemDirectory = null;
                return false;
            }

            pathEntries[index] = normalizedPathEntry!;
        }

        var executableCandidates = pathEntries
            .Select(path => Path.Combine(path, "git.exe"))
            .ToArray();
        var commandCandidates = pathEntries
            .Select(path => Path.Combine(path, "git.cmd"))
            .ToArray();
        paths.UnionWith(executableCandidates);
        paths.UnionWith(commandCandidates);
        var selectedPathDirectory = executableCandidates
                                        .Select((path, index) => (path, index))
                                        .Where(candidate => File.Exists(candidate.path))
                                        .Select(candidate => pathEntries[candidate.index])
                                        .FirstOrDefault()
                                    ?? commandCandidates
                                        .Select((path, index) => (path, index))
                                        .Where(candidate => File.Exists(candidate.path))
                                        .Select(candidate => pathEntries[candidate.index])
                                        .FirstOrDefault();
        if (selectedPathDirectory is null)
        {
            systemDirectory = null;
            return true;
        }

        var installDirectory = Path.GetDirectoryName(selectedPathDirectory);
        if (installDirectory is null)
        {
            systemDirectory = null;
            return true;
        }

        var defaultEtc = Path.GetFullPath(Path.Combine(installDirectory, "etc"));
        var mingwEtc = Path.GetFullPath(Path.Combine(defaultEtc, "..", "mingw64", "etc"));
        paths.Add(defaultEtc);
        paths.Add(mingwEtc);
        systemDirectory = Directory.Exists(mingwEtc) ? mingwEtc : defaultEtc;
        return true;
    }

    private static bool TryAddSourceLinkConfigurationRoot(
        List<string> candidates,
        string? directory,
        params string[] relativeSegments)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        if (!TryNormalizeAbsoluteDirectory(directory, out var normalizedDirectory))
            return false;

        try
        {
            candidates.Add(Path.GetFullPath(Path.Combine(
                [normalizedDirectory!, .. relativeSegments])));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryNormalizeAbsoluteDirectory(
        string directory,
        out string? normalizedDirectory)
    {
        normalizedDirectory = null;
        if (!Path.IsPathFullyQualified(directory))
            return false;
        try
        {
            normalizedDirectory = Path.GetFullPath(directory);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    private async Task<bool> AddSourceLinkConfigurationGraphAsync(
        string path,
        int depth,
        HashSet<string> paths,
        HashSet<string> processed,
        HashSet<string> visiting,
        CancellationToken cancellationToken)
    {
        paths.Add(path);
        if (Directory.Exists(path) || depth > 10)
            return false;
        if (!File.Exists(path) || processed.Contains(path))
            return true;
        if (!visiting.Add(path))
            return false;

        try
        {
            var declaredIncludes = await CaptureDeclaredGitIncludesAsync(path, cancellationToken);
            if (declaredIncludes is null)
                return false;
            foreach (var declaredInclude in declaredIncludes)
            {
                if (!TryResolveSourceLinkIncludePath(
                        path,
                        declaredInclude,
                        out var includedPath)
                    || !await AddSourceLinkConfigurationGraphAsync(
                        includedPath,
                        depth + 1,
                        paths,
                        processed,
                        visiting,
                        cancellationToken))
                {
                    return false;
                }
            }

            processed.Add(path);
            return true;
        }
        finally
        {
            visiting.Remove(path);
        }
    }

    private bool TryResolveSourceLinkIncludePath(
        string declaringConfigPath,
        string declaredInclude,
        out string includedPath)
    {
        includedPath = string.Empty;
        string root;
        if (declaredInclude.Length >= 2
            && declaredInclude[0] == '~'
            && declaredInclude[1] is '/' or '\\')
        {
            var userProfile = _getUserProfileDirectory();
            if (string.IsNullOrWhiteSpace(userProfile)
                || !TryNormalizeAbsoluteDirectory(userProfile, out var normalizedProfile))
            {
                return false;
            }

            root = normalizedProfile!;
            declaredInclude = declaredInclude[2..];
        }
        else
        {
            root = Path.GetDirectoryName(declaringConfigPath)!;
        }

        try
        {
            includedPath = Path.GetFullPath(Path.Combine(root, declaredInclude));
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }
    }

    private static async Task<string[]?> CaptureDeclaredGitIncludesAsync(
        string configPath,
        CancellationToken cancellationToken)
    {
        var result = await RunGitProcessAsync(
            [
                "config",
                "--file",
                configPath,
                "--no-includes",
                "--null",
                "--get-regexp",
                "^(include\\.path|includeif\\..*\\.path)$"
            ],
            cancellationToken);
        if (result is null || result.ExitCode is not (0 or 1))
            return null;
        if (result.ExitCode == 1)
            return [];

        string output;
        try
        {
            output = new UTF8Encoding(false, true).GetString(result.Output);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }

        var includes = new List<string>();
        foreach (var entry in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('\n');
            if (separator < 0)
                return null;
            includes.Add(entry[(separator + 1)..]);
        }

        return includes.ToArray();
    }

    private static async Task<string?> CaptureEffectiveGitConfigurationAsync(
        GitRepositoryLocation repository,
        CancellationToken cancellationToken)
    {
        var result = await RunGitProcessAsync(
            [
                "--git-dir",
                repository.GitDirectory,
                "--work-tree",
                repository.WorktreeRoot,
                "config",
                "--includes",
                "--show-origin",
                "--show-scope",
                "--null",
                "--list"
            ],
            cancellationToken);
        if (result is null || result.ExitCode != 0)
            return null;

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(result.Output);
        hash.AppendData(result.Error);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static async Task<GitProcessResult?> RunGitProcessAsync(
        IEnumerable<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        try
        {
            if (!process.Start())
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var registration = timeout.Token.Register(
                static state => TryTerminateProcess((Process)state!),
                process);
            var outputTask = ReadBoundedProcessOutputAsync(
                process.StandardOutput.BaseStream,
                4 * 1024 * 1024,
                process,
                timeout.Token);
            var errorTask = ReadBoundedProcessOutputAsync(
                process.StandardError.BaseStream,
                256 * 1024,
                process,
                timeout.Token);
            await Task.WhenAll(
                process.WaitForExitAsync(timeout.Token),
                outputTask,
                errorTask);
            return new GitProcessResult(
                process.ExitCode,
                await outputTask,
                await errorTask);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                           or IOException
                                           or InvalidOperationException
                                           or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            TryTerminateProcess(process);
        }
    }

    private static async Task<byte[]> ReadBoundedProcessOutputAsync(
        Stream stream,
        int maximumBytes,
        Process process,
        CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                return output.ToArray();
            if (output.Length + read > maximumBytes)
            {
                TryTerminateProcess(process);
                throw new InvalidDataException("Git configuration output exceeded the safe capture limit.");
            }

            output.Write(buffer, 0, read);
        }
    }

    private static void TryTerminateProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException
                                           or NotSupportedException
                                           or System.ComponentModel.Win32Exception)
        {
            // The process either never started or exited between the check and the kill.
        }
    }

    private static bool TryResolveGitRefPaths(
        string referenceName,
        string gitDirectory,
        string commonDirectory,
        out string[] paths)
    {
        paths = [];
        if (!referenceName.StartsWith("refs/", StringComparison.Ordinal)
            || referenceName.Contains('\\')
            || referenceName.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        var relativePath = referenceName.Replace('/', Path.DirectorySeparatorChar);
        string gitPath;
        string commonPath;
        try
        {
            gitPath = Path.GetFullPath(relativePath, gitDirectory);
            commonPath = Path.GetFullPath(relativePath, commonDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException
                                           or NotSupportedException
                                           or PathTooLongException)
        {
            return false;
        }

        if (!IsPathWithinRoot(gitPath, gitDirectory)
            || !IsPathWithinRoot(commonPath, commonDirectory))
        {
            return false;
        }

        paths = PathComparer.Equals(gitPath, commonPath)
            ? [gitPath]
            : [gitPath, commonPath];
        return true;
    }

    private static bool TryReadGitControlFile(string path, out string contents)
    {
        contents = string.Empty;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 1024 * 1024)
                return false;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);
            contents = reader.ReadToEnd();
            return true;
        }
        catch (Exception exception) when (exception is IOException
                                           or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasExistingProjectOutsideWorktree(
        TrustedWorkspaceIdentity identity,
        Solution? solution) => solution?.Projects.Any(project => project.FilePath is { } path
            && File.Exists(path)
            && !IsPathWithinRoot(path, identity.WorktreeRoot)) == true;

    private static bool IsPathWithinRoot(string path, string root)
    {
        var relative = Path.GetRelativePath(root, Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private sealed record SemanticInputDiscovery(
        IReadOnlyList<string> Paths,
        bool Reusable,
        IReadOnlyList<string>? Values = null);

    private enum GitRepositoryInputState
    {
        Valid,
        Invalid,
        Unproven
    }

    private sealed record GitRepositoryLocation(
        string WorktreeRoot,
        string GitDirectory,
        string CommonDirectory);

    private sealed record GitProcessResult(int ExitCode, byte[] Output, byte[] Error);

    private sealed record DeclaredBuildInputs(IReadOnlySet<string> Paths, bool Reusable);

    private sealed record SystemHookInputs(
        IReadOnlySet<string> Paths,
        bool Reusable);

    private static HashSet<string> ReadDefaultProjectOutputDirectories(
        IEnumerable<string> projectPaths)
    {
        var projects = new List<ProjectOutputExclusions>();
        foreach (var projectPath in projectPaths)
        {
            var excludeBin = true;
            var excludeObj = true;
            if (File.Exists(projectPath))
            {
                try
                {
                    using var stream = new FileStream(
                        projectPath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var reader = XmlReader.Create(stream, new XmlReaderSettings
                    {
                        DtdProcessing = DtdProcessing.Prohibit,
                        XmlResolver = null
                    });
                    var document = XDocument.Load(reader, LoadOptions.None);
                    var customizations = document.Descendants()
                        .Where(IsDefaultOutputCustomization)
                        .Select(element => element.Name.LocalName)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var customizesDefaultItems = customizations.Overlaps(
                    [
                        "DefaultItemExcludes",
                        "DefaultExcludesInProjectFolder",
                        "EnableDefaultItems",
                        "EnableDefaultCompileItems"
                    ]);
                    excludeBin = !customizesDefaultItems
                                 && !customizations.Contains("BaseOutputPath")
                                 && !customizations.Contains("OutputPath");
                    excludeObj = !customizesDefaultItems
                                 && !customizations.Contains("BaseIntermediateOutputPath")
                                 && !customizations.Contains("MSBuildProjectExtensionsPath");
                }
                catch (XmlException exception)
                {
                    throw new IOException(
                        $"Workspace project input could not be parsed consistently: '{projectPath}'.",
                        exception);
                }
            }

            var projectDirectory = Path.GetDirectoryName(projectPath)!;
            var excludedDirectories = new HashSet<string>(PathComparer);
            if (excludeBin)
                excludedDirectories.Add(Path.Combine(projectDirectory, "bin"));
            if (excludeObj)
                excludedDirectories.Add(Path.Combine(projectDirectory, "obj"));
            projects.Add(new ProjectOutputExclusions(projectDirectory, excludedDirectories));
        }

        var paths = new HashSet<string>(PathComparer);
        foreach (var candidate in projects.SelectMany(project => project.ExcludedDirectories))
        {
            var potentiallyConsumingProjects = projects.Where(project =>
                IsPathWithinRoot(candidate, project.ProjectDirectory));
            if (potentiallyConsumingProjects.All(project =>
                    project.ExcludedDirectories.Contains(candidate)))
            {
                paths.Add(candidate);
            }
        }

        return paths;
    }

    private sealed record ProjectOutputExclusions(
        string ProjectDirectory,
        HashSet<string> ExcludedDirectories);

    private static IEnumerable<string> EnumerateRelevantRepositoryFiles(
        string root,
        HashSet<string> excludedDirectories)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory)
                         .OrderBy(path => path, PathComparer))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child))
                    && !excludedDirectories.Contains(Path.GetFullPath(child)))
                {
                    pending.Push(child);
                }
            }

            foreach (var path in Directory.EnumerateFiles(directory)
                         .OrderBy(path => path, PathComparer))
            {
                if (RelevantExtensions.Contains(Path.GetExtension(path)))
                    yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateSolutionInputs(Solution solution)
    {
        foreach (var project in solution.Projects)
        {
            if (project.FilePath is not null)
                yield return project.FilePath;
            foreach (var document in project.Documents
                         .Concat<TextDocument>(project.AdditionalDocuments)
                         .Concat(project.AnalyzerConfigDocuments))
            {
                if (document.FilePath is not null)
                    yield return document.FilePath;
            }

            foreach (var analyzer in project.AnalyzerReferences)
            {
                if (!string.IsNullOrWhiteSpace(analyzer.FullPath))
                    yield return analyzer.FullPath;
            }

            foreach (var metadata in project.MetadataReferences)
            {
                if (!string.IsNullOrWhiteSpace(metadata.Display))
                    yield return metadata.Display;
            }
        }
    }

    private static async Task<bool> IsLoadedSolutionConsistentAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        foreach (var document in solution.Projects.SelectMany(project => project.Documents
                     .Concat<TextDocument>(project.AdditionalDocuments)
                     .Concat(project.AnalyzerConfigDocuments)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.FilePath is null)
                continue;
            if (!await IsLoadedDocumentConsistentAsync(document, cancellationToken))
                return false;
        }

        return true;
    }

    private static async Task<bool> IsLoadedDocumentConsistentAsync(
        TextDocument document,
        CancellationToken cancellationToken)
    {
        var path = document.FilePath!;
        var before = new FileInfo(path);
        if (!before.Exists)
            return false;

        var beforeLength = before.Length;
        var beforeWrite = before.LastWriteTimeUtc;
        var loadedText = await document.GetTextAsync(cancellationToken);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            // Stryker disable once Bitwise : these are performance hints and do not change read semantics.
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var physicalText = SourceText.From(
            stream,
            loadedText.Encoding,
            loadedText.ChecksumAlgorithm);
        var after = new FileInfo(path);
        EnsureFileUnchanged(
            after.Exists,
            beforeLength,
            after.Length,
            beforeWrite,
            after.LastWriteTimeUtc,
            path);
        return loadedText.ContentEquals(physicalText);
    }

    private static async Task AppendStableFileAsync(
        IncrementalHash hash,
        string path,
        CancellationToken cancellationToken)
    {
        var before = new FileInfo(path);
        var beforeLength = before.Length;
        var beforeWrite = before.LastWriteTimeUtc;
        // Stryker disable once Arithmetic : the buffer size affects throughput, not the bytes hashed.
        const int bufferSize = 64 * 1024;
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize,
            // Stryker disable once Bitwise : these are performance hints and do not change read semantics.
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[bufferSize];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            hash.AppendData(buffer.AsSpan(0, read));
        }

        var after = new FileInfo(path);
        EnsureFileUnchanged(
            after.Exists,
            beforeLength,
            after.Length,
            beforeWrite,
            after.LastWriteTimeUtc,
            path);
    }

    internal static void EnsureFileUnchanged(
        bool afterExists,
        long beforeLength,
        long afterLength,
        DateTime beforeWrite,
        DateTime afterWrite,
        string path)
    {
        if (!afterExists
            || beforeLength != afterLength
            || beforeWrite != afterWrite)
        {
            throw new IOException($"Workspace input changed while it was being read: '{path}'.");
        }
    }

    private static void Append(IncrementalHash hash, string? value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(value ?? string.Empty));
        hash.AppendData([0]);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

/// <summary>
///     A bounded cache of read-only Roslyn snapshots. A cache entry is retired before
///     replacement or eviction and its workspace is disposed only after the last lease exits.
/// </summary>
internal sealed class ReadOnlyWorkspaceSessionCache : IReadOnlyWorkspaceSessions, IDisposable
{
    internal const int DefaultCapacity = 4;

    private readonly ConcurrentDictionary<WorkspaceCacheKey, CacheSlot> _slots = new();
    private readonly IWorkspaceSnapshotLoader _loader;
    private readonly IWorkspaceInputFingerprint _fingerprint;
    private readonly IWorkspaceCacheTelemetry _telemetry;
    private readonly ReadOnlyWorkspaceCacheTestHooks? _testHooks;
    private readonly int _capacity;
    private readonly SemaphoreSlim _retirementGate = new(1, 1);
    private long _accessSequence;
    private long _loadCount;
    private int _disposed;

    internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    internal ReadOnlyWorkspaceSessionCache(
        IWorkspaceSnapshotLoader loader,
        IWorkspaceInputFingerprint fingerprint,
        IWorkspaceCacheTelemetry telemetry,
        int capacity = DefaultCapacity,
        ReadOnlyWorkspaceCacheTestHooks? testHooks = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _loader = loader;
        _fingerprint = fingerprint;
        _telemetry = telemetry;
        _capacity = capacity;
        _testHooks = testHooks;
    }

    Task<ReadOnlyWorkspaceLease> IReadOnlyWorkspaceSessions.AcquireAsync(
        string? solutionPath,
        CancellationToken cancellationToken) => AcquireAsync(solutionPath, cancellationToken);

    internal async Task<ReadOnlyWorkspaceLease> AcquireAsync(
        string? solutionPath,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (IWorkspaceSessionFactory.AmbientOverride is { } ambient)
        {
            var ambientRevision = await SolutionRevision.ComputeAsync(
                ambient.Solution,
                cancellationToken);
            var ambientCompleteness = await WorkspaceCompleteness.AssessSemanticAsync(
                ambient.Solution,
                cancellationToken);
            return new ReadOnlyWorkspaceLease(ambient.Solution, ambientRevision, ambientCompleteness);
        }

        var resolvedPath = SolutionPathResolver.Resolve(solutionPath);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Solution not found: {resolvedPath}", resolvedPath);

        var identity = RepositoryTrustContext.ResolveWorkspaceIdentity(resolvedPath);
        var environmentIdentity = _fingerprint.CaptureEnvironmentIdentity();
        var key = new WorkspaceCacheKey(
            WorkspaceCacheKey.NormalizePath(identity.SolutionPath),
            identity.RepositoryId,
            WorkspaceCacheKey.NormalizePath(identity.WorktreeRoot),
            environmentIdentity);
        while (true)
        {
            var slot = _slots.GetOrAdd(key, static _ => new CacheSlot());
            ReadOnlyWorkspaceLease lease;
            _testHooks?.BeforeSlotWait?.Invoke();
            await slot.Gate.WaitAsync(cancellationToken);
            try
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    if (_slots.TryRemove(new KeyValuePair<WorkspaceCacheKey, CacheSlot>(key, slot)))
                        slot.Current?.Retire();
                    throw new ObjectDisposedException(nameof(ReadOnlyWorkspaceSessionCache));
                }

                if (!_slots.TryGetValue(key, out var admittedSlot))
                    continue;
                if (!ReferenceEquals(admittedSlot, slot))
                    continue;

                identity = RepositoryTrustContext.ResolveWorkspaceIdentity(resolvedPath);
                if (!key.Matches(identity, _fingerprint.CaptureEnvironmentIdentity()))
                {
                    if (_slots.TryRemove(new KeyValuePair<WorkspaceCacheKey, CacheSlot>(key, slot)))
                        slot.Current?.Retire();
                    throw new IOException(
                        "The trusted workspace identity or semantic environment changed during cache admission.");
                }

                var current = slot.Current;
                WorkspaceInputFingerprintResult? currentFingerprint = null;
                try
                {
                    if (current is not null)
                    {
                        currentFingerprint = await _fingerprint.CaptureAsync(
                            identity,
                            current.Solution,
                            cancellationToken);
                    }
                }
                catch (Exception exception) when (exception is IOException
                                                   or UnauthorizedAccessException)
                {
                }

                if (current is { Reusable: true }
                    && currentFingerprint is
                    {
                        Reusable: true,
                        SnapshotConsistent: true
                    } captured
                    && string.Equals(current.Fingerprint, captured.Value, StringComparison.Ordinal))
                {
                    slot.LastAccess = Interlocked.Increment(ref _accessSequence);
                    lease = current.Lease();
                    _telemetry.Record(new WorkspaceCacheEvent(
                        WorkspaceCacheEventKind.Hit,
                        key.IdentityHash,
                        current.SourceRevision,
                        Volatile.Read(ref _loadCount)));
                }
                else
                {
                    var beforeLoadFingerprint = await _fingerprint.CaptureAsync(
                        identity,
                        solution: null,
                        cancellationToken);

                    var replacement = await LoadFreshEntryAsync(
                        key,
                        identity,
                        beforeLoadFingerprint,
                        cancellationToken);
                    slot.Current = replacement;
                    slot.LastAccess = Interlocked.Increment(ref _accessSequence);
                    current?.Retire();
                    var loadCount = Interlocked.Increment(ref _loadCount);
                    _telemetry.Record(new WorkspaceCacheEvent(
                        current is null ? WorkspaceCacheEventKind.Load : WorkspaceCacheEventKind.Refresh,
                        key.IdentityHash,
                        replacement.SourceRevision,
                        loadCount));
                    lease = replacement.Lease();
                }
            }
            catch
            {
                if (slot.Current is null)
                    _slots.TryRemove(new KeyValuePair<WorkspaceCacheKey, CacheSlot>(key, slot));
                throw;
            }
            finally
            {
                slot.Gate.Release();
            }

            try
            {
                await TrimAsync(key, cancellationToken);
                lease.AttachCompleteness(await WorkspaceCompleteness.AssessSemanticAsync(
                    lease.Solution,
                    cancellationToken));
                return lease;
            }
            catch
            {
                lease.Dispose();
                throw;
            }
        }
    }

    void IDisposable.Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _retirementGate.Wait();
        try
        {
            foreach (var pair in _slots.ToArray())
            {
                pair.Value.Gate.Wait();
                try
                {
                    if (_slots.TryRemove(pair))
                        pair.Value.Current?.Retire();
                }
                finally
                {
                    pair.Value.Gate.Release();
                }
            }
        }
        finally
        {
            _retirementGate.Release();
        }
    }

    private async Task<CacheEntry> LoadFreshEntryAsync(
        WorkspaceCacheKey key,
        TrustedWorkspaceIdentity identity,
        WorkspaceInputFingerprintResult beforeRepositoryFingerprint,
        CancellationToken cancellationToken)
    {
        var lastFailure = "input stability could not be established";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var snapshot = await _loader.LoadOwnedWorkspaceAsync(
                identity.SolutionPath,
                cancellationToken);
            var owner = snapshot.Owner;
            var solution = snapshot.Solution;
            try
            {
                var sourceRevision = await SolutionRevision.ComputeAsync(solution, cancellationToken);
                var afterRepositoryFingerprint = await _fingerprint.CaptureAsync(
                    identity,
                    solution: null,
                    cancellationToken);
                if (string.Equals(
                        beforeRepositoryFingerprint.Value,
                        afterRepositoryFingerprint.Value,
                        StringComparison.Ordinal))
                {
                    var fullFingerprint = await _fingerprint.CaptureAsync(
                        identity,
                        solution,
                        cancellationToken);
                    var finalRepositoryFingerprint = await _fingerprint.CaptureAsync(
                        identity,
                        solution: null,
                        cancellationToken);
                    if (string.Equals(
                            afterRepositoryFingerprint.Value,
                            finalRepositoryFingerprint.Value,
                            StringComparison.Ordinal)
                        && fullFingerprint.SnapshotConsistent)
                    {
                        return new CacheEntry(
                            owner,
                            solution,
                            sourceRevision,
                            fullFingerprint.Value,
                            beforeRepositoryFingerprint.Reusable
                            && afterRepositoryFingerprint.Reusable
                            && fullFingerprint.Reusable
                            && finalRepositoryFingerprint.Reusable,
                            () => _telemetry.Record(new WorkspaceCacheEvent(
                                WorkspaceCacheEventKind.Dispose,
                                key.IdentityHash,
                                sourceRevision,
                                Volatile.Read(ref _loadCount))));
                    }

                    lastFailure = fullFingerprint.SnapshotConsistent
                        ? "repository inputs changed after the loaded snapshot was checked"
                        : "the loaded solution did not match its physical document inputs";

                    beforeRepositoryFingerprint = finalRepositoryFingerprint;
                }
                else
                {
                    lastFailure = "repository inputs changed while the solution was loading";
                    beforeRepositoryFingerprint = afterRepositoryFingerprint;
                }
            }
            catch
            {
                owner.Dispose();
                throw;
            }

            owner.Dispose();
        }

        throw new IOException(
            $"Workspace inputs for '{identity.SolutionPath}' changed repeatedly during load; "
            + $"no stable snapshot was served ({lastFailure}).");
    }

    private async Task TrimAsync(
        WorkspaceCacheKey protectedKey,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _testHooks?.BeforeRetirementWait?.Invoke();
        await _retirementGate.WaitAsync(cancellationToken);
        try
        {
            while (_slots.Count > _capacity)
            {
                var candidates = _slots
                    .Where(pair => !pair.Key.Equals(protectedKey))
                    .OrderBy(pair => Volatile.Read(ref pair.Value.LastAccess))
                    .ToArray();
                if (candidates.Length == 0)
                    return;

                var removed = false;
                foreach (var candidate in candidates)
                {
                    if (!candidate.Value.Gate.Wait(0, cancellationToken))
                        continue;

                    _testHooks?.AfterRetirementCandidateWait?.Invoke();
                    try
                    {
                        if (!_slots.TryRemove(candidate))
                            continue;

                        var entry = candidate.Value.Current;
                        entry?.Retire();
                        _telemetry.Record(new WorkspaceCacheEvent(
                            WorkspaceCacheEventKind.Evict,
                            candidate.Key.IdentityHash,
                            entry?.SourceRevision,
                            Volatile.Read(ref _loadCount)));
                        removed = true;
                        break;
                    }
                    finally
                    {
                        candidate.Value.Gate.Release();
                    }
                }

                if (!removed)
                    return;
            }
        }
        finally
        {
            _retirementGate.Release();
        }
    }

    internal sealed class ReadOnlyWorkspaceCacheTestHooks
    {
        internal Action? BeforeSlotWait { get; init; }

        internal Action? BeforeRetirementWait { get; init; }

        internal Action? AfterRetirementCandidateWait { get; init; }
    }

    internal sealed record WorkspaceCacheKey(
        string SolutionPath,
        string RepositoryId,
        string WorktreeRoot,
        string EnvironmentIdentity)
    {
        internal string IdentityHash { get; } = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{RepositoryId}\0{WorktreeRoot}\0{SolutionPath}\0{EnvironmentIdentity}")))[..16];

        internal bool Matches(TrustedWorkspaceIdentity identity, string environmentIdentity) =>
            string.Equals(SolutionPath, NormalizePath(identity.SolutionPath), StringComparison.Ordinal)
            && string.Equals(RepositoryId, identity.RepositoryId, StringComparison.Ordinal)
            && string.Equals(WorktreeRoot, NormalizePath(identity.WorktreeRoot), StringComparison.Ordinal)
            && string.Equals(EnvironmentIdentity, environmentIdentity, StringComparison.Ordinal);

        internal static string NormalizePath(string path) => OperatingSystem.IsWindows()
            ? path.ToUpperInvariant()
            : path;
    }

    private sealed class CacheSlot
    {
        internal SemaphoreSlim Gate { get; } = new(1, 1);
        internal CacheEntry? Current { get; set; }
        internal long LastAccess;
    }

    private sealed class CacheEntry(
        IDisposable owner,
        Solution solution,
        string sourceRevision,
        string fingerprint,
        bool reusable,
        Action onDisposed)
    {
        private readonly object _sync = new();
        private int _leases;
        private bool _retired;
        private bool _disposed;

        internal Solution Solution { get; } = solution;
        internal string SourceRevision { get; } = sourceRevision;
        internal string Fingerprint { get; } = fingerprint;
        internal bool Reusable { get; } = reusable;

        internal ReadOnlyWorkspaceLease Lease()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                _leases++;
            }

            return new ReadOnlyWorkspaceLease(Solution, SourceRevision, Release);
        }

        internal void Retire()
        {
            var dispose = false;
            lock (_sync)
            {
                _retired = true;
                if (_leases == 0 && !_disposed)
                {
                    _disposed = true;
                    dispose = true;
                }
            }

            if (dispose)
            {
                owner.Dispose();
                onDisposed();
            }
        }

        private void Release()
        {
            var dispose = false;
            lock (_sync)
            {
                _leases--;
                if (_retired && _leases == 0 && !_disposed)
                {
                    _disposed = true;
                    dispose = true;
                }
            }

            if (dispose)
            {
                owner.Dispose();
                onDisposed();
            }
        }
    }
}
