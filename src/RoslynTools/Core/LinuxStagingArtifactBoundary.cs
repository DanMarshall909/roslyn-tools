using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace RoslynTools.Core;

/// <summary>
///     A handle-relative staging primitive for a separately provisioned Linux root.
///     It does not grant repository effect authority or establish the writer set.
/// </summary>
internal sealed class LinuxStagingArtifactBoundary : IStagingArtifactBoundary
{
    private readonly string _trustedStagingRoot;
    private readonly IStagingArtifactDurability _durability;

    internal LinuxStagingArtifactBoundary(
        string trustedStagingRoot,
        IStagingArtifactDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedStagingRoot);
        _trustedStagingRoot = Path.GetFullPath(trustedStagingRoot);
        _durability = durability ?? LinuxStagingArtifactDurability.Instance;
    }

    IStagingArtifactSession IStagingArtifactBoundary.Acquire(StoredChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Handle-relative staging currently requires Linux x64.");
        var payload = plan.Envelope.Payload;
        if (!plan.Envelope.Verify()
            || !string.Equals(payload.Coordination.StagingRoot, _trustedStagingRoot, StringComparison.Ordinal)
            || !string.Equals(payload.Coordination.StagingRootResolvedPath, _trustedStagingRoot,
                StringComparison.Ordinal))
            throw new InvalidOperationException("The plan does not name the configured staging root.");

        var staging = LinuxStagingHandles.OpenAbsoluteDirectory(_trustedStagingRoot);
        try
        {
            using var repository = LinuxStagingHandles.OpenAbsoluteDirectory(
                payload.Repository.RepositoryRoot);
            var stagingIdentity = LinuxStagingHandles.Identity(staging);
            var repositoryIdentity = LinuxStagingHandles.Identity(repository);
            if (stagingIdentity.MountId != repositoryIdentity.MountId
                || stagingIdentity.DeviceMajor != repositoryIdentity.DeviceMajor
                || stagingIdentity.DeviceMinor != repositoryIdentity.DeviceMinor
                || stagingIdentity.Inode == repositoryIdentity.Inode)
                throw new InvalidOperationException("Staging must be separate from the repository on its mount.");
            LinuxStagingHandles.RequirePrivateDirectory(staging);
            return new Session(_trustedStagingRoot, staging, _durability);
        }
        catch
        {
            staging.Dispose();
            throw;
        }
    }

    private sealed class Session(
        string root,
        SafeFileHandle staging,
        IStagingArtifactDurability durability) : IStagingArtifactSession
    {
        Task<string> IStagingArtifactSession.CreateAndHashAsync(
            OperationStagingJournalEvent creation,
            ReadOnlyMemory<byte> replacementBytes,
            bool allowExistingAdmittedArtifact) =>
            HashAsync(creation, replacementBytes, allowExistingAdmittedArtifact,
                requireExisting: false);

        Task<string> IStagingArtifactSession.VerifyExistingAsync(
            OperationStagingJournalEvent verification,
            ReadOnlyMemory<byte> replacementBytes) =>
            HashAsync(verification, replacementBytes, allowExistingAdmittedArtifact: true,
                requireExisting: true);

        private async Task<string> HashAsync(
            OperationStagingJournalEvent artifactEvent,
            ReadOnlyMemory<byte> replacementBytes,
            bool allowExistingAdmittedArtifact,
            bool requireExisting)
        {
            ArgumentNullException.ThrowIfNull(artifactEvent);
            if (artifactEvent.Kind != (requireExisting
                    ? OperationStagingJournalKind.Verified
                    : OperationStagingJournalKind.CreationAdmitted)
                || artifactEvent.CreationToken is null
                || artifactEvent.CreationToken == Guid.Empty
                || artifactEvent.StagingRoot != root)
                throw new InvalidOperationException("An exact staging creation admission is required.");

            var relative = Path.GetRelativePath(root, artifactEvent.ArtifactPath);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            if (parts.Length != 3
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var epoch) || epoch <= 0
                || !Guid.TryParseExact(parts[1], "N", out var nonce) || nonce == Guid.Empty
                || !int.TryParse(Path.GetFileNameWithoutExtension(parts[2]),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0
                || parts[2] != $"{sequence:D8}.stage"
                || artifactEvent.EffectSequence != sequence
                || Path.Combine(root, parts[0], parts[1], parts[2]) != artifactEvent.ArtifactPath)
                throw new InvalidOperationException("The admitted artifact path is not operation scoped.");

            var sourceHash = Convert.ToHexString(SHA256.HashData(replacementBytes.Span))
                .ToLowerInvariant();
            if (!string.Equals(sourceHash, artifactEvent.ExpectedHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The replacement bytes differ from the durable intent.");

            using var epochDirectory = requireExisting
                ? LinuxStagingHandles.OpenPrivateDirectory(staging, parts[0], durability)
                : LinuxStagingHandles.OpenOrCreatePrivateDirectory(staging, parts[0], durability);
            using var operationDirectory = requireExisting
                ? LinuxStagingHandles.OpenPrivateDirectory(epochDirectory, parts[1], durability)
                : LinuxStagingHandles.OpenOrCreatePrivateDirectory(epochDirectory, parts[1], durability);
            var (artifact, created) = requireExisting
                ? (LinuxStagingHandles.OpenVerifiedFile(
                    operationDirectory, parts[2], replacementBytes.Length), false)
                : LinuxStagingHandles.OpenAdmittedFile(
                    operationDirectory, parts[2], replacementBytes.Length,
                    allowExistingAdmittedArtifact);
            using (artifact)
            using (var stream = new FileStream(artifact,
                       created ? FileAccess.ReadWrite : FileAccess.Read, 4096, isAsync: false))
            {
                if (created)
                    await stream.WriteAsync(replacementBytes, CancellationToken.None);
                durability.FlushFile(stream);
                durability.FlushDirectory(operationDirectory);
                stream.Position = 0;
                var observedHash = Convert.ToHexString(await SHA256.HashDataAsync(
                    stream, CancellationToken.None)).ToLowerInvariant();
                if (!string.Equals(observedHash, artifactEvent.ExpectedHash, StringComparison.Ordinal))
                    throw new IOException("The durable artifact hash differs from the reviewed replacement.");
                return observedHash;
            }
        }

        void IDisposable.Dispose() => staging.Dispose();
    }
}

internal interface IStagingArtifactDurability
{
    void FlushFile(FileStream stream);
    void FlushDirectory(SafeFileHandle handle);
}

internal sealed class LinuxStagingArtifactDurability : IStagingArtifactDurability
{
    internal static LinuxStagingArtifactDurability Instance { get; } = new();

    void IStagingArtifactDurability.FlushFile(FileStream stream) => stream.Flush(flushToDisk: true);
    void IStagingArtifactDurability.FlushDirectory(SafeFileHandle handle) =>
        LinuxStagingHandles.FlushDirectory(handle);
}

[SuppressMessage("Globalization", "CA2101", Justification =
    "Linux libc paths use UTF-8 and each string parameter is explicitly marshaled as LPUTF8Str.")]
internal static class LinuxStagingHandles
{
    private const int ReadOnly = 0;
    private const int ReadWrite = 2;
    private const int Create = 0x40;
    private const int Exclusive = 0x80;
    private const int NonBlocking = 0x800;
    private const int Directory = 0x10000;
    private const int NoFollow = 0x20000;
    private const int CloseOnExec = 0x80000;
    private const int AtEmptyPath = 0x1000;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtRemoveDirectory = 0x200;
    private const uint StatxBasicStats = 0x7ff;
    private const uint StatxMountId = 0x1000;
    private const int AlreadyExists = 17;
    private const int Missing = 2;

    internal static SafeFileHandle OpenAbsoluteDirectory(string path)
        => TryOpenAbsoluteDirectory(path) ?? throw NativeError("openat", Missing);

    /// <summary>
    ///     Opens a regular file beneath a held directory without following symbolic
    ///     links or crossing mount points. Only a missing path returns null.
    /// </summary>
    internal static SafeFileHandle? TryOpenContainedReadOnlyFile(
        string rootPath,
        IReadOnlyList<string> components)
    {
        if (components.Count == 0)
            throw new InvalidOperationException("A relative file path is required.");
        if (components.Any(component => component.Length == 0
            || component is "." or ".." || component.Contains('/') || component.Contains('\0')))
            throw new InvalidOperationException("A normalized relative file path is required.");

        using var root = OpenAbsoluteDirectory(rootPath);
        var rootMount = Identity(root).MountId;
        var directory = root;
        try
        {
            for (var index = 0; index < components.Count - 1; index++)
            {
                var next = TryOpenAt(directory, components[index],
                    ReadOnly | Directory | NoFollow | CloseOnExec);
                if (next is null)
                    return null;
                try
                {
                    var identity = Identity(next);
                    if ((identity.Mode & 0xf000) != 0x4000 || identity.MountId != rootMount)
                        throw new InvalidOperationException(
                            "The working-tree path crosses a mount or non-directory component.");
                }
                catch
                {
                    next.Dispose();
                    throw;
                }

                if (!ReferenceEquals(directory, root))
                    directory.Dispose();
                directory = next;
            }

            var file = TryOpenAt(directory, components[^1],
                ReadOnly | NonBlocking | NoFollow | CloseOnExec);
            if (file is null)
                return null;
            try
            {
                var identity = Identity(file);
                if ((identity.Mode & 0xf000) != 0x8000 || identity.MountId != rootMount)
                    throw new InvalidOperationException(
                        "The working-tree path is not a regular file on the repository mount.");
                return file;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }
        finally
        {
            if (!ReferenceEquals(directory, root))
                directory.Dispose();
        }
    }

    internal static SafeFileHandle? TryOpenAbsoluteDirectory(
        string path,
        IStagingArtifactDurability? durability = null)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("A fully qualified directory is required.");
        var handle = Open("/", ReadOnly | Directory | NoFollow | CloseOnExec);
        try
        {
            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (component is "." or "..")
                    throw new InvalidOperationException("A normalized directory path is required.");
                var next = TryOpenAt(handle, component,
                    ReadOnly | Directory | NoFollow | CloseOnExec);
                if (next is null)
                {
                    durability?.FlushDirectory(handle);
                    handle.Dispose();
                    return null;
                }
                handle.Dispose();
                handle = next;
            }
            return handle;
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle OpenOrCreatePrivateDirectory(
        SafeFileHandle parent,
        string name,
        IStagingArtifactDurability durability)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('/'))
            throw new InvalidOperationException("A single directory component is required.");
        if (MkdirAt(parent, name, Convert.ToUInt32("700", 8)) != 0)
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != AlreadyExists)
                throw NativeError("mkdirat", error);
        }
        return OpenPrivateDirectory(parent, name, durability);
    }

    internal static SafeFileHandle OpenPrivateDirectory(
        SafeFileHandle parent,
        string name,
        IStagingArtifactDurability durability)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('/'))
            throw new InvalidOperationException("A single directory component is required.");
        var directory = OpenAt(parent, name, ReadOnly | Directory | NoFollow | CloseOnExec);
        try
        {
            RequirePrivateDirectory(directory);
            // EEXIST can race a different writer whose mkdirat has not yet been flushed.
            durability.FlushDirectory(parent);
            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle? TryOpenPrivateDirectory(
        SafeFileHandle parent,
        string name,
        IStagingArtifactDurability durability)
    {
        if (name.Length == 0 || name is "." or ".." || name.Contains('/'))
            throw new InvalidOperationException("A single directory component is required.");
        var directory = TryOpenAt(parent, name, ReadOnly | Directory | NoFollow | CloseOnExec);
        if (directory is null)
        {
            durability.FlushDirectory(parent);
            return null;
        }
        try
        {
            RequirePrivateDirectory(directory);
            durability.FlushDirectory(parent);
            return directory;
        }
        catch
        {
            directory.Dispose();
            throw;
        }
    }

    internal static SafeFileHandle? TryOpenCleanupFile(
        SafeFileHandle parent,
        string name,
        IStagingArtifactDurability durability)
    {
        var artifact = TryOpenAt(parent, name,
            ReadOnly | NonBlocking | NoFollow | CloseOnExec);
        if (artifact is null)
        {
            durability.FlushDirectory(parent);
            return null;
        }
        try
        {
            RequirePrivateRegularFile(artifact);
            return artifact;
        }
        catch
        {
            artifact.Dispose();
            throw;
        }
    }

    internal static void RemoveVerifiedFile(
        SafeFileHandle parent,
        string name,
        SafeFileHandle artifact,
        IStagingArtifactDurability durability)
    {
        RequireSameIdentity(RequirePrivateRegularFile(artifact), IdentityAt(parent, name));
        if (UnlinkAt(parent, name, 0) != 0)
            throw NativeError("unlinkat", Marshal.GetLastPInvokeError());
        durability.FlushDirectory(parent);
    }

    internal static void RemoveEmptyPrivateDirectory(
        SafeFileHandle parent,
        string name,
        IStagingArtifactDurability durability)
    {
        using var directory = TryOpenPrivateDirectory(parent, name, durability);
        if (directory is null)
            return;
        RequirePrivateDirectory(directory);
        RequireSameIdentity(Identity(directory), IdentityAt(parent, name));
        if (UnlinkAt(parent, name, AtRemoveDirectory) != 0)
            throw NativeError("unlinkat", Marshal.GetLastPInvokeError());
        durability.FlushDirectory(parent);
    }

    private static void RequireSameIdentity(StatxIdentity opened, StatxIdentity named)
    {
        if (opened.MountId != named.MountId
            || opened.DeviceMajor != named.DeviceMajor
            || opened.DeviceMinor != named.DeviceMinor
            || opened.Inode != named.Inode
            || opened.Size != named.Size
            || opened.UserId != named.UserId
            || opened.Mode != named.Mode
            || opened.LinkCount != named.LinkCount)
            throw new InvalidOperationException("The cleanup entry changed after it was opened.");
    }

    private static StatxIdentity IdentityAt(SafeFileHandle parent, string name)
    {
        if (Statx(parent, name, AtSymlinkNoFollow, StatxBasicStats | StatxMountId,
                out var identity) != 0)
            throw NativeError("statx", Marshal.GetLastPInvokeError());
        if ((identity.Mask & (StatxBasicStats | StatxMountId))
            != (StatxBasicStats | StatxMountId))
            throw new InvalidOperationException("The cleanup entry has no durable mount identity.");
        return identity;
    }

    internal static (SafeFileHandle Handle, bool Created) OpenAdmittedFile(
        SafeFileHandle parent,
        string name,
        int expectedLength,
        bool allowExistingAdmittedArtifact)
    {
        var descriptor = NativeOpenAt(parent, name,
            ReadWrite | Create | Exclusive | NoFollow | CloseOnExec,
            Convert.ToUInt32("600", 8));
        if (descriptor >= 0)
        {
            var created = new SafeFileHandle(descriptor, ownsHandle: true);
            try
            {
                RequirePrivateRegularFile(created);
                return (created, true);
            }
            catch
            {
                created.Dispose();
                throw;
            }
        }
        var error = Marshal.GetLastPInvokeError();
        if (error != AlreadyExists)
            throw NativeError("openat", error);
        if (!allowExistingAdmittedArtifact)
            throw new IOException("A fresh staging admission cannot adopt an existing artifact.");

        return (OpenVerifiedFile(parent, name, expectedLength), false);
    }

    internal static SafeFileHandle OpenVerifiedFile(
        SafeFileHandle parent,
        string name,
        int expectedLength)
    {
        var existing = OpenAt(parent, name, ReadOnly | NonBlocking | NoFollow | CloseOnExec);
        try
        {
            var identity = RequirePrivateRegularFile(existing);
            if (identity.Size != (ulong)expectedLength)
                throw new IOException("The existing admitted artifact has a different byte length.");
            return existing;
        }
        catch
        {
            existing.Dispose();
            throw;
        }
    }

    internal static void RequirePrivateDirectory(SafeFileHandle handle)
    {
        var identity = Identity(handle);
        if (identity.UserId != GetEffectiveUserId() || (identity.Mode & 0x3f) != 0
            || (identity.Mode & 0xf000) != 0x4000)
            throw new InvalidOperationException("The staging directory is not private to the service user.");
    }

    private static StatxIdentity RequirePrivateRegularFile(SafeFileHandle handle)
    {
        var identity = Identity(handle);
        if (identity.UserId != GetEffectiveUserId() || (identity.Mode & 0x3f) != 0
            || (identity.Mode & 0xf000) != 0x8000 || identity.LinkCount != 1)
            throw new InvalidOperationException(
                "The admitted artifact is not a private, singly linked regular file.");
        return identity;
    }

    internal static StatxIdentity Identity(SafeFileHandle handle)
    {
        if (Statx(handle, "", AtEmptyPath, StatxBasicStats | StatxMountId,
                out var identity) != 0)
            throw NativeError("statx", Marshal.GetLastPInvokeError());
        if ((identity.Mask & (StatxBasicStats | StatxMountId))
            != (StatxBasicStats | StatxMountId))
            throw new InvalidOperationException("The filesystem does not expose durable mount identity.");
        return identity;
    }

    internal static void FlushDirectory(SafeFileHandle handle)
    {
        if (Fsync(handle) != 0)
            throw NativeError("fsync", Marshal.GetLastPInvokeError());
    }

    private static SafeFileHandle Open(string path, int flags)
    {
        var descriptor = NativeOpen(path, flags);
        if (descriptor < 0)
            throw NativeError("open", Marshal.GetLastPInvokeError());
        return new SafeFileHandle(descriptor, ownsHandle: true);
    }

    private static SafeFileHandle OpenAt(SafeFileHandle parent, string name, int flags, uint mode = 0)
    {
        var descriptor = NativeOpenAt(parent, name, flags, mode);
        if (descriptor < 0)
            throw NativeError("openat", Marshal.GetLastPInvokeError());
        return new SafeFileHandle(descriptor, ownsHandle: true);
    }

    private static SafeFileHandle? TryOpenAt(SafeFileHandle parent, string name, int flags)
    {
        var descriptor = NativeOpenAt(parent, name, flags, 0);
        if (descriptor >= 0)
            return new SafeFileHandle(descriptor, ownsHandle: true);
        var error = Marshal.GetLastPInvokeError();
        if (error == Missing)
            return null;
        throw NativeError("openat", error);
    }

    private static IOException NativeError(string operation, int error) =>
        new($"Linux {operation} failed: {new Win32Exception(error).Message} (errno {error}).");

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    internal struct StatxIdentity
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(16)] internal uint LinkCount;
        [FieldOffset(20)] internal uint UserId;
        [FieldOffset(28)] internal ushort Mode;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(40)] internal ulong Size;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
        [FieldOffset(144)] internal ulong MountId;
    }

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int NativeOpen([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int NativeOpenAt(SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc", EntryPoint = "mkdirat", SetLastError = true)]
    private static extern int MkdirAt(SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask,
        out StatxIdentity identity);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(SafeFileHandle descriptor);

    [DllImport("libc", EntryPoint = "unlinkat", SetLastError = true)]
    private static extern int UnlinkAt(SafeFileHandle directory,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
