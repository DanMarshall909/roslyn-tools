using System.Text.Json;
using Microsoft.Build.Locator;
using RoslynTools.Core;

namespace RoslynTools;

/// <summary>
/// Class for bootstrapping MSBuild processes.
/// </summary>
internal static class MsBuildBootstrap
{
    private static readonly object RegistrationLock = new();

    /// <summary>
    /// Method to register MSBuild bootstrapping steps.
    /// </summary>
    internal static void Register()
    {
        Register(Environment.CurrentDirectory);
    }

    internal static void Register(string startDirectory)
    {
        lock (RegistrationLock)
        {
            if (MSBuildLocator.IsRegistered)
                return;

            RegisterCore(startDirectory);
        }
    }

    internal static VisualStudioInstance RegisterForDaemon(string startDirectory)
    {
        lock (RegistrationLock)
        {
            if (MSBuildLocator.IsRegistered)
            {
                throw new InvalidOperationException(
                    "The daemon cannot capture an authoritative MSBuild identity after registration.");
            }

            var sdkRequest = InspectSdkRequest(startDirectory);
            if (sdkRequest.State == DoctorSdkRequestState.Invalid)
                throw new InvalidOperationException(sdkRequest.Diagnostic);

            var instance = FindPreferredInstance(startDirectory)
                ?? throw CreateMissingSdkException(startDirectory);
            MSBuildLocator.RegisterInstance(instance);
            return instance;
        }
    }

    private static void RegisterCore(string startDirectory)
    {
        var sdkRequest = InspectSdkRequest(startDirectory);
        if (sdkRequest.State == DoctorSdkRequestState.Invalid)
            throw new InvalidOperationException(sdkRequest.Diagnostic);

        VisualStudioInstance? sdkInstance = null;
        try
        {
            sdkInstance = FindPreferredInstance(startDirectory);
        }
        catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException)
        {
        }

        var registrationPath = SelectRegistrationPath(
            sdkRequest.State,
            sdkInstance is not null);
        if (registrationPath == MsBuildRegistrationPath.ScopedInstance)
        {
            MSBuildLocator.RegisterInstance(sdkInstance!);
            return;
        }

        if (registrationPath == MsBuildRegistrationPath.RequestedSdkUnavailable)
            throw CreateMissingSdkException(startDirectory);

