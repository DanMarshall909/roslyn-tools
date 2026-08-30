using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares IOptions migrations without rendering or writing changes.</summary>
internal sealed class MigrateToIOptionsService : IRtService
{
    private static readonly Regex GetValueRegex = new(
        @"GetValue\s*(?:<(?<type>[^>]+)>)?\s*\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    internal sealed record Preparation(
        Solution Solution,
        MigrateToIOptionsResult Result,
        IReadOnlyList<MutationDiagnostic> Diagnostics,
        bool Changed);

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "The service is deliberately instance-shaped for the application operation seam.")]
    internal async Task<Result<Preparation, RtError>> PrepareAsync(
        Solution solution,
        string section,
        string name,
        CancellationToken cancellationToken = default)
    {
        var candidates = await FindRegistrationSitesAsync(solution, cancellationToken);
        if (candidates.Count == 0)
            return Result<Preparation, RtError>.Err(RtError.NotFound(
                "No DI registration site found. Add a services.Configure entry-point manually or pass a solution containing IServiceCollection configuration."));

        var orderedCandidates = candidates
            .OrderBy(candidate => candidate.FilePath, StringComparer.InvariantCulture)
            .ThenBy(candidate => candidate.Line)
            .ToArray();
        var target = orderedCandidates[0];
        var diagnostics = CreateRegistrationDiagnostics(orderedCandidates);

        cancellationToken.ThrowIfCancellationRequested();
        var targetNamespace = await InferNamespaceAsync(target.Document, cancellationToken);
        var namespaceName = targetNamespace
                            ?? target.Project.AssemblyName
                            ?? target.Project.Name;
        var outputPath = Path.Combine(Path.GetDirectoryName(target.Project.FilePath!)!, name + ".cs");
        var discovered = await InferPropertiesAsync(solution, section, cancellationToken);
        var pocoText = BuildPoco(namespaceName, name, discovered.Properties);

        if (File.Exists(outputPath) && await File.ReadAllTextAsync(outputPath, cancellationToken) != pocoText)
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Type name collision: {name} already exists at {outputPath}.",
                new Dictionary<string, string>
                {
                    ["name"] = name,
                    ["filePath"] = outputPath
                }));

        cancellationToken.ThrowIfCancellationRequested();
        var namespaceImport = $"using {namespaceName};";
        var needsNamespaceImport = targetNamespace is null
                                   && !target.Text.Contains(namespaceImport, StringComparison.Ordinal);
        var existingPocoMatches = File.Exists(outputPath)
                                  && await File.ReadAllTextAsync(outputPath, cancellationToken) == pocoText;
        var alreadyApplied = existingPocoMatches
                             && target.Text.Contains($"Configure<{name}>", StringComparison.Ordinal)
                             && !needsNamespaceImport;
        var worklist = await FindConsumersAsync(solution, section, cancellationToken);
        var registration = new MigrateToIOptionsRegistrationSite(
            target.FilePath,
            target.Line,
            target.ServicesName,
            target.ConfigurationName);
        var result = new MigrateToIOptionsResult(
            section,
            name,
            outputPath,
            registration,
            discovered.ConfigSources,
            worklist,
            alreadyApplied);

        if (alreadyApplied)
        {
            diagnostics.Add(new MutationDiagnostic(
                MutationDiagnosticSeverity.Information,
                $"Migration already applied for {name}."));
            return Result<Preparation, RtError>.Ok(new Preparation(solution, result, diagnostics, false));
        }

        var newSolution = solution;
        if (!File.Exists(outputPath))
        {
            var documentId = DocumentId.CreateNewId(target.Project.Id);
            newSolution = newSolution.AddDocument(
                documentId,
                name + ".cs",
                SourceText.From(pocoText, Encoding.UTF8),
                filePath: outputPath);
        }

        if (!target.Text.Contains($"Configure<{name}>", StringComparison.Ordinal) || needsNamespaceImport)
        {
            var updatedText = target.Text;
            var insertOffset = target.InsertOffset;
            if (needsNamespaceImport)
            {
                var beforeImport = updatedText.Length;
                updatedText = InsertNamespaceImport(updatedText, namespaceImport);
                insertOffset += updatedText.Length - beforeImport;
            }

            if (!updatedText.Contains($"Configure<{name}>", StringComparison.Ordinal))
                updatedText = InsertRegistration(
                    updatedText,
                    name,
                    section,
                    target.ServicesName,
                    target.ConfigurationName,
                    insertOffset);

            newSolution = newSolution.WithDocumentText(target.Document.Id, SourceText.From(updatedText, Encoding.UTF8));
        }

        return Result<Preparation, RtError>.Ok(new Preparation(newSolution, result, diagnostics, true));
    }

    private static List<MutationDiagnostic> CreateRegistrationDiagnostics(
        IReadOnlyList<RegistrationSite> candidates)
    {
        var diagnostics = new List<MutationDiagnostic>();
        if (candidates.Count <= 1)
            return diagnostics;

        diagnostics.Add(new MutationDiagnostic(
            MutationDiagnosticSeverity.Warning,
            "Multiple DI registration sites found; using first candidate."));
        diagnostics.AddRange(candidates.Select(candidate => new MutationDiagnostic(
            MutationDiagnosticSeverity.Warning,
            $"  {candidate.FilePath}:{candidate.Line}")));
        return diagnostics;
    }

    private static async Task<List<RegistrationSite>> FindRegistrationSitesAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        var sites = new List<RegistrationSite>();
        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = document.FilePath is not null && File.Exists(document.FilePath)
                    ? await File.ReadAllTextAsync(document.FilePath, cancellationToken)
                    : (await document.GetTextAsync(cancellationToken)).ToString();
                var match = Regex.Match(
                    text,
                    @"(?<services>[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*)\s*\.\s*(?:Add\w+|Configure)\s*\(");
                if (!match.Success)
                    continue;

                var configurationName = Regex.Match(text, @"\bIConfiguration\s+(?<name>\w+)\b").Groups["name"].Value;
                if (string.IsNullOrEmpty(configurationName))
                    configurationName = Regex.Match(text, @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*\s*\.\s*Configuration)\b")
                        .Groups["name"].Value;
                if (string.IsNullOrEmpty(configurationName))
                {
                    var servicesExpression = NormalizeExpression(match.Groups["services"].Value);
                    configurationName = servicesExpression.EndsWith(".Services", StringComparison.Ordinal)
                        ? servicesExpression[..^".Services".Length] + ".Configuration"
                        : "configuration";
                }

                var insertOffset = text.IndexOf(';', match.Index);
                if (insertOffset < 0)
                    continue;

                var line = text.Take(match.Index).Count(character => character == '\n') + 1;
                sites.Add(new RegistrationSite(
                    project,
                    document,
                    document.FilePath!,
                    text,
                    NormalizeExpression(match.Groups["services"].Value),
                    NormalizeExpression(configurationName),
                    insertOffset + 1,
                    line));
            }

        return sites;
    }

    private static async Task<string?> InferNamespaceAsync(Document document, CancellationToken cancellationToken)
    {
        var text = (await document.GetTextAsync(cancellationToken)).ToString();
        var match = Regex.Match(text, @"namespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]");
        return match.Success ? match.Groups["namespace"].Value : null;
    }

    private static async Task<DiscoveredProperties> InferPropertiesAsync(
        Solution solution,
        string section,
        CancellationToken cancellationToken)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = (await document.GetTextAsync(cancellationToken)).ToString();
                foreach (Match match in GetValueRegex.Matches(text))
                {
                    var key = match.Groups["key"].Value;
                    if (!key.StartsWith(section + ":", StringComparison.Ordinal))
                        continue;

                    var leaf = key[(section.Length + 1)..].Split(':', StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault();
                    if (!string.IsNullOrWhiteSpace(leaf))
                        properties[ToPascalCase(leaf)] = NormalizeType(match.Groups["type"].Value);
                }
            }

        var configSources = new SortedSet<string>(StringComparer.InvariantCulture);
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var projectDir = Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDir))
                continue;

            foreach (var appsettings in Directory.EnumerateFiles(projectDir, "appsettings*.json",
                         SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.InvariantCulture))
            {
                cancellationToken.ThrowIfCancellationRequested();
                configSources.Add(appsettings);
                await AddJsonPropertiesAsync(properties, section, appsettings, cancellationToken);
            }
        }

        if (properties.Count == 0)
            properties["Value"] = "string";

        return new DiscoveredProperties(
            properties
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new OptionProperty(pair.Key, pair.Value))
                .ToArray(),
            configSources.ToArray());
    }

    private static async Task AddJsonPropertiesAsync(
        Dictionary<string, string> properties,
        string section,
        string appsettings,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(appsettings);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var current = document.RootElement;
        foreach (var part in section.Split(':'))
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return;

        if (current.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in current.EnumerateObject())
            properties.TryAdd(ToPascalCase(property.Name), InferJsonType(property.Value));
    }

    private static string BuildPoco(string namespaceName, string typeName, IReadOnlyList<OptionProperty> properties)
    {
        var builder = new StringBuilder();
        builder.AppendLine(CultureInfo.InvariantCulture, $"namespace {namespaceName};");
        builder.AppendLine();
        builder.AppendLine(CultureInfo.InvariantCulture, $"internal sealed class {typeName}");
        builder.AppendLine("{");
        foreach (var property in properties)
        {
            var initializer = property.Type == "string" ? " = string.Empty;" : "";
            builder.AppendLine(CultureInfo.InvariantCulture,
                $"    public {property.Type} {property.Name} {{ get; init; }}{initializer}");
        }

        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string InsertRegistration(
        string text,
        string name,
        string section,
        string servicesName,
        string configurationName,
        int insertOffset)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var indentStart = text.LastIndexOf('\n', Math.Max(0, insertOffset - 1));
        var nextNonWhitespace = insertOffset;
        while (nextNonWhitespace < text.Length && char.IsWhiteSpace(text[nextNonWhitespace])
               && text[nextNonWhitespace] != '\n' && text[nextNonWhitespace] != '\r')
            nextNonWhitespace++;

        var indent = "        ";
        if (indentStart >= 0)
        {
            var lineStart = indentStart + 1;
            var leading = text[lineStart..Math.Min(nextNonWhitespace, text.Length)];
            if (!string.IsNullOrWhiteSpace(leading))
                indent = Regex.Match(leading, @"^\s*").Value;
        }

        var registration =
            $"{newline}{indent}{servicesName}.Configure<{name}>({configurationName}.GetSection(\"{section}\"));";
        return text.Insert(insertOffset, registration);
    }

    private static string InsertNamespaceImport(string text, string namespaceImport)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var matches = Regex.Matches(text, @"^using\s+[^\r\n]+;\s*$", RegexOptions.Multiline);
        if (matches.Count == 0)
            return namespaceImport + newline + newline + text;

        var last = matches[^1];
        return text.Insert(last.Index + last.Length, newline + namespaceImport);
    }

    private static async Task<IReadOnlyList<string>> FindConsumersAsync(
        Solution solution,
        string section,
        CancellationToken cancellationToken)
    {
        var files = new SortedSet<string>(StringComparer.InvariantCulture);
        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = (await document.GetTextAsync(cancellationToken)).ToString();
                if (text.Contains($"\"{section}:", StringComparison.Ordinal)
                    || text.Contains($"GetSection(\"{section}\")", StringComparison.Ordinal))
                    files.Add(document.FilePath!);
            }

        return files.ToArray();
    }

    private static string NormalizeType(string type)
    {
        var trimmed = type.Trim();
        return trimmed switch
        {
            "int" or "Int32" => "int",
            "long" or "Int64" => "long",
            "bool" or "Boolean" => "bool",
            "double" or "Double" => "double",
            "decimal" or "Decimal" => "decimal",
            _ => "string"
        };
    }

    private static string NormalizeExpression(string expression) => Regex.Replace(expression, @"\s+", "");

    private static string InferJsonType(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out _) => "int",
        JsonValueKind.Number when value.TryGetInt64(out _) => "long",
        JsonValueKind.Number => "double",
        JsonValueKind.True or JsonValueKind.False => "bool",
        _ => "string"
    };

    private static string ToPascalCase(string value)
    {
        var parts = Regex.Split(value, @"[^A-Za-z0-9]+").Where(part => part.Length > 0).ToArray();
        return parts.Length == 0
            ? "Value"
            : string.Concat(parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private sealed record RegistrationSite(
        Project Project,
        Document Document,
        string FilePath,
        string Text,
        string ServicesName,
        string ConfigurationName,
        int InsertOffset,
        int Line);

    private sealed record OptionProperty(string Name, string Type);

    private sealed record DiscoveredProperties(
        IReadOnlyList<OptionProperty> Properties,
        IReadOnlyList<string> ConfigSources);
}
