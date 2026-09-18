using System.Diagnostics;
using System.Text.Json;
using Microsoft.Build.Locator;
using RoslynTools.Core;
using RoslynTools.Daemon;

namespace RoslynTools;

/// <summary>
/// Class for bootstrapping MSBuild processes.
/// </summary>
internal static class MsBuildBootstrap
{
    private static readonly object RegistrationLock = new();
    private static VisualStudioInstance? _registeredSdkInstance;
    private static MsBuildInstanceIdentity? _registeredSdkIdentity;

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

    internal static MsBuildInstanceIdentity RegisterForDaemon(string startDirectory)
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

            var selection = SelectExactInstance(startDirectory)
                ?? throw CreateMissingSdkException(startDirectory);
            selection.Register();
            return selection.Identity;
        }
    }

    internal static VisualStudioInstance? GetRegisteredSdkInstance()
    {
        lock (RegistrationLock)
            return _registeredSdkInstance;
    }

    internal static MsBuildInstanceIdentity? GetRegisteredSdkIdentity()
    {
        lock (RegistrationLock)
            return _registeredSdkIdentity;
    }

    internal static MsBuildInstanceSelection? SelectExactInstance(string directory)
    {
        return FindPreferredSelection(directory);
    }

    internal static void RegisterExactInstance(VisualStudioInstance instance)
    {
        lock (RegistrationLock)
        {
            if (MSBuildLocator.IsRegistered)
            {
                if (_registeredSdkInstance is not null
                    && _registeredSdkInstance.Version == instance.Version
                    && string.Equals(
                        Path.GetFullPath(_registeredSdkInstance.MSBuildPath),
                        Path.GetFullPath(instance.MSBuildPath),
                        OperatingSystem.IsWindows()
                            ? StringComparison.OrdinalIgnoreCase
                            : StringComparison.Ordinal))
                {
                    return;
                }

                throw new InvalidOperationException(
                    "MSBuild was already registered before its exact SDK identity could be verified.");
            }

            RegisterInstance(instance);
        }
    }

    private static void RegisterCore(string startDirectory)
    {
        var sdkRequest = InspectSdkRequest(startDirectory);
        if (sdkRequest.State == DoctorSdkRequestState.Invalid)
            throw new InvalidOperationException(sdkRequest.Diagnostic);

        MsBuildInstanceSelection? sdkSelection = null;
        try
        {
            sdkSelection = FindPreferredSelection(startDirectory);
        }
        catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException)
        {
        }

        var registrationPath = SelectRegistrationPath(
            sdkRequest.State,
            sdkSelection is not null);
        if (registrationPath == MsBuildRegistrationPath.ScopedInstance)
        {
            sdkSelection!.Register();
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

    private static void RegisterInstance(VisualStudioInstance instance)
    {
        MSBuildLocator.RegisterInstance(instance);
        _registeredSdkInstance = instance;
        _registeredSdkIdentity = MsBuildInstanceIdentity.From(instance);
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
        var workingDirectory = startDirectory ?? Environment.CurrentDirectory;
        return QueryPreferredInstance(workingDirectory);
    }

    internal static MsBuildInstanceSelection? SelectPreferredSelection(
        Func<MsBuildInstanceSelection?> queryNativeLocator,
        Func<MsBuildInstanceSelection?> findManualFallback)
    {
        try
        {
            return queryNativeLocator() ?? findManualFallback();
        }
        catch (Exception ex) when (ex is DllNotFoundException or InvalidOperationException)
        {
            return findManualFallback();
        }
    }

    private static MsBuildInstanceSelection? FindPreferredSelection(string workingDirectory) =>
        SelectPreferredSelection(
            () =>
            {
                var instance = QueryPreferredInstance(workingDirectory);
                return instance is null
                    ? null
                    : new MsBuildInstanceSelection(
                        MsBuildInstanceIdentity.From(instance),
                        () => RegisterExactInstance(instance));
            },
            () => FindDotnetSelectedSelection(workingDirectory));

    private static VisualStudioInstance? QueryPreferredInstance(string workingDirectory)
    {
        var options = new VisualStudioInstanceQueryOptions
        {
            WorkingDirectory = workingDirectory,
            DiscoveryTypes = DiscoveryType.DotNetSdk
        };
        return MSBuildLocator.QueryVisualStudioInstances(options)
            .Where(i => i.DiscoveryType == DiscoveryType.DotNetSdk)
            .OrderByDescending(i => i.Version)
            .FirstOrDefault();
    }

    private static MsBuildInstanceSelection? FindDotnetSelectedSelection(string workingDirectory)
    {
        string? selectedVersion;
        try
        {
            var startInfo = new ProcessStartInfo("dotnet", "--version")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(startInfo);
            if (process is null)
                return null;
            if (!process.WaitForExit(10_000))
            {
                process.Kill(entireProcessTree: true);
                return null;
            }

            selectedVersion = process.ExitCode == 0
                ? process.StandardOutput.ReadToEnd().Trim()
                : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                   or IOException
                                   or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(selectedVersion))
            return null;

        var sdkPath = GetDotnetRoots()
            .Select(root => Path.Combine(root, "sdk", selectedVersion))
            .FirstOrDefault(path => File.Exists(Path.Combine(path, "MSBuild.dll")));
        if (sdkPath is null)
            return null;

        var identity = MsBuildInstanceIdentity.Create(
            sdkPath,
            ParseVersion(selectedVersion));
        return new MsBuildInstanceSelection(
            identity,
            () => RegisterExactPath(identity));
    }

    private static void RegisterExactPath(MsBuildInstanceIdentity identity)
    {
        lock (RegistrationLock)
        {
            if (MSBuildLocator.IsRegistered)
            {
                if (_registeredSdkIdentity?.Matches(identity) == true)
                    return;

                throw new InvalidOperationException(
                    "MSBuild was already registered before its exact SDK identity could be verified.");
            }

            MSBuildLocator.RegisterMSBuildPath(identity.MSBuildPath);
            _registeredSdkIdentity = identity;
        }
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
