using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RoslynTools.Core;

internal interface IRepositoryMutationReservationProtectionAuthority
{
    bool TryAcquire(
        string controlRoot,
        ChangePlanRepository repository,
        out IDisposable? protectionLease);
}

internal sealed class RepositoryMutationReservation : IDisposable
{
    private readonly FileStream _stream;
    private readonly IDisposable _protectionLease;
    private bool _disposed;

    internal RepositoryMutationReservation(
        FileStream stream,
        IDisposable protectionLease,
        string repositoryId,
        long fenceEpoch)
    {
        _stream = stream;
        _protectionLease = protectionLease;
        RepositoryId = repositoryId;
        FenceEpoch = fenceEpoch;
        FenceToken = Guid.NewGuid();
    }

    internal string RepositoryId { get; }

    internal Guid FenceToken { get; }

    internal long FenceEpoch { get; }

    internal void AssertHeld(string repositoryId)
    {
        ObjectDisposedException.ThrowIf(
            _disposed || _stream.SafeFileHandle.IsClosed,
            nameof(RepositoryMutationReservation));
        if (!string.Equals(RepositoryId, repositoryId, StringComparison.Ordinal))
            throw new InvalidOperationException("The reservation belongs to another repository.");
    }

    /// <summary>
    ///     Releases the exclusive repository lock and its protected-root lease.
    ///     Repeated disposal has no further effect.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stream.Dispose();
        _protectionLease.Dispose();
    }
}

internal sealed class RepositoryMutationReservationProvider
{
    private readonly string _controlRoot;
    private readonly IRepositoryMutationReservationProtectionAuthority _protectionAuthority;
    private readonly IOperationLedgerDurability _durability;

    internal RepositoryMutationReservationProvider(
        string controlRoot,
        IRepositoryMutationReservationProtectionAuthority protectionAuthority,
        IOperationLedgerDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlRoot);
        ArgumentNullException.ThrowIfNull(protectionAuthority);
        _controlRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(controlRoot));
        _protectionAuthority = protectionAuthority;
        _durability = durability ?? DurableOperationLedger.PlatformOperationLedgerDurability.Instance;
    }

    internal async Task<RepositoryMutationReservation> AcquireAsync(
        ChangePlanRepository repository,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_protectionAuthority.TryAcquire(_controlRoot, repository, out var protectionLease)
            || protectionLease is null)
        {
            protectionLease?.Dispose();
            throw new InvalidOperationException(
                "The repository mutation reservation has no protected control-root lease.");
        }

        try
        {
            var digest = Convert.ToHexString(SHA256.HashData(
                Encoding.UTF8.GetBytes(repository.RepositoryId))).ToLowerInvariant();
            var reservationPath = Path.Combine(_controlRoot, digest + ".reservation");
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FileStream stream;
                try
                {
                    stream = new FileStream(
                        reservationPath,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.None,
                        bufferSize: 1,
                        FileOptions.WriteThrough);
                }
                catch (IOException) when (elapsed.Elapsed < TimeSpan.FromSeconds(5))
                {
                    await Task.Delay(25, cancellationToken);
                    continue;
                }
                try
                {
                    var fenceEpoch = IssueFenceEpoch(digest);
                    return new RepositoryMutationReservation(
                        stream,
                        protectionLease,
                        repository.RepositoryId,
                        fenceEpoch);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            }
        }
        catch
        {
            protectionLease.Dispose();
            throw;
        }
    }

    private long IssueFenceEpoch(string repositoryDigest)
    {
        var epochPath = Path.Combine(_controlRoot, repositoryDigest + ".epoch");
        long lastEpoch = 0;
        if (File.Exists(epochPath))
        {
            if ((File.GetAttributes(epochPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("The repository fence-epoch record is a link.");
            var bytes = File.ReadAllBytes(epochPath);
            if (bytes.Length is < 1 or > 20
                || !long.TryParse(
                    Encoding.ASCII.GetString(bytes),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out lastEpoch)
                || lastEpoch <= 0)
            {
                throw new InvalidDataException("The repository fence-epoch record is invalid.");
            }
        }
        var nextEpoch = checked(lastEpoch + 1);
        var temporaryPath = epochPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 32,
                       FileOptions.WriteThrough))
            {
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(
                        temporaryPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                var bytes = Encoding.ASCII.GetBytes(nextEpoch.ToString(CultureInfo.InvariantCulture));
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            _durability.ReplaceFile(temporaryPath, epochPath);
            _durability.FlushDirectoryMetadata(_controlRoot);
            return nextEpoch;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
