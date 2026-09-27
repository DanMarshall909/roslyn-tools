using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace RoslynTools.Core;

/// <summary>
///     A handle-relative backup primitive for a separately provisioned Linux root.
///     It does not grant repository effect authority or establish the writer set.
/// </summary>
internal sealed class LinuxBackupArtifactBoundary : IBackupArtifactBoundary
{
    private readonly string _trustedBackupRoot;
    private readonly IStagingArtifactDurability _durability;

    internal LinuxBackupArtifactBoundary(
        string trustedBackupRoot,
        IStagingArtifactDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedBackupRoot);
        _trustedBackupRoot = Path.GetFullPath(trustedBackupRoot);
        _durability = durability ?? LinuxStagingArtifactDurability.Instance;
    }

    IBackupArtifactSession IBackupArtifactBoundary.Acquire(StoredChangePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Handle-relative backup currently requires Linux x64.");
        var payload = plan.Envelope.Payload;
        if (!plan.Envelope.Verify()
            || !string.Equals(payload.Coordination.BackupRoot, _trustedBackupRoot, StringComparison.Ordinal)
            || !string.Equals(payload.Coordination.BackupRootResolvedPath, _trustedBackupRoot,
                StringComparison.Ordinal))
            throw new InvalidOperationException("The plan does not name the configured backup root.");

        var backup = LinuxStagingHandles.OpenAbsoluteDirectory(_trustedBackupRoot);
        try
        {
            using var repository = LinuxStagingHandles.OpenAbsoluteDirectory(
                payload.Repository.RepositoryRoot);
            var backupIdentity = LinuxStagingHandles.Identity(backup);
            var repositoryIdentity = LinuxStagingHandles.Identity(repository);
            if (backupIdentity.MountId != repositoryIdentity.MountId
                || backupIdentity.DeviceMajor != repositoryIdentity.DeviceMajor
                || backupIdentity.DeviceMinor != repositoryIdentity.DeviceMinor
                || backupIdentity.Inode == repositoryIdentity.Inode)
                throw new InvalidOperationException("Backup must be separate from the repository on its mount.");
            LinuxStagingHandles.RequirePrivateDirectory(backup);
            return new Session(_trustedBackupRoot, backup, _durability);
        }
        catch
        {
            backup.Dispose();
            throw;
        }
    }

    private sealed class Session(
        string root,
        SafeFileHandle backup,
        IStagingArtifactDurability durability) : IBackupArtifactSession
    {
        Task<string> IBackupArtifactSession.CreateAndHashAsync(
            OperationBackupJournalEvent creation,
            ReadOnlyMemory<byte> originalBytes,
            bool allowExistingAdmittedArtifact) =>
            HashAsync(creation, originalBytes, allowExistingAdmittedArtifact,
                requireExisting: false);

        Task<string> IBackupArtifactSession.VerifyExistingAsync(
            OperationBackupJournalEvent verification,
            ReadOnlyMemory<byte> originalBytes) =>
            HashAsync(verification, originalBytes, allowExistingAdmittedArtifact: true,
                requireExisting: true);

        private async Task<string> HashAsync(
            OperationBackupJournalEvent artifactEvent,
            ReadOnlyMemory<byte> originalBytes,
            bool allowExistingAdmittedArtifact,
            bool requireExisting)
        {
            ArgumentNullException.ThrowIfNull(artifactEvent);
            if (artifactEvent.Kind != (requireExisting
                    ? OperationBackupJournalKind.Verified
                    : OperationBackupJournalKind.CreationAdmitted)
                || artifactEvent.CreationToken is null
                || artifactEvent.CreationToken == Guid.Empty
                || artifactEvent.BackupRoot != root)
                throw new InvalidOperationException("An exact backup creation admission is required.");

            var relative = Path.GetRelativePath(root, artifactEvent.ArtifactPath);
            var parts = relative.Split(Path.DirectorySeparatorChar);
            if (parts.Length != 3
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture,
                    out var epoch) || epoch <= 0
                || !Guid.TryParseExact(parts[1], "N", out var nonce) || nonce == Guid.Empty
                || !int.TryParse(Path.GetFileNameWithoutExtension(parts[2]),
                    NumberStyles.None, CultureInfo.InvariantCulture, out var sequence) || sequence <= 0
                || parts[2] != $"{sequence:D8}.backup"
                || artifactEvent.EffectSequence != sequence
                || Path.Combine(root, parts[0], parts[1], parts[2]) != artifactEvent.ArtifactPath)
                throw new InvalidOperationException("The admitted artifact path is not operation scoped.");

            var sourceHash = Convert.ToHexString(SHA256.HashData(originalBytes.Span))
                .ToLowerInvariant();
            if (!string.Equals(sourceHash, artifactEvent.ExpectedHash, StringComparison.Ordinal))
                throw new InvalidOperationException("The original bytes differ from the durable intent.");

            using var epochDirectory = requireExisting
                ? LinuxStagingHandles.OpenPrivateDirectory(backup, parts[0], durability)
                : LinuxStagingHandles.OpenOrCreatePrivateDirectory(backup, parts[0], durability);
            using var operationDirectory = requireExisting
                ? LinuxStagingHandles.OpenPrivateDirectory(epochDirectory, parts[1], durability)
                : LinuxStagingHandles.OpenOrCreatePrivateDirectory(epochDirectory, parts[1], durability);
            var (artifact, created) = requireExisting
                ? (LinuxStagingHandles.OpenVerifiedFile(
                    operationDirectory, parts[2], originalBytes.Length), false)
                : LinuxStagingHandles.OpenAdmittedFile(
                    operationDirectory, parts[2], originalBytes.Length,
                    allowExistingAdmittedArtifact);
            using (artifact)
            using (var stream = new FileStream(artifact,
                       created ? FileAccess.ReadWrite : FileAccess.Read, 4096, isAsync: false))
            {
                if (created)
                    await stream.WriteAsync(originalBytes, CancellationToken.None);
                durability.FlushFile(stream);
                durability.FlushDirectory(operationDirectory);
                stream.Position = 0;
                var observedHash = Convert.ToHexString(await SHA256.HashDataAsync(
                    stream, CancellationToken.None)).ToLowerInvariant();
                if (!string.Equals(observedHash, artifactEvent.ExpectedHash, StringComparison.Ordinal))
                    throw new IOException("The durable artifact hash differs from the reviewed original.");
                return observedHash;
            }
        }

        void IDisposable.Dispose() => backup.Dispose();
    }
}
