using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record RepositoryTrustListing(string RegistryPath, IReadOnlyList<string> Roots);
internal sealed record RepositoryTrustChange(string RegistryPath, string Root, bool Changed);

/// <summary>Shared configuration operations. Hosts must authorize additions before calling this service.</summary>
internal sealed class RepositoryTrustService(RepositoryTrustStore store) : IRtService
{
    internal static Result<string, RtError> ValidateRoot(string root, bool requireExists)
        => Execute(() => RepositoryTrustStore.NormalizeRoot(root, requireExists));

    internal Result<RepositoryTrustListing, RtError> List()
        => Execute(() => new RepositoryTrustListing(store.FilePath, store.Read()));

    internal Result<RepositoryTrustChange, RtError> Add(string root, CancellationToken cancellationToken = default)
        => Execute(() => new RepositoryTrustChange(store.FilePath,
            RepositoryTrustStore.NormalizeRoot(root, requireExists: true), store.Add(root, cancellationToken)));

    internal Result<RepositoryTrustChange, RtError> Remove(string root, CancellationToken cancellationToken = default)
        => Execute(() => new RepositoryTrustChange(store.FilePath,
            RepositoryTrustStore.NormalizeRoot(root, requireExists: false), store.Remove(root, cancellationToken)));

    private static Result<T, RtError> Execute<T>(Func<T> action)
    {
        try
        {
            return Result<T, RtError>.Ok(action());
        }
        catch (ArgumentException exception)
        {
            return Result<T, RtError>.Err(new RtError(RtErrorKind.InvalidInput, exception.Message));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Result<T, RtError>.Err(new RtError(RtErrorKind.TrustRequired,
                $"Unable to read or update saved repository trust: {exception.Message}"));
        }
    }
}
