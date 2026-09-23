using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Microsoft.Build.Evaluation;
using Microsoft.Build.Exceptions;
using Microsoft.CodeAnalysis;

using BuildProject = Microsoft.Build.Evaluation.Project;
using RoslynProject = Microsoft.CodeAnalysis.Project;

namespace RoslynTools.Core;

internal sealed record WorkspaceAcquisitionDiscovery(
    SemanticInputManifest Manifest,
    ImmutableArray<SemanticInputOutcome> Outcomes,
    ImmutableArray<WorkspaceAcquisitionDiagnostic> Diagnostics,
    string Configuration,
    ImmutableArray<string> TargetFrameworks,
    string SdkIdentity,
    bool IsClosed);

/// <summary>
/// Evaluates the requested MSBuild projects independently of the Roslyn projects that happen to
/// load, then reconciles that declared input set with the exact loaded solution snapshot.
/// </summary>
internal static class WorkspaceAcquisitionCollector
{
    private sealed record ResolutionSnapshot(
        IReadOnlyDictionary<string, HashSet<string>> Packages,
        IReadOnlySet<string> Frameworks,
        string? Failure);

    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    internal static async Task<WorkspaceAcquisitionDiscovery> DiscoverAsync(
        WorkspaceManifestSeed seed,
        IReadOnlyDictionary<string, string> globalProperties,
        CancellationToken cancellationToken)
    {
        var inputs = new Dictionary<string, ExpectedSemanticInput>(StringComparer.Ordinal);
        var outcomes = new Dictionary<string, SemanticInputOutcome>(StringComparer.Ordinal);
        var diagnostics = seed.Diagnostics.ToBuilder();
        var targetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var configuration = globalProperties.TryGetValue("Configuration", out var configured)
            ? configured
            : Environment.GetEnvironmentVariable("Configuration") ?? "Debug";
        var sdkIdentities = new HashSet<string>(StringComparer.Ordinal);
        var isClosed = seed.Diagnostics.All(diagnostic => diagnostic.Impact == CompletenessDiagnosticImpact.None);

        foreach (var input in seed.Manifest.Inputs.Where(input => !input.Id.StartsWith("scope:", StringComparison.Ordinal)))
        {
            inputs.Add(input.Id, input);
            if (input.Kind == SemanticInputKind.BuildInput && input.Fingerprint is not null)
                outcomes[input.Id] = new SemanticInputOutcome(input.Id, SemanticInputState.Resolved, input.Fingerprint);
        }

        var pendingProjects = new Queue<ExpectedSemanticInput>(
            inputs.Values.Where(input => input.Kind == SemanticInputKind.Project));
        var discoveredProjects = pendingProjects
            .Select(input => input.ProjectPath ?? input.Path ?? input.Id)
            .Select(Path.GetFullPath)
            .ToHashSet(PathComparer);
        while (pendingProjects.Count > 0)
        {
            var projectInput = pendingProjects.Dequeue();
            cancellationToken.ThrowIfCancellationRequested();
            var projectPath = projectInput.ProjectPath ?? projectInput.Path ?? projectInput.Id;
            if (!File.Exists(projectPath))
            {
                isClosed = false;
                outcomes[projectInput.Id] = new SemanticInputOutcome(projectInput.Id, SemanticInputState.Failed,
                    Detail: "The requested project file does not exist.");
                diagnostics.Add(Failure("acquisition.project-file", "The requested project file does not exist.",
                    projectInput.Id, projectPath));
                AddUnavailableContext(projectPath);
                continue;
            }

            try
            {
                using var outerCollection = new ProjectCollection(ToDictionary(globalProperties));
                var outerProject = outerCollection.LoadProject(projectPath);
                var contexts = GetTargetFrameworks(outerProject);
                if (contexts.Length == 0)
                    contexts = [string.Empty];

                foreach (var targetFramework in contexts)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var contextProperties = ToDictionary(globalProperties);
                    if (!string.IsNullOrWhiteSpace(targetFramework))
                        contextProperties["TargetFramework"] = targetFramework;
                    contextProperties["Configuration"] = configuration;

                    using var contextCollection = new ProjectCollection(contextProperties);
                    var evaluated = contextCollection.LoadProject(projectPath);
                    var effectiveFramework = evaluated.GetPropertyValue("TargetFramework");
                    if (string.IsNullOrWhiteSpace(effectiveFramework))
                        effectiveFramework = targetFramework;
                    if (!string.IsNullOrWhiteSpace(effectiveFramework))
                        targetFrameworks.Add(effectiveFramework);

                    var contextKey = ContextKey(projectPath, effectiveFramework);
                    AddInput(new ExpectedSemanticInput(contextKey, SemanticInputKind.CompilationContext, true,
                        ProjectPath: projectPath, Context: contextKey, Path: effectiveFramework));
                    outcomes[contextKey] = new SemanticInputOutcome(contextKey, SemanticInputState.Resolved,
                        Detail: $"MSBuild evaluated '{DisplayFramework(effectiveFramework)}'.");

                    var evaluationId = $"evaluation:{contextKey}";
                    AddInput(new ExpectedSemanticInput(evaluationId, SemanticInputKind.EvaluationStage, true,
                        ProjectPath: projectPath, Context: contextKey));
                    outcomes[evaluationId] = new SemanticInputOutcome(evaluationId, SemanticInputState.Resolved);

                    var sdkVersion = evaluated.GetPropertyValue("NETCoreSdkVersion");
                    var sdkPath = evaluated.GetPropertyValue("MSBuildSDKsPath");
                    var sdkIdentity = string.IsNullOrWhiteSpace(sdkVersion) && string.IsNullOrWhiteSpace(sdkPath)
                        ? "MSBuild SDK identity unavailable"
                        : $"{sdkVersion}@{sdkPath}";
                    sdkIdentities.Add(sdkIdentity);
                    var sdkId = $"sdk:{contextKey}";
                    AddInput(new ExpectedSemanticInput(sdkId, SemanticInputKind.Sdk, true,
                        ProjectPath: projectPath, Context: contextKey, Path: sdkIdentity));
                    outcomes[sdkId] = new SemanticInputOutcome(sdkId,
                        sdkIdentity == "MSBuild SDK identity unavailable" ? SemanticInputState.Failed : SemanticInputState.Resolved);
                    if (sdkIdentity == "MSBuild SDK identity unavailable")
                    {
                        isClosed = false;
                        diagnostics.Add(Failure("acquisition.sdk", "MSBuild did not expose an SDK identity.", sdkId, projectPath));
                    }

                    await AddBuildInputAsync(projectPath, cancellationToken);
                    foreach (var import in evaluated.Imports.Select(import => import.ImportedProject.FullPath)
                                 .Where(path => !string.IsNullOrWhiteSpace(path))
                                 .Distinct(PathComparer))
                    {
                        await AddBuildInputAsync(import, cancellationToken);
                    }

                    await AddDocumentsAsync(evaluated, "Compile", SemanticInputKind.SourceDocument, contextKey, cancellationToken);
                    await AddDocumentsAsync(evaluated, "AdditionalFiles", SemanticInputKind.AdditionalDocument, contextKey, cancellationToken);
                    await AddDocumentsAsync(evaluated, "EditorConfigFiles", SemanticInputKind.AnalyzerConfigDocument, contextKey, cancellationToken);
                    AddProjectReferences(evaluated, contextKey);
                    await AddAnalyzerItemsAsync(evaluated, contextKey, cancellationToken);
                    await AddDeclaredMetadataReferencesAsync(evaluated, contextKey, cancellationToken);
                    var resolution = await ReadResolutionSnapshotAsync(
                        evaluated,
                        effectiveFramework,
                        contextKey,
                        cancellationToken);
                    AddResolutionInputs(evaluated, "PackageReference", SemanticInputKind.Package, "packages",
                        contextKey, resolution.Packages, resolution.Failure);
                    AddResolutionInputs(evaluated, "FrameworkReference", SemanticInputKind.FrameworkReference,
                        "framework-references", contextKey, resolution.Frameworks, resolutionFailure: null);

                    var generatorId = $"generators:{contextKey}";
                    AddInput(new ExpectedSemanticInput(generatorId, SemanticInputKind.GeneratorStage, true,
                        ProjectPath: projectPath, Context: contextKey));
                    var analyzerId = $"analyzers:{contextKey}";
                    AddInput(new ExpectedSemanticInput(analyzerId, SemanticInputKind.AnalyzerStage, true,
                        ProjectPath: projectPath, Context: contextKey));
                }
                outcomes[projectInput.Id] = new SemanticInputOutcome(projectInput.Id, SemanticInputState.Resolved,
                    Detail: "MSBuild evaluated the requested project and all declared contexts.");
            }
            catch (Exception exception) when (exception is InvalidProjectFileException or IOException
                                               or UnauthorizedAccessException or InvalidOperationException)
            {
                isClosed = false;
                outcomes[projectInput.Id] = new SemanticInputOutcome(projectInput.Id, SemanticInputState.Failed,
                    Detail: exception.Message);
                diagnostics.Add(Failure("acquisition.project-evaluation", exception.Message,
                    projectInput.Id, projectPath));
                AddUnavailableContext(projectPath);
            }
        }