        try
        {
            MSBuildLocator.RegisterDefaults();
            return;
        }
        catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException)
        {
            RtLog.Warning(
                "[MsBuildBootstrap] MSBuildLocator.RegisterDefaults failed ({Message}); falling back to manual SDK discovery.",
                ex.Message);
        }

        var sdkPath = FindLatestDotnetSdkPath();
        if (sdkPath == null)
            throw CreateMissingSdkException(startDirectory);

        MSBuildLocator.RegisterMSBuildPath(sdkPath);
    }

    private static InvalidOperationException CreateMissingSdkException(string startDirectory)
    {
        var globalJsonPath = FindGlobalJson(startDirectory);
        var requestedVersion = globalJsonPath == null ? null : ReadRequestedSdkVersion(globalJsonPath);
        var requested = requestedVersion == null
            ? string.Empty
            : $" The solution requests SDK {requestedVersion} in '{globalJsonPath}'.";

        return new InvalidOperationException(
            "Roslyn Tools can start without a .NET SDK, but semantic solution loading requires one."
            + requested
            + " Install the requested .NET SDK and ensure dotnet is on PATH or DOTNET_ROOT points to it.");
    }

    private static string? FindGlobalJson(string startDirectory)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory != null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "global.json");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ReadRequestedSdkVersion(string globalJsonPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
            return document.RootElement.GetProperty("sdk").GetProperty("version").GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException)
        {
            return null;
        }
    }

    internal static string? FindRequestedSdkVersion(string startDirectory)
    {
        var request = InspectSdkRequest(startDirectory);
        return request.State == DoctorSdkRequestState.Valid ? request.RequestedVersion : null;
    }

    internal static DoctorSdkRequest InspectSdkRequest(string startDirectory)
    {
        string? globalJsonPath;
        try
        {
            globalJsonPath = FindGlobalJson(startDirectory);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or IOException
                                          or UnauthorizedAccessException
                                          or NotSupportedException)
        {
            return new DoctorSdkRequest(
                DoctorSdkRequestState.Invalid,
                null,
                null,
                $"Could not search for global.json from '{startDirectory}': {exception.Message}");
        }

        if (globalJsonPath is null)
            return new DoctorSdkRequest(DoctorSdkRequestState.Absent, null, null, null);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(globalJsonPath));
            if (!document.RootElement.TryGetProperty("sdk", out var sdk))
            {
                return new DoctorSdkRequest(
                    DoctorSdkRequestState.Absent,
                    globalJsonPath,
                    null,
                    null);
            }

            if (sdk.ValueKind != JsonValueKind.Object)
                throw new JsonException("sdk must be an object.");

            string? version = null;
            if (sdk.TryGetProperty("version", out var versionElement))
            {
                if (versionElement.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(versionElement.GetString()))
                {
                    throw new JsonException("sdk.version must be a non-empty string when specified.");
                }

                version = versionElement.GetString();
            }

            return new DoctorSdkRequest(
                DoctorSdkRequestState.Valid,
                globalJsonPath,
                version,
                null);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or JsonException
                                          or KeyNotFoundException
                                          or InvalidOperationException)
        {
            return new DoctorSdkRequest(
                DoctorSdkRequestState.Invalid,
                globalJsonPath,
                null,
                $"global.json at '{globalJsonPath}' could not be read as an SDK request: {exception.Message}");
        }
    }

    internal static MsBuildRegistrationPath SelectRegistrationPath(
        DoctorSdkRequestState requestState,
        bool scopedInstanceFound) =>
        scopedInstanceFound
            ? MsBuildRegistrationPath.ScopedInstance
            : requestState == DoctorSdkRequestState.Absent
                ? MsBuildRegistrationPath.UnscopedFallback
                : MsBuildRegistrationPath.RequestedSdkUnavailable;

    // Prefer a .NET SDK instance over VS's .NET Framework MSBuild — the Framework host
    // causes TypeInitializationException when loading .NET 8+ projects.
    /// <summary>
    /// Method to find the preferred MSBuild instance.
    /// </summary>
    internal static VisualStudioInstance? FindPreferredInstance(string? startDirectory = null)
    {
        var options = new VisualStudioInstanceQueryOptions
        {
            WorkingDirectory = startDirectory ?? Environment.CurrentDirectory,
            DiscoveryTypes = DiscoveryType.DotNetSdk
        };
        return MSBuildLocator.QueryVisualStudioInstances(options)
            .Where(i => i.DiscoveryType == DiscoveryType.DotNetSdk)
            .OrderByDescending(i => i.Version)
            .FirstOrDefault();
    }

    private static string? FindLatestDotnetSdkPath()
    {
        foreach (var root in GetDotnetRoots())
        {
            var sdkRoot = Path.Combine(root, "sdk");
            if (!Directory.Exists(sdkRoot))
                continue;

            var bestPath = Directory
                .EnumerateDirectories(sdkRoot)
                .Select(path => new { Path = path, Version = ParseVersion(Path.GetFileName(path)) })
                .Where(x => File.Exists(Path.Combine(x.Path, "MSBuild.dll")))
                .OrderByDescending(x => x.Version)
                .ThenByDescending(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .Select(x => x.Path)
                .FirstOrDefault();

            if (bestPath != null)
                return bestPath;
        }

        return null;
    }

    private static IEnumerable<string> GetDotnetRoots()
    {
        var roots = new List<string?>
        {
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            Environment.GetEnvironmentVariable("DOTNET_ROOT(x86)")
        };

        if (OperatingSystem.IsWindows())
        {
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
            roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "dotnet"));
        }
        else
        {
            roots.Add("/usr/share/dotnet");
            roots.Add("/usr/local/share/dotnet");
        }

        return roots
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(static path => Path.GetFullPath(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static Version ParseVersion(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
            return new Version(0, 0);

        var stablePart = folderName.Split('-')[0];
        return Version.TryParse(stablePart, out var version)
            ? version
            : new Version(0, 0);
    }
}

internal enum DoctorSdkRequestState
{
    Absent,
    Valid,
    Invalid
}

internal enum MsBuildRegistrationPath
{
    ScopedInstance,
    UnscopedFallback,
    RequestedSdkUnavailable
}

internal sealed record DoctorSdkRequest(
    DoctorSdkRequestState State,
    string? GlobalJsonPath,
    string? RequestedVersion,
    string? Diagnostic);
