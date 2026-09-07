using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal static class ArchitectureCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    internal static async Task<Result<ArchitectureCheckResult, RtError>> CheckAsync(
        Solution solution,
        ArchitectureCheckRequest request,
        CancellationToken cancellationToken)
    {
        var repositoryRoot = ResolveRepositoryRoot(solution, request.Solution);
        var configPath = ResolveContainedPath(repositoryRoot, request.Config, "configuration");
        if (!File.Exists(configPath))
            return Result<ArchitectureCheckResult, RtError>.Err(RtError.NotFound(
                $"Architecture configuration was not found: '{configPath}'.",
                new Dictionary<string, string> { ["config"] = configPath }));

        ArchitectureConfiguration configuration;
        try
        {
            await using var stream = File.OpenRead(configPath);
            configuration = await JsonSerializer.DeserializeAsync<ArchitectureConfiguration>(
                stream,
                JsonOptions,
                cancellationToken) ?? throw new InvalidOperationException(
                $"Architecture configuration '{configPath}' is empty.");
        }
        catch (JsonException ex)
        {
            return Result<ArchitectureCheckResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Architecture configuration '{configPath}' is invalid JSON: {ex.Message}",
                new Dictionary<string, string> { ["config"] = configPath }));
        }

        var validation = ValidateAndMap(configuration, solution);
        if (validation.IsError)
            return Result<ArchitectureCheckResult, RtError>.Err(validation.Error!);

        var mapping = validation.Value!;
        var baselinePathValue = request.Baseline ?? configuration.BaselineFile;
        string? baselinePath = null;
        var baselineFingerprints = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(baselinePathValue))
        {
            baselinePath = ResolveContainedPath(repositoryRoot, baselinePathValue, "baseline");
            var baselineResult = await LoadBaselineAsync(baselinePath, cancellationToken);
            if (!TryGet(baselineResult, out var loadedBaseline, out var baselineError))
                return Result<ArchitectureCheckResult, RtError>.Err(baselineError!);
            baselineFingerprints = loadedBaseline!;
        }

        var candidates = new List<ViolationCandidate>();
        foreach (var entry in mapping.Projects.OrderBy(entry => entry.Project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectResult = await AnalyzeProjectAsync(
                entry.Project,
                entry.Layer,
                mapping,
                configuration.TechnologyRules,
                repositoryRoot,
                cancellationToken);
            if (!TryGet(projectResult, out var projectCandidates, out var projectError))
                return Result<ArchitectureCheckResult, RtError>.Err(projectError!);
            candidates.AddRange(projectCandidates!);
        }

        var violations = candidates
            .DistinctBy(candidate => candidate.Identity, StringComparer.Ordinal)
            .Select(candidate => ToViolation(candidate, baselineFingerprints))
            .OrderBy(violation => violation.Project, StringComparer.Ordinal)
            .ThenBy(violation => violation.File, StringComparer.Ordinal)
            .ThenBy(violation => violation.Line)
            .ThenBy(violation => violation.Rule, StringComparer.Ordinal)
            .ThenBy(violation => violation.Dependency, StringComparer.Ordinal)
            .ToList();
        var newCount = violations.Count(violation => violation.BaselineState == "new");

        return Result<ArchitectureCheckResult, RtError>.Ok(new ArchitectureCheckResult(
            ToRelativePath(repositoryRoot, configPath),
            baselinePath is null ? null : ToRelativePath(repositoryRoot, baselinePath),
            mapping.Projects.Count,
            violations.Count == 0,
            violations.Count,
            newCount,
            violations.Count - newCount,
            violations));
    }

    private static ValidationResult ValidateAndMap(
        ArchitectureConfiguration configuration,
        Solution solution)
    {
        if (configuration.Version != 1)
            return ValidationResult.Fail(
                $"Architecture configuration version must be 1; found {configuration.Version}.");
        if (configuration.Layers.Count == 0)
            return ValidationResult.Fail("Architecture configuration must define at least one layer.");

        var duplicateLayer = configuration.Layers
            .GroupBy(layer => layer.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => string.IsNullOrWhiteSpace(group.Key) || group.Count() > 1);
        if (duplicateLayer is not null)
            return ValidationResult.Fail(string.IsNullOrWhiteSpace(duplicateLayer.Key)
                ? "Every architecture layer requires a non-empty name."
                : $"Architecture layer name '{duplicateLayer.Key}' is duplicated.");

        var layersByName = configuration.Layers.ToDictionary(
            layer => layer.Name,
            StringComparer.OrdinalIgnoreCase);
        foreach (var layer in configuration.Layers)
        {
            if (layer.Projects.Count == 0)
                return ValidationResult.Fail($"Architecture layer '{layer.Name}' must select at least one project.");
            var unknownAllowedLayer = layer.AllowedProjectReferences
                .FirstOrDefault(name => !layersByName.ContainsKey(name));
            if (unknownAllowedLayer is not null)
                return ValidationResult.Fail(
                    $"Architecture layer '{layer.Name}' allows unknown layer '{unknownAllowedLayer}'.");
        }

        foreach (var technology in configuration.TechnologyRules)
        {
            if (string.IsNullOrWhiteSpace(technology.Name))
                return ValidationResult.Fail("Every technology rule requires a non-empty name.");
            var unknownLayer = technology.ForbiddenInLayers
                .FirstOrDefault(name => !layersByName.ContainsKey(name));
            if (unknownLayer is not null)
                return ValidationResult.Fail(
                    $"Technology rule '{technology.Name}' names unknown layer '{unknownLayer}'.");
        }

        var entries = new List<ProjectLayerEntry>();
        foreach (var project in solution.Projects)
        {
            var ignored = configuration.IgnoredProjects.Any(pattern => MatchesPattern(project.Name, pattern));
            var matchingLayers = configuration.Layers
                .Where(layer => layer.Projects.Any(pattern => MatchesPattern(project.Name, pattern)))
                .ToList();
            if (ignored && matchingLayers.Count > 0)
                return ValidationResult.Fail(
                    $"Project '{project.Name}' is both ignored and assigned to layer '{matchingLayers[0].Name}'.");
            if (ignored)
                continue;
            if (matchingLayers.Count > 1)
                return ValidationResult.Fail(
                    $"Project '{project.Name}' is assigned to multiple layers: "
                    + string.Join(", ", matchingLayers.Select(layer => layer.Name)) + ".");
            if (matchingLayers.Count == 1)
                entries.Add(new ProjectLayerEntry(project, matchingLayers[0]));
            else
                return ValidationResult.Fail(
                    $"Loaded project '{project.Name}' is not assigned to a layer. "
                    + "Assign it or list it in ignoredProjects explicitly.");
        }

        foreach (var layer in configuration.Layers)
            foreach (var pattern in layer.Projects)
                if (!solution.Projects.Any(project => MatchesPattern(project.Name, pattern)))
                    return ValidationResult.Fail(
                        $"Architecture layer '{layer.Name}' project pattern '{pattern}' matched no loaded project.");
        foreach (var pattern in configuration.IgnoredProjects)
            if (!solution.Projects.Any(project => MatchesPattern(project.Name, pattern)))
                return ValidationResult.Fail(
                    $"Ignored project pattern '{pattern}' matched no loaded project.");

        var projectsById = entries.ToDictionary(entry => entry.Project.Id);
        var projectsByAssembly = entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Project.AssemblyName))
            .GroupBy(entry => entry.Project.AssemblyName!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        return ValidationResult.Success(new ArchitectureMapping(entries, projectsById, projectsByAssembly));
    }

    private static async Task<Result<List<ViolationCandidate>, RtError>> AnalyzeProjectAsync(
        Project project,
        ArchitectureLayerRule layer,
        ArchitectureMapping mapping,
        IReadOnlyList<ArchitectureTechnologyRule> technologyRules,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(project.FilePath) || !File.Exists(project.FilePath))
            return Result<List<ViolationCandidate>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Configured project '{project.Name}' has no readable project file; architecture verification is incomplete.",
                new Dictionary<string, string> { ["project"] = project.Name }));

        var compilation = await project.GetCompilationAsync(cancellationToken);
        if (compilation is null)
            return Result<List<ViolationCandidate>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Configured project '{project.Name}' produced no compilation; architecture verification is incomplete."));
        var errors = compilation.GetDiagnostics(cancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(5)
            .ToList();
        if (errors.Count > 0)
            return Result<List<ViolationCandidate>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Configured project '{project.Name}' has compilation errors; architecture verification stopped: "
                + string.Join(" | ", errors.Select(error => error.ToString())),
                new Dictionary<string, string>
                {
                    ["project"] = project.Name,
                    ["diagnosticCount"] = errors.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));

        var candidates = AnalyzeProjectFile(project, layer, mapping, technologyRules, repositoryRoot);
        foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            var model = await document.GetSemanticModelAsync(cancellationToken);
            var text = await document.GetTextAsync(cancellationToken);
            if (root is null || model is null)
                return Result<List<ViolationCandidate>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    $"Document '{document.FilePath ?? document.Name}' in project '{project.Name}' has no semantic model."));

            foreach (var name in root.DescendantNodes().OfType<NameSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbolInfo = model.GetSymbolInfo(name, cancellationToken);
                if (symbolInfo.Symbol is null && symbolInfo.CandidateReason != CandidateReason.None)
                    return Result<List<ViolationCandidate>, RtError>.Err(new RtError(
                        RtErrorKind.InvalidInput,
                        $"Dependency binding is unresolved in '{document.FilePath ?? document.Name}' at "
                        + $"line {name.GetLocation().GetLineSpan().StartLinePosition.Line + 1}: '{name}'."));

                var symbol = NormalizeDependencySymbol(symbolInfo.Symbol)
                             ?? NormalizeDependencySymbol(model.GetTypeInfo(name, cancellationToken).Type);
                if (symbol is null)
                    continue;
                AddSemanticViolations(
                    candidates,
                    project,
                    layer,
                    mapping,
                    technologyRules,
                    repositoryRoot,
                    document,
                    text,
                    name,
                    symbol);
            }
        }

        return Result<List<ViolationCandidate>, RtError>.Ok(candidates);
    }

    private static List<ViolationCandidate> AnalyzeProjectFile(
        Project project,
        ArchitectureLayerRule layer,
        ArchitectureMapping mapping,
        IReadOnlyList<ArchitectureTechnologyRule> technologyRules,
        string repositoryRoot)
    {
        var candidates = new List<ViolationCandidate>();
        var projectPath = project.FilePath!;
        var document = XDocument.Load(projectPath, LoadOptions.SetLineInfo | LoadOptions.PreserveWhitespace);
        var relativePath = ToRelativePath(repositoryRoot, projectPath);

        foreach (var reference in project.ProjectReferences)
        {
            var targetProject = project.Solution.GetProject(reference.ProjectId);
            if (targetProject is null)
                continue;
            var element = FindReferenceElement(document, "ProjectReference", targetProject.Name);
            var line = GetLine(element);
            var source = element?.ToString(SaveOptions.DisableFormatting) ?? $"ProjectReference: {targetProject.Name}";
            if (!mapping.ProjectsById.TryGetValue(targetProject.Id, out var targetEntry))
            {
                candidates.Add(new ViolationCandidate(
                    "unassigned-project-reference", layer.Name, project.Name, relativePath, line,
                    targetProject.Name, source));
                continue;
            }

            if (!IsProjectDependencyAllowed(layer, targetEntry))
                candidates.Add(new ViolationCandidate(
                    "layer-project-reference", layer.Name, project.Name, relativePath, line,
                    targetProject.Name, source));
        }

        foreach (var sdk in EnumerateSdks(document))
        {
            AddPatternViolation(candidates, layer.ForbiddenSdks, "forbidden-sdk", layer, project,
                relativePath, sdk.Name, sdk.Element, "SDK");
            AddTechnologyProjectViolations(candidates, technologyRules, layer, project, relativePath,
                sdk.Name, sdk.Element, "SDK", static rule => rule.Sdks);
        }

        foreach (var package in document.Descendants().Where(element => element.Name.LocalName == "PackageReference"))
        {
            var name = package.Attribute("Include")?.Value ?? package.Attribute("Update")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            AddPatternViolation(candidates, layer.ForbiddenPackageReferences, "forbidden-package-reference",
                layer, project, relativePath, name, package, "PackageReference");
            AddTechnologyProjectViolations(candidates, technologyRules, layer, project, relativePath,
                name, package, "PackageReference", static rule => rule.PackagePrefixes);
        }

        foreach (var framework in document.Descendants().Where(element => element.Name.LocalName == "FrameworkReference"))
        {
            var name = framework.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            AddPatternViolation(candidates, layer.ForbiddenFrameworkReferences,
                "forbidden-framework-reference", layer, project, relativePath, name, framework,
                "FrameworkReference");
            AddTechnologyProjectViolations(candidates, technologyRules, layer, project, relativePath,
                name, framework, "FrameworkReference", static rule => rule.FrameworkReferences);
        }

        return candidates;
    }

    private static void AddSemanticViolations(
        ICollection<ViolationCandidate> candidates,
        Project project,
        ArchitectureLayerRule layer,
        ArchitectureMapping mapping,
        IReadOnlyList<ArchitectureTechnologyRule> technologyRules,
        string repositoryRoot,
        Document document,
        SourceText text,
        NameSyntax syntax,
        ISymbol symbol)
    {
        var type = symbol as INamedTypeSymbol ?? symbol.ContainingType;
        var namespaceName = type?.ContainingNamespace?.ToDisplayString()
                            ?? symbol.ContainingNamespace?.ToDisplayString()
                            ?? string.Empty;
        var typeName = type?.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        var dependency = typeName ?? namespaceName;
        if (string.IsNullOrWhiteSpace(dependency)
            || layer.DependencyExceptions.Any(pattern => MatchesPattern(dependency, pattern)))
            return;

        var location = syntax.GetLocation().GetLineSpan();
        var line = location.StartLinePosition.Line + 1;
        var file = document.FilePath is null
            ? document.Name
            : ToRelativePath(repositoryRoot, document.FilePath);
        var source = text.Lines[location.StartLinePosition.Line].ToString().Trim();

        var assemblyName = type?.ContainingAssembly?.Identity.Name
                           ?? symbol.ContainingAssembly?.Identity.Name;
        if (assemblyName is not null
            && mapping.ProjectsByAssembly.TryGetValue(assemblyName, out var target)
            && target.Project.Id != project.Id
            && !IsProjectDependencyAllowed(layer, target))
            candidates.Add(new ViolationCandidate(
                "layer-semantic-dependency", layer.Name, project.Name, file, line,
                dependency, source));

        if (layer.ForbiddenNamespacePrefixes.Any(pattern => MatchesPrefix(namespaceName, pattern)))
            candidates.Add(new ViolationCandidate(
                "forbidden-namespace", layer.Name, project.Name, file, line,
                namespaceName, source));
        if (typeName is not null && layer.ForbiddenTypeNames.Any(pattern => MatchesPattern(typeName, pattern)))
            candidates.Add(new ViolationCandidate(
                "forbidden-type", layer.Name, project.Name, file, line,
                typeName, source));

        foreach (var technology in technologyRules.Where(rule =>
                     rule.ForbiddenInLayers.Contains(layer.Name, StringComparer.OrdinalIgnoreCase)))
        {
            var namespaceMatch = technology.NamespacePrefixes.Any(pattern => MatchesPrefix(namespaceName, pattern));
            var typeMatch = typeName is not null
                            && technology.TypeNames.Any(pattern => MatchesPattern(typeName, pattern));
            if (namespaceMatch || typeMatch)
                candidates.Add(new ViolationCandidate(
                    $"technology:{technology.Name}", layer.Name, project.Name, file, line,
                    dependency, source));
        }
    }

    private static ISymbol? NormalizeDependencySymbol(ISymbol? symbol) => symbol switch
    {
        IAliasSymbol alias => NormalizeDependencySymbol(alias.Target),
        ITypeSymbol type => type,
        INamespaceSymbol ns => ns,
        IMethodSymbol method => method.ContainingType,
        IPropertySymbol property => property.ContainingType,
        IFieldSymbol field => field.ContainingType,
        IEventSymbol eventSymbol => eventSymbol.ContainingType,
        _ => (ISymbol?)symbol?.ContainingType ?? symbol?.ContainingNamespace
    };

    private static bool IsProjectDependencyAllowed(
        ArchitectureLayerRule layer,
        ProjectLayerEntry target) =>
        layer.ProjectReferenceExceptions.Any(pattern => MatchesPattern(target.Project.Name, pattern))
        || layer.AllowSameLayerReferences
        && string.Equals(layer.Name, target.Layer.Name, StringComparison.OrdinalIgnoreCase)
        || layer.AllowedProjectReferences.Contains(target.Layer.Name, StringComparer.OrdinalIgnoreCase);

    private static void AddPatternViolation(
        ICollection<ViolationCandidate> candidates,
        IReadOnlyList<string> patterns,
        string rule,
        ArchitectureLayerRule layer,
        Project project,
        string relativePath,
        string dependency,
        XElement? element,
        string dependencyKind)
    {
        if (!patterns.Any(pattern => MatchesPattern(dependency, pattern)))
            return;
        candidates.Add(new ViolationCandidate(
            rule, layer.Name, project.Name, relativePath, GetLine(element),
            $"{dependencyKind}: {dependency}",
            element?.ToString(SaveOptions.DisableFormatting) ?? dependency));
    }

    private static void AddTechnologyProjectViolations(
        ICollection<ViolationCandidate> candidates,
        IReadOnlyList<ArchitectureTechnologyRule> technologyRules,
        ArchitectureLayerRule layer,
        Project project,
        string relativePath,
        string dependency,
        XElement? element,
        string dependencyKind,
        Func<ArchitectureTechnologyRule, IReadOnlyList<string>> patterns)
    {
        foreach (var technology in technologyRules.Where(rule =>
                     rule.ForbiddenInLayers.Contains(layer.Name, StringComparer.OrdinalIgnoreCase)
                     && patterns(rule).Any(pattern => MatchesPattern(dependency, pattern))))
            candidates.Add(new ViolationCandidate(
                $"technology:{technology.Name}", layer.Name, project.Name, relativePath,
                GetLine(element), $"{dependencyKind}: {dependency}",
                element?.ToString(SaveOptions.DisableFormatting) ?? dependency));
    }

    private static List<(string Name, XElement? Element)> EnumerateSdks(XDocument document)
    {
        var result = new List<(string Name, XElement? Element)>();
        if (document.Root?.Attribute("Sdk")?.Value is { Length: > 0 } rootSdk)
            result.Add((rootSdk, document.Root));
        result.AddRange(document.Descendants()
            .Where(element => element.Name.LocalName == "Sdk")
            .Select(element => (element.Attribute("Name")?.Value, Element: (XElement?)element))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Item1))
            .Select(entry => (entry.Item1!, entry.Element)));
        return result;
    }

    private static XElement? FindReferenceElement(XDocument document, string elementName, string projectName) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == elementName)
            .FirstOrDefault(element =>
            {
                var include = element.Attribute("Include")?.Value;
                return include is not null
                       && string.Equals(
                           Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)),
                           projectName,
                           StringComparison.OrdinalIgnoreCase);
            });

    private static int GetLine(XElement? element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : 1;

    private static async Task<Result<HashSet<string>, RtError>> LoadBaselineAsync(
        string path,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return Result<HashSet<string>, RtError>.Err(RtError.NotFound(
                $"Architecture baseline was not found: '{path}'.",
                new Dictionary<string, string> { ["baseline"] = path }));
        try
        {
            await using var stream = File.OpenRead(path);
            var baseline = await JsonSerializer.DeserializeAsync<ArchitectureBaseline>(
                stream,
                JsonOptions,
                cancellationToken) ?? throw new InvalidOperationException(
                $"Architecture baseline '{path}' is empty.");
            if (baseline.Version != 1)
                return Result<HashSet<string>, RtError>.Err(new RtError(
                    RtErrorKind.InvalidInput,
                    $"Architecture baseline version must be 1; found {baseline.Version}."));
            return Result<HashSet<string>, RtError>.Ok(
                baseline.Fingerprints.ToHashSet(StringComparer.Ordinal));
        }
        catch (JsonException ex)
        {
            return Result<HashSet<string>, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Architecture baseline '{path}' is invalid JSON: {ex.Message}"));
        }
    }

    private static ArchitectureViolation ToViolation(
        ViolationCandidate candidate,
        HashSet<string> baselineFingerprints)
    {
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(candidate.Identity)))
            .ToLowerInvariant();
        return new ArchitectureViolation(
            fingerprint,
            candidate.Rule,
            candidate.Layer,
            candidate.Project,
            candidate.File,
            candidate.Line,
            candidate.Dependency,
            candidate.Source,
            baselineFingerprints.Contains(fingerprint) ? "existing" : "new");
    }

    private static string ResolveRepositoryRoot(Solution solution, string? requestedSolution)
    {
        var solutionPath = solution.FilePath;
        if (string.IsNullOrWhiteSpace(solutionPath))
            solutionPath = requestedSolution;
        if (string.IsNullOrWhiteSpace(solutionPath))
            return Path.GetFullPath(Directory.GetCurrentDirectory());
        var fullPath = Path.GetFullPath(solutionPath);
        return Path.GetDirectoryName(fullPath)
               ?? throw new InvalidOperationException($"Cannot determine repository root from '{fullPath}'.");
    }

    private static string ResolveContainedPath(string repositoryRoot, string value, string purpose)
    {
        var path = Path.GetFullPath(value, repositoryRoot);
        var relative = Path.GetRelativePath(repositoryRoot, path);
        if (relative == ".."
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathFullyQualified(relative))
            throw new InvalidOperationException(
                $"Architecture {purpose} path '{value}' is outside solution directory '{repositoryRoot}'.");
        return path;
    }

    private static string ToRelativePath(string root, string path) =>
        Path.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');

    private static bool MatchesPrefix(string value, string pattern)
    {
        var normalized = pattern.EndsWith('*') ? pattern[..^1] : pattern;
        return value.Equals(normalized, StringComparison.Ordinal)
               || value.StartsWith(normalized + ".", StringComparison.Ordinal)
               || pattern.EndsWith('*') && value.StartsWith(normalized, StringComparison.Ordinal);
    }

    private static bool MatchesPattern(string value, string pattern)
    {
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return string.Equals(value, pattern, StringComparison.OrdinalIgnoreCase);
        var expression = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool TryGet<T>(
        Result<T, RtError> result,
        out T? value,
        out RtError? error)
        where T : class
    {
        T? capturedValue = null;
        RtError? capturedError = null;
        result.Match(
            success => capturedValue = success,
            failure => capturedError = failure);
        value = capturedValue;
        error = capturedError;
        return result.IsOk;
    }

    private sealed record ViolationCandidate(
        string Rule,
        string Layer,
        string Project,
        string File,
        int Line,
        string Dependency,
        string Source)
    {
        internal string Identity => string.Join('|', Rule, Layer, Project, File, Line, Dependency);
    }

    private sealed record ProjectLayerEntry(Project Project, ArchitectureLayerRule Layer);

    private sealed record ArchitectureMapping(
        IReadOnlyList<ProjectLayerEntry> Projects,
        IReadOnlyDictionary<ProjectId, ProjectLayerEntry> ProjectsById,
        IReadOnlyDictionary<string, ProjectLayerEntry> ProjectsByAssembly);

    private sealed record ValidationResult(ArchitectureMapping? Value, RtError? Error)
    {
        internal bool IsError => Error is not null;

        internal static ValidationResult Success(ArchitectureMapping value) => new(value, null);

        internal static ValidationResult Fail(string message) => new(
            null,
            new RtError(RtErrorKind.InvalidInput, message));
    }
}