        var closure = new ExpectedSemanticInput("manifest:closure", SemanticInputKind.ManifestClosure, true);
        AddInput(closure);
        outcomes[closure.Id] = new SemanticInputOutcome(closure.Id,
            isClosed ? SemanticInputState.Resolved : SemanticInputState.Unknown);
        var snapshot = new ExpectedSemanticInput("source:snapshot", SemanticInputKind.SourceSnapshot, true);
        AddInput(snapshot);

        if (!isClosed)
        {
            foreach (var unresolved in WorkspaceManifestDiscovery.UnresolvedScopeInputs()
                         .Where(input => input.Kind is not SemanticInputKind.ManifestClosure
                             and not SemanticInputKind.SourceSnapshot))
                AddInput(unresolved);
        }

        foreach (var input in inputs.Values)
        {
            if (!outcomes.ContainsKey(input.Id))
                outcomes[input.Id] = new SemanticInputOutcome(input.Id, SemanticInputState.Unknown);
        }

        return new WorkspaceAcquisitionDiscovery(
            new SemanticInputManifest(seed.Manifest.SolutionPath, inputs.Values,
                isClosed ? ManifestDiscoveryState.Closed : ManifestDiscoveryState.Unknown),
            outcomes.Values.ToImmutableArray(),
            diagnostics.ToImmutable(),
            configuration,
            targetFrameworks.Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray(),
            sdkIdentities.Count == 0 ? "Unknown" : string.Join("; ", sdkIdentities.Order(StringComparer.Ordinal)),
            isClosed);

        void AddInput(ExpectedSemanticInput input) => inputs.TryAdd(input.Id, input);

