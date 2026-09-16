using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

internal sealed record OwnedWorkspaceSnapshot(IDisposable Owner, Solution Solution);

internal sealed class ReadOnlyWorkspaceLease : IDisposable
{
    private Action? _release;

    internal ReadOnlyWorkspaceLease(Solution solution, string sourceRevision, Action? release = null)
    {
        Solution = solution;
        SourceRevision = sourceRevision;
        _release = release;
    }

    internal Solution Solution { get; }

    internal string SourceRevision { get; }

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

    internal WorkspaceCacheTelemetry()
        : this(
            () => Environment.GetEnvironmentVariable(TracePathEnvironmentVariable),
            static (path, line) => File.AppendAllText(path, line, new UTF8Encoding(false)),
            static exception => RtLog.Warning(
                "[workspace-cache] Could not write instrumentation trace: {Message}",
                exception.Message))
    {
    }

    internal WorkspaceCacheTelemetry(
        Func<string?> getTracePath,
        Action<string, string> appendLine,
        Action<Exception>? reportFailure = null)
    {
        _getTracePath = getTracePath;
        _appendLine = appendLine;
        _reportFailure = reportFailure ?? (static _ => { });
    }

    void IWorkspaceCacheTelemetry.Record(WorkspaceCacheEvent cacheEvent)
    {
        RtLog.Info(
            "[workspace-cache] {Kind} identity={IdentityHash} revision={SourceRevision} loadCount={LoadCount}",
            cacheEvent.Kind,
            cacheEvent.IdentityHash,
            cacheEvent.SourceRevision,
            cacheEvent.LoadCount);

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
    private static readonly string[] EnvironmentVariables =
    [
        "Configuration",
        "DOTNET_HOST_PATH",
        "DOTNET_ROOT",
        "MSBuildExtensionsPath",
        "MSBuildSDKsPath",
        "NUGET_PACKAGES",
        "Platform",
        "RuntimeIdentifier",
        "TargetFramework",
        "RT_ISOLATED_OBJ"
    ];

    private static readonly HashSet<string> RelevantExtensions = new(
        [
            ".cs",
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
            ".ruleset",
            ".config"
        ],
        StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> IgnoredDirectories = new(
        [
            ".git",
            ".hg",
            ".svn",
            ".tools",
            "artifacts",
            "bin",
            "node_modules",
            "obj",
            "StrykerOutput",
            "TestResults"
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly IWorkspaceEnvironmentIdentity _environmentIdentity;
    private readonly Func<string, string?> _getEnvironmentVariable;

    internal WorkspaceInputFingerprint(IWorkspaceEnvironmentIdentity environmentIdentity)
        : this(environmentIdentity, Environment.GetEnvironmentVariable)
    {
    }

    internal WorkspaceInputFingerprint(
        IWorkspaceEnvironmentIdentity environmentIdentity,
        Func<string, string?> getEnvironmentVariable)
    {
        _environmentIdentity = environmentIdentity;
        _getEnvironmentVariable = getEnvironmentVariable;
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
            Append(hash, _getEnvironmentVariable(variable));
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    async Task<WorkspaceInputFingerprintResult> IWorkspaceInputFingerprint.CaptureAsync(
        TrustedWorkspaceIdentity identity,
        Solution? solution,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "rt.workspace.inputs.v1");
        Append(hash, identity.RepositoryId);
        Append(hash, identity.WorktreeRoot);
        Append(hash, identity.SolutionPath);
        Append(hash, CaptureEnvironmentIdentity());

        var discovery = DiscoverSemanticInputs(identity, solution);
        Append(hash, discovery.Reusable ? "reusable" : "non-reusable");

        foreach (var path in discovery.Paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Append(hash, path);
            if (!File.Exists(path))
            {
                Append(hash, "missing");
                continue;
            }

            await AppendStableFileAsync(hash, path, cancellationToken);
        }

        var snapshotConsistent = solution is null
            || await IsLoadedSolutionConsistentAsync(solution, cancellationToken);
        return new WorkspaceInputFingerprintResult(
            Convert.ToHexString(hash.GetHashAndReset()),
            discovery.Reusable,
            snapshotConsistent);
    }

    private static SemanticInputDiscovery DiscoverSemanticInputs(
        TrustedWorkspaceIdentity identity,
        Solution? solution)
    {
        var paths = new HashSet<string>(PathComparer);
        foreach (var path in EnumerateRelevantRepositoryFiles(identity.WorktreeRoot)
                     .Concat(solution is null ? [] : EnumerateSolutionInputs(solution))
                     .Append(identity.SolutionPath))
        {
            var fullPath = Path.GetFullPath(path);
            paths.Add(fullPath);
        }

        var solutionProjects = ReadDeclaredSolutionProjects(identity.SolutionPath);
        paths.UnionWith(solutionProjects.Paths);
        var reusable = solutionProjects.Reusable
                       && !HasExistingProjectOutsideWorktree(identity, solution);

        foreach (var startDirectory in paths
                     .Select(Path.GetDirectoryName)
                     .Where(directory => directory is not null)
                     .Distinct(PathComparer)
                     .ToArray())
        {
            AddAncestorConfiguration(paths, startDirectory!);
        }

        var buildInputs = new Queue<string>();
        foreach (var path in paths.Where(IsBuildInput))
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
            reusable);
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

    private static DeclaredBuildInputs ReadDeclaredBuildInputs(string buildInput)
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
            var reusable = true;
            foreach (var element in document.Descendants())
            {
                var isImport = element.Name.LocalName.Equals("Import", StringComparison.OrdinalIgnoreCase);
                string? declaredPath = element.Name.LocalName switch
                {
                    "Import" => element.Attribute("Project")?.Value,
                    "Analyzer" or "AdditionalFiles" or "EditorConfigFiles" or "Compile"
                        or "ProjectReference" =>
                        element.Attribute("Include")?.Value,
                    "HintPath" => element.Value,
                    _ => null
                };
                if (string.IsNullOrWhiteSpace(declaredPath))
                    continue;

                foreach (var candidate in declaredPath.Split(
                             ';',
                             StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var expanded = candidate
                        .Replace("$(MSBuildThisFileDirectory)", directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
                    if (expanded.Contains("$(MSBuildProjectDirectory)", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsProjectInput(buildInput))
                        {
                            reusable = false;
                            continue;
                        }

                        expanded = expanded.Replace(
                            "$(MSBuildProjectDirectory)",
                            directory,
                            StringComparison.OrdinalIgnoreCase);
                    }

                    expanded = expanded
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar);
                    if (expanded.Contains("$(", StringComparison.Ordinal)
                        || expanded.IndexOfAny(['*', '?']) >= 0)
                    {
                        reusable = false;
                        continue;
                    }

                    var fullPath = Path.GetFullPath(expanded, directory);
                    paths.Add(fullPath);
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

    private static void AddAncestorConfiguration(HashSet<string> paths, string startDirectory)
    {
        string[] names =
        [
            "Directory.Build.props",
            "Directory.Build.targets",
            "Directory.Packages.props",
            "Directory.Packages.targets",
            "global.json",
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

    private static bool ShouldDiscoverDeclaredInputs(string path) => !path
        .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Any(segment => segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
                        || segment.Equals("bin", StringComparison.OrdinalIgnoreCase));

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

    private sealed record SemanticInputDiscovery(IReadOnlyList<string> Paths, bool Reusable);

    private sealed record DeclaredBuildInputs(IReadOnlySet<string> Paths, bool Reusable);

    private static IEnumerable<string> EnumerateRelevantRepositoryFiles(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var child in Directory.EnumerateDirectories(directory)
                         .OrderBy(path => path, PathComparer))
            {
                if (!IgnoredDirectories.Contains(Path.GetFileName(child)))
                    pending.Push(child);
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
            return new ReadOnlyWorkspaceLease(ambient.Solution, ambientRevision);
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
            finally
            {
                slot.Gate.Release();
            }

            await TrimAsync(key);
            return lease;
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

                    beforeRepositoryFingerprint = finalRepositoryFingerprint;
                }
                else
                {
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
            $"Workspace inputs for '{identity.SolutionPath}' changed repeatedly during load; no stable snapshot was served.");
    }

    private async Task TrimAsync(WorkspaceCacheKey protectedKey)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;

        _testHooks?.BeforeRetirementWait?.Invoke();
        await _retirementGate.WaitAsync();
        try
        {
            while (_slots.Count > _capacity)
            {
                var candidate = _slots
                    .Where(pair => !pair.Key.Equals(protectedKey))
                    .OrderBy(pair => Volatile.Read(ref pair.Value.LastAccess))
                    .FirstOrDefault();
                if (candidate.Value is null)
                    return;

                await candidate.Value.Gate.WaitAsync();
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
                }
                finally
                {
                    candidate.Value.Gate.Release();
                }
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
