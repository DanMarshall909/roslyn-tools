using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoslynTools.Core;

/// <summary>User-owned, exact-root approvals. Reading this store does not itself grant invocation authority.</summary>
internal sealed class RepositoryTrustStore
{
    private const int MaximumBytes = 1_048_576;
    private static readonly StringComparer RootComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly Lazy<string> _filePath;

    internal RepositoryTrustStore(string? filePath = null)
    {
        if (filePath is null)
        {
            // Constructing CLI services must not create a profile dependency for CI or ordinary invocations.
            _filePath = new Lazy<string>(GetDefaultFilePath);
            return;
        }
        if (!Path.IsPathFullyQualified(filePath))
            throw new ArgumentException("The trust registry location must be an absolute path.", nameof(filePath));
        var canonicalPath = Path.GetFullPath(filePath);
        _filePath = new Lazy<string>(() => canonicalPath);
    }

    internal string FilePath => _filePath.Value;

    internal IReadOnlyList<string> Read()
    {
        RejectLinkedPath(FilePath);
        try
        {
            using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaximumBytes)
                throw new InvalidDataException("The trust registry exceeds its size limit.");
            var registry = JsonSerializer.Deserialize<Registry>(stream, JsonOptions)
                ?? throw new InvalidDataException("The trust registry must contain a JSON object.");
            if (registry.Version != 1 || registry.Roots is null || registry.Roots.Length > 4096)
                throw new InvalidDataException("The trust registry has an unsupported version or invalid roots.");
            var roots = registry.Roots.Select(root => NormalizeRoot(root, requireExists: false))
                .Distinct(RootComparer).Order(RootComparer).ToArray();
            return Array.AsReadOnly(roots);
        }
        catch (FileNotFoundException)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }
        catch (DirectoryNotFoundException)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The trust registry is malformed; no saved approvals were loaded.", exception);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException("The trust registry contains an invalid root; no saved approvals were loaded.", exception);
        }
    }

    internal bool Add(string root, CancellationToken cancellationToken = default)
        => Update(NormalizeRoot(root, requireExists: true), add: true, cancellationToken);

    internal bool Remove(string root, CancellationToken cancellationToken = default)
        => Update(NormalizeRoot(root, requireExists: false), add: false, cancellationToken);

    internal static string NormalizeRoot(string root, bool requireExists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("The trusted root must be an absolute directory path.", nameof(root));
        var canonical = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (requireExists)
        {
            if (!Directory.Exists(canonical))
                throw new ArgumentException($"The trusted root directory does not exist: '{canonical}'.", nameof(root));
            RejectLinkedPath(canonical);
        }
        return canonical;
    }

    /// <summary>Rejects symbolic links and Windows reparse points in every existing path component.</summary>
    internal static void RejectLinkedPath(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Repository trust cannot use a symbolic link or reparse point: '{current}'.");
        }
    }

    private static string GetDefaultFilePath()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localData) || !Path.IsPathFullyQualified(localData))
            throw new IOException("A user-owned local application data directory is required for saved repository trust.");
        return Path.Combine(localData, "RoslynTools", "trusted-roots.json");
    }

    private bool Update(string root, bool add, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(FilePath)!;
        RejectLinkedPath(FilePath);
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        using var updateLock = AcquireLock(cancellationToken);
        var roots = Read().ToHashSet(RootComparer);
        var changed = add ? roots.Add(root) : roots.Remove(root);
        if (!changed)
            return false;
        if (roots.Count > 4096)
            throw new InvalidDataException("The trust registry cannot contain more than 4096 roots.");

        var contents = JsonSerializer.SerializeToUtf8Bytes(new Registry(1, roots.Order(RootComparer).ToArray()), JsonOptions);
        if (contents.Length > MaximumBytes)
            throw new InvalidDataException("The trust registry exceeds its size limit.");
        var temporaryPath = Path.Combine(directory, $".trusted-roots-{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = OpenPrivateFile(temporaryPath, FileMode.CreateNew, FileShare.None))
            {
                stream.Write(contents);
                stream.Flush(flushToDisk: true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinkedPath(FilePath);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
        return true;
    }

    private FileStream AcquireLock(CancellationToken cancellationToken)
    {
        var lockPath = FilePath + ".lock";
        var elapsed = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RejectLinkedPath(lockPath);
            try
            {
                // Keep the lock file after releasing the handle: deleting it permits split-inode locks on Unix.
                return OpenPrivateFile(lockPath, FileMode.OpenOrCreate, FileShare.None);
            }
            catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(10))
            {
                if (cancellationToken.WaitHandle.WaitOne(25))
                    cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }

    private static FileStream OpenPrivateFile(string path, FileMode mode, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = FileAccess.ReadWrite, Share = share };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private sealed record Registry([property: JsonRequired] int Version, [property: JsonRequired] string[] Roots);
}
