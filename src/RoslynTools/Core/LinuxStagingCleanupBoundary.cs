using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace RoslynTools.Core;

/// <summary>
///     Handle-relative cleanup of an admitted operation's staging artifacts.
///     The caller must first prove the operation's pre-effect activity is drained.
/// </summary>
internal sealed class LinuxStagingCleanupBoundary
{
    private readonly IStagingArtifactDurability _durability;

    internal LinuxStagingCleanupBoundary(
        string trustedStagingRoot,
        IStagingArtifactDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trustedStagingRoot);
        TrustedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedStagingRoot));
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
                throw new IOException("The protected staging root is missing before artifact removal.");
            return new Session(TrustedRoot, null, _durability);
        }
        try
        {
            LinuxStagingHandles.RequirePrivateDirectory(root);
            return new Session(TrustedRoot, root, _durability);
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
        IStagingArtifactDurability durability) : IDisposable
    {
        internal void RemoveArtifact(OperationId operationId, OperationCleanupArtifact artifact)
        {
            ArgumentNullException.ThrowIfNull(operationId);
            ArgumentNullException.ThrowIfNull(artifact);
            if (root is null)
                throw new IOException("The protected staging root is unavailable for artifact removal.");
            var epoch = operationId.Epoch.ToString(CultureInfo.InvariantCulture);
            var nonce = operationId.Nonce.ToString("N");
            var name = $"{artifact.EffectSequence:D8}.stage";
            var expectedPath = Path.Combine(rootPath, epoch, nonce, name);
            if (artifact.EffectSequence <= 0
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
