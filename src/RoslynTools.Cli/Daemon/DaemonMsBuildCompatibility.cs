using Microsoft.Build.Locator;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Daemon;

internal sealed record MsBuildInstanceIdentity(string MSBuildPath, Version Version)
{
    internal static MsBuildInstanceIdentity From(VisualStudioInstance instance) => new(
        NormalizePath(instance.MSBuildPath),
        instance.Version);

    internal bool Matches(MsBuildInstanceIdentity other) =>
        Version == other.Version
        && string.Equals(MSBuildPath, other.MSBuildPath, PathComparison);

    internal static MsBuildInstanceIdentity Create(string msBuildPath, Version version) =>
        new(NormalizePath(msBuildPath), version);

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    private static string NormalizePath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}

internal sealed class DaemonMsBuildCompatibility(
    MsBuildInstanceIdentity registeredIdentity,
    Func<string, MsBuildInstanceIdentity?>? resolveIdentity = null)
    : IDaemonSdkContextGuard
{
    private readonly MsBuildInstanceIdentity _registeredIdentity = registeredIdentity;
    private readonly Func<string, MsBuildInstanceIdentity?> _resolveIdentity =
        resolveIdentity ?? ResolveIdentity;

    public Result<bool, RtError> Check(string directory)
    {
        MsBuildInstanceIdentity? requestedIdentity;
        try
        {
            requestedIdentity = _resolveIdentity(directory);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or DllNotFoundException
                                          or InvalidOperationException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Result<bool, RtError>.Err(CreateSelectionError(
                directory,
                $"Could not select the .NET SDK: {exception.Message}"));
        }

        if (requestedIdentity is null)
        {
            return Result<bool, RtError>.Err(CreateSelectionError(
                directory,
                "No compatible .NET SDK could be selected."));
        }

        if (_registeredIdentity.Matches(requestedIdentity))
            return Result<bool, RtError>.Ok(true);

        return Result<bool, RtError>.Err(CreateRestartRequiredError(directory, requestedIdentity));
    }

    private static MsBuildInstanceIdentity? ResolveIdentity(string directory)
    {
        var instance = MsBuildBootstrap.FindPreferredInstance(directory);
        return CreateIdentity(instance);
    }

    internal static MsBuildInstanceIdentity? CreateIdentity(VisualStudioInstance? instance) =>
        instance is null ? null : MsBuildInstanceIdentity.From(instance);

    private static RtError CreateSelectionError(string directory, string diagnostic) =>
        RtError.ExternalToolFailure(
            $"{diagnostic} Restart the daemon after correcting the SDK configuration for '{directory}'.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["daemonRestartRequired"] = "true",
                ["requestedDirectory"] = directory
            });

    private RtError CreateRestartRequiredError(
        string directory,
        MsBuildInstanceIdentity requestedIdentity) => RtError.ExternalToolFailure(
        $"The requested solution selects MSBuild {requestedIdentity.Version} at '{requestedIdentity.MSBuildPath}', "
        + $"but this daemon registered MSBuild {_registeredIdentity.Version} at '{_registeredIdentity.MSBuildPath}'. "
        + "Restart the daemon from a compatible SDK context.",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["daemonRestartRequired"] = "true",
            ["registeredSdkVersion"] = _registeredIdentity.Version.ToString(),
            ["registeredMsBuildPath"] = _registeredIdentity.MSBuildPath,
            ["requestedSdkVersion"] = requestedIdentity.Version.ToString(),
            ["requestedMsBuildPath"] = requestedIdentity.MSBuildPath,
            ["requestedDirectory"] = directory
        });
}