        void AddUnavailableContext(string projectPath)
        {
            var id = ContextKey(projectPath, "unavailable");
            AddInput(new ExpectedSemanticInput(id, SemanticInputKind.CompilationContext, true,
                ProjectPath: projectPath, Context: id, Path: "unavailable"));
            outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Failed,
                Detail: "MSBuild could not discover this project's compilation contexts.");
        }

        async Task AddBuildInputAsync(string path, CancellationToken token)
        {
            var fullPath = Path.GetFullPath(path);
            var id = $"build:{fullPath}";
            if (inputs.ContainsKey(id))
                return;
            try
            {
                var bytes = await File.ReadAllBytesAsync(fullPath, token);
                var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                AddInput(new ExpectedSemanticInput(id, SemanticInputKind.BuildInput, true,
                    Path: fullPath, Fingerprint: fingerprint));
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved, fingerprint);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                isClosed = false;
                AddInput(new ExpectedSemanticInput(id, SemanticInputKind.BuildInput, true, Path: fullPath));
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Failed, Detail: exception.Message);
                diagnostics.Add(Failure("acquisition.build-input", exception.Message, id));
            }
        }

        async Task AddDocumentsAsync(BuildProject project, string itemType, SemanticInputKind kind,
            string contextKey, CancellationToken token)
        {
            foreach (var item in project.GetItems(itemType))
            {
                var path = ItemPath(item);
                var id = $"{kind}:{contextKey}:{path}";
                try
                {
                    await AddBuildInputAsync(path, token);
                    var text = await File.ReadAllTextAsync(path, token);
                    var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
                    AddInput(new ExpectedSemanticInput(id, kind, true, ProjectPath: project.FullPath,
                        Context: contextKey, Path: path, Fingerprint: fingerprint));
                    outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Loaded, fingerprint);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    AddInput(new ExpectedSemanticInput(id, kind, true, ProjectPath: project.FullPath,
                        Context: contextKey, Path: path));
                    outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Failed, Detail: exception.Message);
                    diagnostics.Add(Failure("acquisition.document", exception.Message, id, project.FullPath));
                }
            }
        }

        void AddProjectReferences(BuildProject project, string contextKey)
        {
            foreach (var item in project.GetItems("ProjectReference"))
            {
                var path = ItemPath(item);
                var id = $"{SemanticInputKind.ProjectReference}:{contextKey}:{path}";
                AddInput(new ExpectedSemanticInput(id, SemanticInputKind.ProjectReference, true,
                    ProjectPath: project.FullPath, Context: contextKey, Path: path));
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved);
                if (!discoveredProjects.Add(path))
                    continue;

                var transitiveProject = new ExpectedSemanticInput(
                    WorkspaceManifestDiscovery.ProjectInputId(path),
                    SemanticInputKind.Project,
                    true,
                    ProjectPath: path,
                    Path: path);
                AddInput(transitiveProject);
                pendingProjects.Enqueue(transitiveProject);
            }
        }

        async Task AddAnalyzerItemsAsync(BuildProject project, string contextKey, CancellationToken token)
        {
            var index = 0;
            foreach (var item in project.GetItems("Analyzer"))
            {
                var path = ItemPath(item);
                await AddBuildInputAsync(path, token);
                var identity = $"analyzer:{index++}:{path}";
                var id = $"{SemanticInputKind.AnalyzerReference}:{contextKey}:{identity}";
                AddInput(new ExpectedSemanticInput(id, SemanticInputKind.AnalyzerReference, true,
                    ProjectPath: project.FullPath, Context: contextKey, Path: identity));
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved);
            }
        }

        async Task AddDeclaredMetadataReferencesAsync(
            BuildProject project,
            string contextKey,
            CancellationToken token)
        {
            var index = 0;
            foreach (var item in project.GetItems("Reference"))
            {
                var include = item.EvaluatedInclude;
                var hintPath = item.GetMetadataValue("HintPath");
                var path = string.IsNullOrWhiteSpace(hintPath)
                    ? include
                    : Path.GetFullPath(hintPath, Path.GetDirectoryName(project.FullPath)!);
                var id = $"metadata-declared:{contextKey}:{index++}:{include}";
                AddInput(new ExpectedSemanticInput(id, SemanticInputKind.MetadataReference, true,
                    ProjectPath: project.FullPath, Context: contextKey, Path: path));

                if (string.IsNullOrWhiteSpace(hintPath))
                {
                    outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved,
                        Detail: "The evaluated assembly reference awaits reconciliation with the loaded metadata graph.");
                    continue;
                }

                await AddBuildInputAsync(path, token);
                if (File.Exists(path))
                {
                    outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved,
                        Detail: "The evaluated assembly reference path exists and awaits loaded-graph reconciliation.");
                    continue;
                }

                isClosed = false;
                const string message = "The evaluated assembly reference hint path does not exist.";
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Failed, Detail: message);
                diagnostics.Add(Failure("acquisition.metadata-reference", message, id, project.FullPath));
            }
        }

        async Task<ResolutionSnapshot> ReadResolutionSnapshotAsync(
            BuildProject project,
            string targetFramework,
            string contextKey,
            CancellationToken token)
        {
            var packages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            var frameworks = project.GetItems("KnownFrameworkReference")
                .Where(item => item.GetMetadataValue("TargetFramework")
                    .Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.EvaluatedInclude)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (project.GetItems("PackageReference").Count == 0)
                return new ResolutionSnapshot(packages, frameworks, null);

            var assetsPath = project.GetPropertyValue("ProjectAssetsFile");
            if (string.IsNullOrWhiteSpace(assetsPath))
            {
                var extensionsPath = project.GetPropertyValue("MSBuildProjectExtensionsPath");
                assetsPath = Path.Combine(
                    string.IsNullOrWhiteSpace(extensionsPath) ? "obj" : extensionsPath,
                    "project.assets.json");
            }
            if (!Path.IsPathFullyQualified(assetsPath))
                assetsPath = Path.GetFullPath(assetsPath, Path.GetDirectoryName(project.FullPath)!);

            await AddBuildInputAsync(assetsPath, token);
            try
            {
                await using var stream = File.OpenRead(assetsPath);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
                if (document.RootElement.TryGetProperty("libraries", out var libraries))
                {
                    foreach (var library in libraries.EnumerateObject())
                    {
                        if (!library.Value.TryGetProperty("type", out var type)
                            || !string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase))
                            continue;
                        var separator = library.Name.LastIndexOf('/');
                        if (separator <= 0 || separator == library.Name.Length - 1)
                            continue;
                        var package = library.Name[..separator];
                        if (!packages.TryGetValue(package, out var versions))
                        {
                            versions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            packages.Add(package, versions);
                        }
                        versions.Add(library.Name[(separator + 1)..]);
                    }
                }

                if (document.RootElement.TryGetProperty("project", out var projectElement)
                    && projectElement.TryGetProperty("frameworks", out var frameworkMap))
                {
                    JsonElement framework = default;
                    var foundFramework = frameworkMap.TryGetProperty(targetFramework, out framework);
                    if (!foundFramework)
                    {
                        foreach (var candidate in frameworkMap.EnumerateObject())
                        {
                            if (!candidate.Name.Equals(targetFramework, StringComparison.OrdinalIgnoreCase))
                                continue;
                            framework = candidate.Value;
                            foundFramework = true;
                            break;
                        }
                    }
                    if (foundFramework && framework.TryGetProperty("frameworkReferences", out var references))
                    {
                        foreach (var reference in references.EnumerateObject())
                            frameworks.Add(reference.Name);
                    }
                }

                var assetFailure = await ValidatePackageAssetsAsync(
                    document.RootElement,
                    project,
                    targetFramework,
                    contextKey,
                    token);
                return new ResolutionSnapshot(packages, frameworks, assetFailure);
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or JsonException)
            {
                isClosed = false;
                diagnostics.Add(Failure("acquisition.resolution-assets", exception.Message,
                    projectPath: project.FullPath));
                return new ResolutionSnapshot(
                    new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase),
                    frameworks,
                    exception.Message);
            }
        }

        async Task<string?> ValidatePackageAssetsAsync(
            JsonElement root,
            BuildProject project,
            string targetFramework,
            string contextKey,
            CancellationToken token)
        {
            if (!root.TryGetProperty("libraries", out var libraries)
                || !root.TryGetProperty("targets", out var targets)
                || !root.TryGetProperty("packageFolders", out var packageFolders))
                return "The restore graph does not contain libraries, targets, and package-folder evidence.";

            var packagePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Value.TryGetProperty("type", out var type)
                    && string.Equals(type.GetString(), "package", StringComparison.OrdinalIgnoreCase)
                    && library.Value.TryGetProperty("path", out var path)
                    && !string.IsNullOrWhiteSpace(path.GetString()))
                    packagePaths[library.Name] = path.GetString()!;
            }

            var roots = packageFolders.EnumerateObject()
                .Select(folder => Path.GetFullPath(folder.Name))
                .ToArray();
            var selectedTarget = targets.EnumerateObject()
                .OrderBy(target => target.Name.Contains('/'))
                .FirstOrDefault(target => target.Name.Equals(targetFramework, StringComparison.OrdinalIgnoreCase)
                    || target.Name.StartsWith(targetFramework + "/", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(selectedTarget.Name))
                return $"The restore graph has no target for '{targetFramework}'.";

            var targetPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var missingAssets = new List<string>();
            foreach (var library in selectedTarget.Value.EnumerateObject())
            {
                if (!packagePaths.TryGetValue(library.Name, out var packagePath))
                    continue;
                targetPackages.Add(library.Name);
                foreach (var group in library.Value.EnumerateObject().Where(group => IsPackageAssetGroup(group.Name)))
                {
                    if (group.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    foreach (var asset in group.Value.EnumerateObject())
                    {
                        token.ThrowIfCancellationRequested();
                        if (Path.GetFileName(asset.Name).Equals("_._", StringComparison.Ordinal))
                            continue;
                        var candidates = roots.Select(root => Path.GetFullPath(
                                asset.Name.Replace('/', Path.DirectorySeparatorChar),
                                Path.Combine(root, packagePath.Replace('/', Path.DirectorySeparatorChar))))
                            .ToArray();
                        var assetPath = candidates.FirstOrDefault(File.Exists)
                            ?? candidates.FirstOrDefault();
                        if (assetPath is null)
                        {
                            missingAssets.Add($"{library.Name}:{asset.Name}");
                            continue;
                        }

                        await AddBuildInputAsync(assetPath, token);
                        var exists = File.Exists(assetPath);
                        if (!exists)
                            missingAssets.Add(assetPath);
                        if (!group.Name.Equals("compile", StringComparison.OrdinalIgnoreCase)
                            || !asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        var id = $"metadata-package:{contextKey}:{library.Name}:{asset.Name}";
                        AddInput(new ExpectedSemanticInput(
                            id,
                            SemanticInputKind.MetadataReference,
                            true,
                            ProjectPath: project.FullPath,
                            Context: contextKey,
                            Path: assetPath));
                        outcomes[id] = new SemanticInputOutcome(
                            id,
                            exists ? SemanticInputState.Resolved : SemanticInputState.Failed,
                            Detail: exists
                                ? "The restored compile asset awaits reconciliation with the loaded metadata graph."
                                : "The restored compile asset is absent from the package folder.");
                    }
                }
            }

            foreach (var item in project.GetItems("PackageReference"))
            {
                var prefix = item.EvaluatedInclude + "/";
                if (!targetPackages.Any(package => package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    missingAssets.Add($"restore-target:{item.EvaluatedInclude}");
            }
            if (missingAssets.Count == 0)
                return null;

            isClosed = false;
            var first = missingAssets[0];
            return $"The restore graph has {missingAssets.Count} missing or unresolved package asset(s); first: '{first}'.";
        }

        static bool IsPackageAssetGroup(string name) => name is
            "compile" or "runtime" or "runtimeTargets" or "native" or "resource"
            or "build" or "buildMultiTargeting" or "buildTransitive" or "contentFiles";

        void AddResolutionInputs(BuildProject project, string itemType, SemanticInputKind kind,
            string label, string contextKey, object resolvedInputs, string? resolutionFailure)
        {
            var items = project.GetItems(itemType).ToArray();
            if (items.Length == 0)
            {
                var id = $"{label}:{contextKey}:none";
                AddInput(new ExpectedSemanticInput(id, kind, true, ProjectPath: project.FullPath,
                    Context: contextKey, Path: "none"));
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Excluded,
                    Detail: $"The evaluated context declares no {label}.", ExclusionValidated: true);
                return;
            }

            foreach (var item in items)
            {
                var version = item.GetMetadataValue("VersionOverride");
                if (string.IsNullOrWhiteSpace(version))
                    version = item.GetMetadataValue("Version");
                var identity = string.IsNullOrWhiteSpace(version)
                    ? item.EvaluatedInclude
                    : $"{item.EvaluatedInclude}/{version}";
                var id = $"{label}:{contextKey}:{identity}";
                AddInput(new ExpectedSemanticInput(id, kind, true, ProjectPath: project.FullPath,
                    Context: contextKey, Path: identity));
                var resolved = kind switch
                {
                    SemanticInputKind.Package => PackageResolved(
                        (IReadOnlyDictionary<string, HashSet<string>>)resolvedInputs,
                        item.EvaluatedInclude,
                        version)
                        && resolutionFailure is null,
                    SemanticInputKind.FrameworkReference =>
                        ((IReadOnlySet<string>)resolvedInputs).Contains(item.EvaluatedInclude),
                    _ => false
                };
                var detail = resolved
                    ? $"The evaluated {label} declaration has concrete restore resolution evidence."
                    : resolutionFailure ?? $"The evaluated {label} declaration has no concrete resolution evidence.";
                outcomes[id] = new SemanticInputOutcome(
                    id,
                    resolved ? SemanticInputState.Resolved : SemanticInputState.Failed,
                    Detail: detail);
                if (!resolved)
                {
                    isClosed = false;
                    diagnostics.Add(Failure($"acquisition.{label}", detail, id, project.FullPath));
                }
            }
        }

        static bool PackageResolved(
            IReadOnlyDictionary<string, HashSet<string>> packages,
            string package,
            string? declaredVersion)
        {
            if (!packages.TryGetValue(package, out var versions))
                return false;
            if (string.IsNullOrWhiteSpace(declaredVersion)
                || declaredVersion.IndexOfAny(['*', '[', ']', '(', ')', ',']) >= 0)
                return versions.Count > 0;
            return versions.Contains(declaredVersion);
        }
    }

    internal static async Task<(SemanticInputManifest Manifest, ImmutableArray<SemanticInputOutcome> Outcomes,
        WorkspaceEvaluatorProvenance Provenance)> CompleteAsync(
        Solution solution,
        WorkspaceAcquisitionDiscovery discovery,
        TrustedWorkspaceIdentity? workspaceIdentity,
        WorkspaceInputFingerprint? inputFingerprint,
        WorkspaceInputFingerprintResult? preLoadInputSet,
        bool discoveryWindowStable,
        CancellationToken cancellationToken)
    {
        var inputs = discovery.Manifest.Inputs.ToDictionary(input => input.Id, StringComparer.Ordinal);
        var outcomes = discovery.Outcomes.ToDictionary(outcome => outcome.InputId, StringComparer.Ordinal);
        var projectsByPath = solution.Projects
            .Where(project => project.FilePath is not null)
            .GroupBy(project => Path.GetFullPath(project.FilePath!), PathComparer)
            .ToDictionary(group => group.Key, group => group.ToList(), PathComparer);
        var contextProjects = new Dictionary<string, RoslynProject>(StringComparer.Ordinal);

        foreach (var group in inputs.Values.Where(input => input.Kind == SemanticInputKind.CompilationContext)
                     .GroupBy(input => input.ProjectPath ?? string.Empty, PathComparer))
        {
            projectsByPath.TryGetValue(group.Key, out var loaded);
            loaded ??= [];
            var remaining = loaded.ToList();
            foreach (var context in group.OrderBy(input => input.Path, StringComparer.OrdinalIgnoreCase))
            {
                var match = FindContextProject(remaining, context.Path);
                if (match is null)
                {
                    outcomes[context.Id] = new SemanticInputOutcome(context.Id, SemanticInputState.Failed,
                        Detail: $"The evaluated context '{context.Path}' has no loaded Roslyn project.");
                    continue;
                }

                remaining.Remove(match);
                contextProjects[context.Context!] = match;
                var observedContext = WorkspaceCompleteness.CompilationContextIdentity(match);
                ReplaceContext(inputs, context.Context!, observedContext);
                contextProjects.Remove(context.Context!);
                contextProjects[observedContext] = match;
                outcomes[context.Id] = MergeCollectorOutcome(outcomes[context.Id],
                    new SemanticInputOutcome(context.Id, SemanticInputState.Loaded,
                        Detail: $"Loaded Roslyn context for '{DisplayFramework(context.Path)}'."));
            }
        }

        foreach (var projectInput in inputs.Values.Where(input => input.Kind == SemanticInputKind.Project))
        {
            var path = projectInput.ProjectPath ?? projectInput.Path ?? projectInput.Id;
            outcomes[projectInput.Id] = MergeCollectorOutcome(outcomes[projectInput.Id],
                new SemanticInputOutcome(projectInput.Id,
                    projectsByPath.ContainsKey(Path.GetFullPath(path)) ? SemanticInputState.Loaded : SemanticInputState.Failed));
        }

        foreach (var input in inputs.Values.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (input.Context is null || !contextProjects.TryGetValue(input.Context, out var project))
                continue;
            if (input.Kind == SemanticInputKind.MetadataReference)
            {
                var (metadataOutcome, canonicalPath) = ObserveMetadataReference(input, project);
                if (canonicalPath is not null)
                    inputs[input.Id] = input with { Path = canonicalPath };
                outcomes[input.Id] = MergeCollectorOutcome(outcomes[input.Id], metadataOutcome);
                continue;
            }
            var observed = input.Kind switch
            {
                SemanticInputKind.SourceDocument => ObserveDocument(input, project.Documents),
                SemanticInputKind.AdditionalDocument => ObserveDocument(input, project.AdditionalDocuments),
                SemanticInputKind.AnalyzerConfigDocument => ObserveDocument(input, project.AnalyzerConfigDocuments),
                SemanticInputKind.ProjectReference => ObserveProjectReference(input, project, solution),
                SemanticInputKind.AnalyzerReference => ObserveAnalyzerReference(input, project),
                _ => outcomes[input.Id]
            };
            outcomes[input.Id] = MergeCollectorOutcome(outcomes[input.Id], observed);
        }

        foreach (var pair in contextProjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = pair.Key;
            var project = pair.Value;
            await AddLoadedDocumentsAsync(project.Documents, SemanticInputKind.SourceDocument, project, context);
            await AddLoadedDocumentsAsync(project.AdditionalDocuments, SemanticInputKind.AdditionalDocument, project, context);
            await AddLoadedDocumentsAsync(project.AnalyzerConfigDocuments, SemanticInputKind.AnalyzerConfigDocument, project, context);
            for (var analyzerIndex = 0; analyzerIndex < project.AnalyzerReferences.Count; analyzerIndex++)
            {
                if (project.AnalyzerReferences[analyzerIndex].FullPath is { } analyzerPath
                    && Path.IsPathFullyQualified(analyzerPath))
                {
                    await AddPhysicalBuildInputAsync(analyzerPath);
                }
                var path = WorkspaceCompleteness.AnalyzerReferenceIdentity(
                    project.AnalyzerReferences[analyzerIndex], analyzerIndex);
                if (inputs.Values.Any(input => input.Kind == SemanticInputKind.AnalyzerReference
                    && input.Context == context && PathComparer.Equals(input.Path, path)))
                    continue;
                var id = $"{SemanticInputKind.AnalyzerReference}:{context}:{path}";
                inputs[id] = new ExpectedSemanticInput(id, SemanticInputKind.AnalyzerReference, true,
                    ProjectPath: WorkspaceCompleteness.ProjectIdentity(project), Context: context, Path: path);
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved);
            }
            var generatedStage = EnsureStage(SemanticInputKind.GeneratorStage, "generators", project, context);
            var analyzerStage = EnsureStage(SemanticInputKind.AnalyzerStage, "analyzers", project, context);
            try
            {
                var generatorCount = project.AnalyzerReferences
                    .Sum(reference => reference.GetGenerators(project.Language).Length);
                var generated = await project.GetSourceGeneratedDocumentsAsync(cancellationToken);
                outcomes[generatedStage.Id] = new SemanticInputOutcome(generatedStage.Id,
                    generatorCount == 0 ? SemanticInputState.Excluded : SemanticInputState.Resolved,
                    Detail: generatorCount == 0 ? "The context has no source generators." : null,
                    ExclusionValidated: generatorCount == 0);
                foreach (var document in generated)
                    await AddObservedDocumentAsync(document, SemanticInputKind.GeneratedDocument, project, context);
            }
            catch (Exception exception)
            {
                outcomes[generatedStage.Id] = new SemanticInputOutcome(generatedStage.Id,
                    SemanticInputState.Failed, Detail: exception.Message);
            }

            try
            {
                var analyzerCount = project.AnalyzerReferences
                    .Sum(reference => reference.GetAnalyzers(project.Language).Length);
                outcomes[analyzerStage.Id] = new SemanticInputOutcome(analyzerStage.Id,
                    SemanticInputState.Excluded,
                    Detail: analyzerCount == 0
                        ? "The context has no diagnostic analyzers."
                        : "Diagnostic analyzer loadability was verified; analyzer diagnostics are not consumed by current semantic operations.",
                    ExclusionValidated: true);
            }
            catch (Exception exception)
            {
                outcomes[analyzerStage.Id] = new SemanticInputOutcome(analyzerStage.Id,
                    SemanticInputState.Failed, Detail: exception.Message);
            }

            for (var index = 0; index < project.MetadataReferences.Count; index++)
            {
                var reference = project.MetadataReferences[index];
                if (reference.Display is { } referencePath && Path.IsPathFullyQualified(referencePath))
                    await AddPhysicalBuildInputAsync(referencePath);
                var path = WorkspaceCompleteness.MetadataReferenceIdentity(reference, index);
                if (inputs.Values.Any(input => input.Kind == SemanticInputKind.MetadataReference
                    && input.Context == context && PathComparer.Equals(input.Path, path)))
                    continue;
                var id = $"metadata:{context}:{index}:{path}";
                inputs[id] = new ExpectedSemanticInput(id, SemanticInputKind.MetadataReference, true,
                    ProjectPath: WorkspaceCompleteness.ProjectIdentity(project), Context: context, Path: path);
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved);
            }
        }

        const string inputSetId = "workspace:input-set";
        if (workspaceIdentity is null || inputFingerprint is null || preLoadInputSet is null)
        {
            inputs[inputSetId] = new ExpectedSemanticInput(
                inputSetId,
                SemanticInputKind.WorkspaceInputSet,
                true);
            outcomes[inputSetId] = new SemanticInputOutcome(
                inputSetId,
                SemanticInputState.PolicyBlocked,
                Detail: "Workspace input membership cannot be bound before and after load under a trusted repository identity.");
        }
        else
        {
            try
            {
                var projectPaths = discovery.Manifest.Inputs
                    .Where(input => input.Kind == SemanticInputKind.Project)
                    .Select(input => input.ProjectPath ?? input.Path ?? input.Id)
                    .ToArray();
                var beforeInputSet = preLoadInputSet.Value;
                var afterRepositoryInputSet = await inputFingerprint.CaptureRepositoryAsync(
                    workspaceIdentity,
                    projectPaths,
                    cancellationToken);
                var inputSet = await inputFingerprint.CaptureAsync(
                    workspaceIdentity,
                    solution,
                    cancellationToken);
                var finalRepositoryInputSet = await inputFingerprint.CaptureRepositoryAsync(
                    workspaceIdentity,
                    projectPaths,
                    cancellationToken);
                var stable = discoveryWindowStable
                    && beforeInputSet.Reusable
                    && afterRepositoryInputSet.Reusable
                    && inputSet.Reusable
                    && finalRepositoryInputSet.Reusable
                    && inputSet.SnapshotConsistent
                    && string.Equals(
                        beforeInputSet.Value,
                        afterRepositoryInputSet.Value,
                        StringComparison.Ordinal)
                    && string.Equals(
                        afterRepositoryInputSet.Value,
                        finalRepositoryInputSet.Value,
                        StringComparison.Ordinal);
                inputs[inputSetId] = new ExpectedSemanticInput(
                    inputSetId,
                    SemanticInputKind.WorkspaceInputSet,
                    true,
                    Context: "repository-membership-v1",
                    Path: workspaceIdentity.WorktreeRoot,
                    Fingerprint: inputSet.Value);
                outcomes[inputSetId] = new SemanticInputOutcome(
                    inputSetId,
                    stable
                        ? SemanticInputState.Resolved
                        : SemanticInputState.Failed,
                    inputSet.Value,
                    stable
                        ? "Stable pre-load and post-load repository membership, dynamic build inputs, and loaded snapshot are bound."
                        : !discoveryWindowStable
                            ? "Repository inputs changed while the declared semantic-input manifest was being discovered."
                            : !inputSet.SnapshotConsistent
                            ? "The loaded solution disagrees with the current input snapshot."
                            : !beforeInputSet.Reusable || !afterRepositoryInputSet.Reusable
                                || !inputSet.Reusable || !finalRepositoryInputSet.Reusable
                                ? "Workspace input discovery found state that cannot be reused safely."
                                : "Repository inputs changed during solution load.");
            }
            catch (Exception exception) when (exception is IOException
                                               or UnauthorizedAccessException
                                               or InvalidOperationException)
            {
                inputs[inputSetId] = new ExpectedSemanticInput(
                    inputSetId,
                    SemanticInputKind.WorkspaceInputSet,
                    true);
                outcomes[inputSetId] = new SemanticInputOutcome(
                    inputSetId,
                    SemanticInputState.Failed,
                    Detail: exception.Message);
            }
        }

        var revision = await SolutionRevision.ComputeAsync(solution, cancellationToken);
        var snapshot = inputs.Values.Single(input => input.Kind == SemanticInputKind.SourceSnapshot);
        inputs[snapshot.Id] = snapshot with { Fingerprint = revision };
        outcomes[snapshot.Id] = new SemanticInputOutcome(snapshot.Id, SemanticInputState.Resolved, revision);
        var closure = inputs.Values.Single(input => input.Kind == SemanticInputKind.ManifestClosure);
        outcomes[closure.Id] = new SemanticInputOutcome(closure.Id,
            discovery.IsClosed ? SemanticInputState.Resolved : SemanticInputState.Unknown);
        foreach (var input in inputs.Values)
            outcomes.TryAdd(input.Id, new SemanticInputOutcome(input.Id, SemanticInputState.Unknown));

        var manifest = new SemanticInputManifest(discovery.Manifest.SolutionPath, inputs.Values,
            discovery.IsClosed ? ManifestDiscoveryState.Closed : ManifestDiscoveryState.Unknown);
        var provenance = new WorkspaceEvaluatorProvenance(
            workspaceIdentity?.WorktreeRoot,
            solution.FilePath ?? discovery.Manifest.SolutionPath,
            revision,
            discovery.Configuration,
            discovery.TargetFrameworks,
            discovery.SdkIdentity,
            typeof(Compilation).Assembly.GetName().Version?.ToString() ?? "Unknown",
            "MSBuild inputs evaluated; generators executed; diagnostic analyzers loaded but not executed",
            workspaceIdentity is null ? "NotEstablished" : "Trusted",
            "MSBuild evaluation reconciled with Roslyn snapshot");
        return (manifest, outcomes.Values.ToImmutableArray(), provenance);

        static RoslynProject? FindContextProject(List<RoslynProject> projects, string? targetFramework)
        {
            if (projects.Count == 1)
                return projects[0];
            if (string.IsNullOrWhiteSpace(targetFramework))
                return null;
            return projects.FirstOrDefault(project =>
                project.Name.Contains($"({targetFramework})", StringComparison.OrdinalIgnoreCase)
                || PathContainsSegment(project.OutputFilePath, targetFramework)
                || PathContainsSegment(project.OutputRefFilePath, targetFramework));
        }

        static bool PathContainsSegment(string? path, string segment) => path is not null
            && Path.GetFullPath(path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Contains(segment, StringComparer.OrdinalIgnoreCase);

        static void ReplaceContext(Dictionary<string, ExpectedSemanticInput> values, string previous, string current)
        {
            foreach (var pair in values.Where(pair => pair.Value.Context == previous).ToArray())
                values[pair.Key] = pair.Value with { Context = current };
        }

        static SemanticInputOutcome ObserveDocument(ExpectedSemanticInput input, IEnumerable<TextDocument> documents)
        {
            var loaded = documents.Any(document => PathComparer.Equals(document.FilePath ?? document.Name, input.Path));
            return new SemanticInputOutcome(input.Id, loaded ? SemanticInputState.Loaded : SemanticInputState.Failed,
                loaded ? input.Fingerprint : null,
                Detail: loaded ? null : "The evaluated document is absent from the loaded Roslyn context.");
        }

        static SemanticInputOutcome ObserveProjectReference(ExpectedSemanticInput input, RoslynProject project, Solution current)
        {
            var loaded = project.ProjectReferences.Any(reference => current.GetProject(reference.ProjectId) is { } target
                && PathComparer.Equals(target.FilePath, input.Path));
            return new SemanticInputOutcome(input.Id, loaded ? SemanticInputState.Resolved : SemanticInputState.Failed,
                Detail: loaded ? null : "The evaluated project reference is absent from the loaded Roslyn context.");
        }

        static SemanticInputOutcome ObserveAnalyzerReference(ExpectedSemanticInput input, RoslynProject project)
        {
            var loaded = project.AnalyzerReferences.Select((reference, index) =>
                    WorkspaceCompleteness.AnalyzerReferenceIdentity(reference, index))
                .Any(identity => PathComparer.Equals(identity, input.Path));
            return new SemanticInputOutcome(input.Id, loaded ? SemanticInputState.Resolved : SemanticInputState.Failed,
                Detail: loaded ? null : "The evaluated analyzer reference is absent from the loaded Roslyn context.");
        }

        static (SemanticInputOutcome Outcome, string? CanonicalPath) ObserveMetadataReference(
            ExpectedSemanticInput input,
            RoslynProject project)
        {
            var references = project.MetadataReferences.Select((reference, index) => new
            {
                Reference = reference,
                CanonicalPath = WorkspaceCompleteness.MetadataReferenceIdentity(reference, index)
            }).ToArray();
            var match = references.FirstOrDefault(candidate => MetadataReferenceMatches(input.Path, candidate.Reference));
            if (match is not null)
            {
                return (new SemanticInputOutcome(input.Id, SemanticInputState.Resolved), match.CanonicalPath);
            }

            if (input.Id.StartsWith("metadata-package:", StringComparison.Ordinal)
                && TryHashFile(input.Path, out var expectedHash))
            {
                var equivalent = references.FirstOrDefault(candidate =>
                    HasDefaultPackageReferenceProperties(candidate.Reference)
                    && TryHashFile(candidate.Reference.Display, out var observedHash)
                    && string.Equals(expectedHash, observedHash, StringComparison.Ordinal));
                if (equivalent is not null)
                {
                    return (new SemanticInputOutcome(
                            input.Id,
                            SemanticInputState.Excluded,
                            expectedHash,
                            $"MSBuild selected byte-identical metadata from '{equivalent.CanonicalPath}' instead of the duplicate restore asset.",
                            ExclusionValidated: true),
                        null);
                }
            }

            return (new SemanticInputOutcome(
                    input.Id,
                    SemanticInputState.Failed,
                    Detail: "The evaluated metadata reference is absent from the loaded Roslyn context."),
                null);
        }

        static bool TryHashFile(string? path, out string hash)
        {
            hash = string.Empty;
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
                return false;
            try
            {
                hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                return true;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        static bool HasDefaultPackageReferenceProperties(MetadataReference reference) =>
            reference.Properties.Kind == MetadataImageKind.Assembly
            && reference.Properties.Aliases.IsDefaultOrEmpty
            && !reference.Properties.EmbedInteropTypes;

        static bool MetadataReferenceMatches(string? expected, MetadataReference reference)
        {
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(reference.Display))
                return false;
            if (Path.IsPathFullyQualified(expected))
            {
                return Path.IsPathFullyQualified(reference.Display)
                    && PathComparer.Equals(Path.GetFullPath(expected), Path.GetFullPath(reference.Display));
            }

            var simpleName = expected.Split(',', 2)[0].Trim();
            return Path.GetFileNameWithoutExtension(reference.Display)
                .Equals(simpleName, StringComparison.OrdinalIgnoreCase);
        }

        async Task AddLoadedDocumentsAsync(IEnumerable<TextDocument> documents, SemanticInputKind kind,
            RoslynProject project, string context)
        {
            foreach (var document in documents)
            {
                var path = document.FilePath ?? document.Name;
                if (document.FilePath is not null)
                    await AddPhysicalBuildInputAsync(document.FilePath);
                if (inputs.Values.Any(input => input.Kind == kind && input.Context == context
                    && PathComparer.Equals(input.Path, path)))
                    continue;
                var id = $"{kind}:{context}:{path}";
                var text = await document.GetTextAsync(cancellationToken);
                var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
                inputs[id] = new ExpectedSemanticInput(id, kind, true,
                    ProjectPath: WorkspaceCompleteness.ProjectIdentity(project), Context: context, Path: path,
                    Fingerprint: fingerprint);
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Loaded, fingerprint);
            }
        }

        ExpectedSemanticInput EnsureStage(SemanticInputKind kind, string label,
            RoslynProject project, string context)
        {
            var existing = inputs.Values.SingleOrDefault(input => input.Kind == kind && input.Context == context);
            if (existing is not null)
                return existing;
            var id = $"{label}:{context}";
            var added = new ExpectedSemanticInput(id, kind, true,
                ProjectPath: WorkspaceCompleteness.ProjectIdentity(project), Context: context);
            inputs[id] = added;
            return added;
        }

        async Task AddPhysicalBuildInputAsync(string path)
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(path);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                var invalidId = $"build:invalid:{path}";
                inputs.TryAdd(invalidId, new ExpectedSemanticInput(invalidId, SemanticInputKind.BuildInput, true, Path: path));
                outcomes[invalidId] = new SemanticInputOutcome(invalidId, SemanticInputState.Failed, Detail: exception.Message);
                return;
            }

            var id = $"build:{fullPath}";
            if (inputs.ContainsKey(id))
                return;
            try
            {
                var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
                var fingerprint = Convert.ToHexString(SHA256.HashData(bytes));
                inputs[id] = new ExpectedSemanticInput(id, SemanticInputKind.BuildInput, true,
                    Path: fullPath, Fingerprint: fingerprint);
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Resolved, fingerprint);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                inputs[id] = new ExpectedSemanticInput(id, SemanticInputKind.BuildInput, true, Path: fullPath);
                outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Failed, Detail: exception.Message);
            }
        }

        async Task AddObservedDocumentAsync(Document document, SemanticInputKind kind, RoslynProject project, string context)
        {
            var path = document.FilePath ?? document.Name;
            var id = $"{kind}:{context}:{path}";
            var text = await document.GetTextAsync(cancellationToken);
            var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
            inputs[id] = new ExpectedSemanticInput(id, kind, true,
                ProjectPath: WorkspaceCompleteness.ProjectIdentity(project), Context: context, Path: path,
                Fingerprint: fingerprint);
            outcomes[id] = new SemanticInputOutcome(id, SemanticInputState.Loaded, fingerprint);
        }
    }

    private static Dictionary<string, string> ToDictionary(IReadOnlyDictionary<string, string> source)
        => source.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static ImmutableArray<string> GetTargetFrameworks(BuildProject project)
    {
        var targetFrameworks = project.GetPropertyValue("TargetFrameworks");
        if (string.IsNullOrWhiteSpace(targetFrameworks))
            targetFrameworks = project.GetPropertyValue("TargetFramework");
        return targetFrameworks.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToImmutableArray();
    }

    private static string ContextKey(string projectPath, string? targetFramework)
        => $"context:{Path.GetFullPath(projectPath)}:{DisplayFramework(targetFramework)}";

    private static string DisplayFramework(string? targetFramework)
        => string.IsNullOrWhiteSpace(targetFramework) ? "default" : targetFramework;

    private static string ItemPath(ProjectItem item)
    {
        var fullPath = item.GetMetadataValue("FullPath");
        if (!string.IsNullOrWhiteSpace(fullPath))
            return Path.GetFullPath(fullPath);
        return Path.GetFullPath(item.EvaluatedInclude, Path.GetDirectoryName(item.Project.FullPath)!);
    }

    private static WorkspaceAcquisitionDiagnostic Failure(string id, string message,
        string? inputId = null, string? projectPath = null)
        => new(id, DiagnosticSeverity.Error, CompletenessDiagnosticImpact.RequiredInputIncomplete,
            "msbuild-evaluation", message, inputId, projectPath);

    private static SemanticInputOutcome MergeCollectorOutcome(
        SemanticInputOutcome collected,
        SemanticInputOutcome observed)
    {
        if (collected.State is SemanticInputState.Failed or SemanticInputState.PolicyBlocked or SemanticInputState.Unknown)
            return collected;
        return observed;
    }
}