internal sealed class ArchitectureConfiguration
{
    public int Version { get; init; }
    public string? BaselineFile { get; init; }
    public List<string> IgnoredProjects { get; init; } = [];
    public List<ArchitectureLayerRule> Layers { get; init; } = [];
    public List<ArchitectureTechnologyRule> TechnologyRules { get; init; } = [];
}

internal sealed class ArchitectureLayerRule
{
    public string Name { get; init; } = string.Empty;
    public List<string> Projects { get; init; } = [];
    public List<string> AllowedProjectReferences { get; init; } = [];
    public bool AllowSameLayerReferences { get; init; }
    public List<string> ProjectReferenceExceptions { get; init; } = [];
    public List<string> DependencyExceptions { get; init; } = [];
    public List<string> ForbiddenNamespacePrefixes { get; init; } = [];
    public List<string> ForbiddenTypeNames { get; init; } = [];
    public List<string> ForbiddenSdks { get; init; } = [];
    public List<string> ForbiddenFrameworkReferences { get; init; } = [];
    public List<string> ForbiddenPackageReferences { get; init; } = [];
}

internal sealed class ArchitectureTechnologyRule
{
    public string Name { get; init; } = string.Empty;
    public List<string> ForbiddenInLayers { get; init; } = [];
    public List<string> NamespacePrefixes { get; init; } = [];
    public List<string> TypeNames { get; init; } = [];
    public List<string> PackagePrefixes { get; init; } = [];
    public List<string> FrameworkReferences { get; init; } = [];
    public List<string> Sdks { get; init; } = [];
}

internal sealed class ArchitectureBaseline
{
    public int Version { get; init; }
    public List<string> Fingerprints { get; init; } = [];
}
