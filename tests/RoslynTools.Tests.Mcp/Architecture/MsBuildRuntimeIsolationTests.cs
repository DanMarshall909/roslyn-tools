using System.Text.Json;
using Shouldly;

namespace RoslynTools.Tests.Mcp.Architecture;

public sealed class MsBuildRuntimeIsolationTests
{
    [Fact]
    public void runtime_manifests_leave_msbuild_assemblies_to_locator()
    {
        var manifests = new[]
        {
            (ManifestName: "rt.deps.json", HostAssemblyName: "rt.dll"),
            (ManifestName: "RoslynTools.Mcp.deps.json", HostAssemblyName: "RoslynTools.Mcp.dll"),
            (ManifestName: "RoslynTools.Tests.deps.json", HostAssemblyName: "RoslynTools.Tests.dll"),
            (ManifestName: "RoslynTools.Tests.Mcp.deps.json", HostAssemblyName: "RoslynTools.Tests.Mcp.dll")
        };
        var locatorOwnedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Microsoft.Build.dll",
            "Microsoft.Build.Framework.dll",
            "Microsoft.Build.Utilities.Core.dll",
            "Microsoft.Build.Tasks.Core.dll"
        };
        var violations = new List<string>();

        foreach (var (manifestName, hostAssemblyName) in manifests)
        {
            var context = $"{manifestName} ({hostAssemblyName})";
            var manifestPath = Path.Combine(AppContext.BaseDirectory, manifestName);
            if (!File.Exists(manifestPath))
            {
                violations.Add($"{context}: generated dependency manifest is missing");
                continue;
            }

            try
            {
                using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var root = manifest.RootElement;
                var targetName = root.GetProperty("runtimeTarget").GetProperty("name").GetString();
                if (string.IsNullOrWhiteSpace(targetName))
                {
                    violations.Add($"{context}: runtimeTarget.name must select a runtime target");
                    continue;
                }

                var target = root.GetProperty("targets").GetProperty(targetName);
                var hostAssemblyFound = false;
                foreach (var library in target.EnumerateObject())
                {
                    foreach (var assetGroup in library.Value.EnumerateObject())
                    {
                        if (assetGroup.Name is not ("runtime" or "runtimeTargets"))
                        {
                            continue;
                        }

                        foreach (var asset in assetGroup.Value.EnumerateObject())
                        {
                            if (assetGroup.Name == "runtimeTargets"
                                && (asset.Value.ValueKind != JsonValueKind.Object
                                    || !asset.Value.TryGetProperty("assetType", out var assetType)
                                    || assetType.ValueKind != JsonValueKind.String
                                    || assetType.GetString() != "runtime"))
                            {
                                continue;
                            }

                            var assemblyName = Path.GetFileName(asset.Name.Replace('\\', '/'));
                            if (!assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            hostAssemblyFound |= string.Equals(
                                assemblyName, hostAssemblyName, StringComparison.OrdinalIgnoreCase);
                            if (locatorOwnedAssemblies.Contains(assemblyName))
                            {
                                violations.Add($"{context}: application-local {assemblyName} "
                                    + $"[{library.Name}; {assetGroup.Name}/{asset.Name}]");
                            }
                        }
                    }
                }

                if (!hostAssemblyFound)
                {
                    violations.Add($"{context}: selected runtime target '{targetName}' must include {hostAssemblyName}");
                }
            }
            catch (Exception exception) when (exception is IOException or JsonException
                or KeyNotFoundException or InvalidOperationException)
            {
                violations.Add($"{context}: cannot read required runtime assets: {exception.Message}");
            }
        }

        violations.ShouldBeEmpty(
            "All four host manifests must leave SDK-owned MSBuild runtime assemblies to MSBuildLocator");
    }
}
