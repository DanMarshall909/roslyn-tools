using Microsoft.Build.Locator;
using RoslynTools.Core;

namespace RoslynTools;

internal static class MsBuildBootstrap
{
    internal static void Register()
    {
        VisualStudioInstance? sdkInstance = null;
        try
        {
            sdkInstance = FindPreferredInstance();
        }
        catch (DllNotFoundException)
        {
        }

        if (sdkInstance != null)
        {
            MSBuildLocator.RegisterInstance(sdkInstance);
            return;
        }

        try
        {
            MSBuildLocator.RegisterDefaults();
            return;
        }
        catch (DllNotFoundException ex)
        {
            RtLog.Warning(
                "[MsBuildBootstrap] MSBuildLocator.RegisterDefaults failed ({Message}); falling back to manual SDK discovery.",
                ex.Message);
        }

        var sdkPath = FindLatestDotnetSdkPath();
        if (sdkPath == null)
            throw new InvalidOperationException(
                "Unable to locate a .NET SDK for MSBuild. Install a .NET SDK or avoid single-file publish.");

        MSBuildLocator.RegisterMSBuildPath(sdkPath);
    }

    // Prefer a .NET SDK instance over VS's .NET Framework MSBuild — the Framework host
    // causes TypeInitializationException when loading .NET 8+ projects.
    internal static VisualStudioInstance? FindPreferredInstance()
    {
        return MSBuildLocator.QueryVisualStudioInstances()
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
