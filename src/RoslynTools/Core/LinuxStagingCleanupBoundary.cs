using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace RoslynTools.Core;

/// <summary>
///     Handle-relative cleanup of an admitted operation's control artifacts.
///     The caller must first prove the operation's pre-effect activity is drained.
/// </summary>
internal abstract class LinuxControlCleanupBoundary
{
    private readonly IStagingArtifactDurability _durability;
    private readonly OperationCleanupArtifactKind _artifactKind;
    private readonly string _extension;

    protected LinuxControlCleanupBoundary(
        string trustedRoot,
        OperationCleanupArtifactKind artifactKind,
        string extension,
        IStagingArtifactDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedRoot);
        TrustedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRoot));
        _artifactKind = artifactKind;
        _extension = extension;
        _durability = durability ?? LinuxStagingArtifactDurability.Instance;
    }

    internal string TrustedRoot { get; }

    internal Session Acquire(bool allowMissingRoot)
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Handle-relative cleanup currently requires Linux x64.");
        var root = LinuxStagingHandles.TryOpenAbsoluteDirectory(TrustedRoot, _durability);
        if (root is null)
        {
            if (!allowMissingRoot)
                throw new IOException("The protected control root is missing before artifact removal.");
            return new Session(TrustedRoot, null, _durability, _artifactKind, _extension);
        }
        try
        {
            LinuxStagingHandles.RequirePrivateDirectory(root);
            return new Session(TrustedRoot, root, _durability, _artifactKind, _extension);
        }
        catch
        {
            root.Dispose();
            throw;
        }
    }

    internal sealed class Session(
        string rootPath,
        SafeFileHandle? root,
        IStagingArtifactDurability durability,
        OperationCleanupArtifactKind artifactKind,
        string extension) : IDisposable
    {
        internal void RemoveArtifact(OperationId operationId, OperationCleanupArtifact artifact)
        {
            ArgumentNullException.ThrowIfNull(operationId);
            ArgumentNullException.ThrowIfNull(artifact);
            if (root is null)
                throw new IOException("The protected control root is unavailable for artifact removal.");
            var epoch = operationId.Epoch.ToString(CultureInfo.InvariantCulture);
            var nonce = operationId.Nonce.ToString("N");
            var name = $"{artifact.EffectSequence:D8}{extension}";
            var expectedPath = Path.Combine(rootPath, epoch, nonce, name);
            if (artifact.EffectSequence <= 0
                || artifact.Kind != artifactKind
                || artifact.ArtifactPath != expectedPath)
                throw new InvalidOperationException("The cleanup artifact is outside the admitted operation root.");

            using var epochDirectory = LinuxStagingHandles.TryOpenPrivateDirectory(root, epoch, durability);
            if (epochDirectory is null)
                return;
            using var operationDirectory = LinuxStagingHandles.TryOpenPrivateDirectory(
                epochDirectory, nonce, durability);
            if (operationDirectory is null)
                return;
            using var file = LinuxStagingHandles.TryOpenCleanupFile(
                operationDirectory, name, durability);
            if (file is null)
                return;
            using var stream = new FileStream(file, FileAccess.Read);
            var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            if (!string.Equals(hash, artifact.ExpectedHash, StringComparison.Ordinal))
                throw new IOException("The admitted cleanup artifact differs from its durable hash.");
            LinuxStagingHandles.RemoveVerifiedFile(
                operationDirectory, name, stream.SafeFileHandle, durability);
        }

        internal void RemoveEmptyOperationRoot(OperationId operationId)
        {
            ArgumentNullException.ThrowIfNull(operationId);
            if (root is null)
                return;
            var epoch = operationId.Epoch.ToString(CultureInfo.InvariantCulture);
            var nonce = operationId.Nonce.ToString("N");
            using var epochDirectory = LinuxStagingHandles.TryOpenPrivateDirectory(root, epoch, durability);
            if (epochDirectory is null)
                return;
            LinuxStagingHandles.RemoveEmptyPrivateDirectory(epochDirectory, nonce, durability);
        }

        void IDisposable.Dispose() => root?.Dispose();
    }
}

internal sealed class LinuxStagingCleanupBoundary : LinuxControlCleanupBoundary
{
    internal LinuxStagingCleanupBoundary(
        string trustedStagingRoot,
        IStagingArtifactDurability? durability = null)
        : base(trustedStagingRoot, OperationCleanupArtifactKind.Staging, ".stage", durability)
    {
    }
}

internal sealed class LinuxBackupCleanupBoundary : LinuxControlCleanupBoundary
{
    internal LinuxBackupCleanupBoundary(
        string trustedBackupRoot,
        IStagingArtifactDurability? durability = null)
        : base(trustedBackupRoot, OperationCleanupArtifactKind.Backup, ".backup", durability)
    {
    }
}
