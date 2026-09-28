using Microsoft.Build.Locator;
using RoslynTools.Core;

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

internal sealed record MsBuildInstanceSelection(
    MsBuildInstanceIdentity Identity,
    Action Register);

internal static class MsBuildInstanceCompatibility
{
    internal static Result<bool, RtError> Validate(
        MsBuildInstanceIdentity registered,
        MsBuildInstanceIdentity requested,
        string directory,
        string hostName,
        string restartContextKey)
    {
        if (registered.Matches(requested))
            return Result<bool, RtError>.Ok(true);

        return Result<bool, RtError>.Err(RtError.ExternalToolFailure(
            $"The requested solution selects MSBuild {requested.Version} at '{requested.MSBuildPath}', "
            + $"but this {hostName} registered MSBuild {registered.Version} at '{registered.MSBuildPath}'. "
            + $"Restart the {hostName} from a compatible SDK context.",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [restartContextKey] = "true",
                ["registeredSdkVersion"] = registered.Version.ToString(),
                ["registeredMsBuildPath"] = registered.MSBuildPath,
                ["requestedSdkVersion"] = requested.Version.ToString(),
                ["requestedMsBuildPath"] = requested.MSBuildPath,
                ["requestedDirectory"] = directory
            }));
    }

    internal static RtError SelectionError(
        string directory,
        string diagnostic,
        string hostName,
        string restartContextKey) => RtError.ExternalToolFailure(
        $"{diagnostic} Restart the {hostName} after correcting the SDK configuration for '{directory}'.",
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [restartContextKey] = "true",
            ["requestedDirectory"] = directory
        });
}
