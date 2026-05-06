using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed class MigrateToIOptionsService
{
    private static readonly Regex GetValueRegex = new(
        @"GetValue\s*(?:<(?<type>[^>]+)>)?\s*\(\s*""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    public static async Task<int> RunAsync(Solution solution, string section, string name, bool dryRun)
    {
        var candidates = await FindRegistrationSitesAsync(solution);
        if (candidates.Count == 0)
        {
            RtLog.Error("No DI registration site found. Add a services.Configure entry-point manually or pass a solution containing IServiceCollection configuration.");
            return 1;
        }

        var target = candidates
            .OrderBy(c => c.FilePath, StringComparer.InvariantCulture)
            .ThenBy(c => c.Line)
            .First();

        if (candidates.Count > 1)
        {
            RtLog.Warning("Multiple DI registration sites found; using first candidate.");
            foreach (var candidate in candidates.OrderBy(c => c.FilePath, StringComparer.InvariantCulture).ThenBy(c => c.Line))
                RtLog.Warning("  {FilePath}:{Line}", candidate.FilePath, candidate.Line);
        }

        var targetNamespace = await InferNamespaceAsync(target.Document);
        var namespaceName = targetNamespace
                            ?? target.Project.AssemblyName
                            ?? target.Project.Name;
        var outputPath = Path.Combine(Path.GetDirectoryName(target.Project.FilePath!)!, name + ".cs");
        var pocoText = BuildPoco(namespaceName, name, await InferPropertiesAsync(solution, section));

        if (File.Exists(outputPath) && await File.ReadAllTextAsync(outputPath) != pocoText)
        {
            RtLog.Error("Type name collision: {Name} already exists at {FilePath}.", name, outputPath);
            return 1;
        }

        var changedFiles = new SortedSet<string>(StringComparer.InvariantCulture);
        var newSolution = solution;
        var namespaceImport = $"using {namespaceName};";
        var needsNamespaceImport = targetNamespace is null
                                   && !target.Text.Contains(namespaceImport, StringComparison.Ordinal);

        var alreadyApplied = File.Exists(outputPath)
                             && await File.ReadAllTextAsync(outputPath) == pocoText
                             && target.Text.Contains($"Configure<{name}>", StringComparison.Ordinal)
                             && !needsNamespaceImport;

        if (alreadyApplied)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"Migration already applied for {name}.");
            return 0;
        }

        if (!File.Exists(outputPath))
        {
            var docId = DocumentId.CreateNewId(target.Project.Id);
            newSolution = newSolution.AddDocument(
                docId,
                name + ".cs",
                SourceText.From(pocoText, Encoding.UTF8),
                filePath: outputPath);
            changedFiles.Add(outputPath);
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
                updatedText = InsertRegistration(updatedText, name, section, target.ServicesName, target.ConfigurationName, insertOffset);

            newSolution = newSolution.WithDocumentText(target.Document.Id, SourceText.From(updatedText, Encoding.UTF8));
            changedFiles.Add(target.FilePath);
        }

        if (dryRun)
            RtLog.Info("dry-run mode: no files will be written.");

        await SolutionWriter.ApplyChangesAsync(solution, newSolution, dryRun);

        RtConsole.WriteLine(RtOutputContext.Result, "Changed files:");
        foreach (var file in changedFiles)
            RtConsole.WriteLine(RtOutputContext.Result, file);

        var worklist = await FindConsumersAsync(solution, section);
        RtConsole.WriteLine(RtOutputContext.Result, "Read-site worklist:");
        foreach (var file in worklist)
            RtConsole.WriteLine(RtOutputContext.Result, file);

        return 0;
    }

    private static async Task<List<RegistrationSite>> FindRegistrationSitesAsync(Solution solution)
    {
        var sites = new List<RegistrationSite>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var text = document.FilePath is not null && File.Exists(document.FilePath)
                    ? await File.ReadAllTextAsync(document.FilePath)
                    : (await document.GetTextAsync()).ToString();
                var match = Regex.Match(
                    text,
                    @"(?<services>[A-Za-z_][A-Za-z0-9_]*(?:\s*\.\s*[A-Za-z_][A-Za-z0-9_]*)*)\s*\.\s*(?:Add\w+|Configure)\s*\(");
                if (!match.Success)
                    continue;

                var configurationName = Regex.Match(text, @"\bIConfiguration\s+(?<name>\w+)\b").Groups["name"].Value;
                if (string.IsNullOrEmpty(configurationName))
                    configurationName = Regex.Match(text, @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*\s*\.\s*Configuration)\b").Groups["name"].Value;
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

                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
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
        }

        return sites;
    }

    private static async Task<string?> InferNamespaceAsync(Document document)
    {
        var text = (await document.GetTextAsync()).ToString();
        var match = Regex.Match(text, @"namespace\s+(?<namespace>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]");
        return match.Success ? match.Groups["namespace"].Value : null;
    }

    private static async Task<IReadOnlyList<OptionProperty>> InferPropertiesAsync(Solution solution, string section)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var text = (await document.GetTextAsync()).ToString();
                foreach (Match match in GetValueRegex.Matches(text))
                {
                    var key = match.Groups["key"].Value;
                    if (!key.StartsWith(section + ":", StringComparison.Ordinal))
                        continue;

                    var leaf = key[(section.Length + 1)..].Split(':', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
                    if (string.IsNullOrWhiteSpace(leaf))
                        continue;

                    properties[ToPascalCase(leaf)] = NormalizeType(match.Groups["type"].Value);
                }
            }
        }

        foreach (var project in solution.Projects)
        {
            var projectDir = Path.GetDirectoryName(project.FilePath);
            if (string.IsNullOrEmpty(projectDir))
                continue;

            foreach (var appsettings in Directory.EnumerateFiles(projectDir, "appsettings*.json", SearchOption.TopDirectoryOnly))
                AddJsonProperties(properties, section, appsettings);
        }

        if (properties.Count == 0)
            properties["Value"] = "string";

        return properties
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new OptionProperty(kv.Key, kv.Value))
            .ToList();
    }

    private static void AddJsonProperties(Dictionary<string, string> properties, string section, string appsettings)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(appsettings));
        var current = document.RootElement;
        foreach (var part in section.Split(':'))
        {
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out current))
                return;
        }

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
            builder.AppendLine(CultureInfo.InvariantCulture, $"    public {property.Type} {property.Name} {{ get; init; }}{initializer}");
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
        while (nextNonWhitespace < text.Length && char.IsWhiteSpace(text[nextNonWhitespace]) && text[nextNonWhitespace] != '\n' && text[nextNonWhitespace] != '\r')
            nextNonWhitespace++;

        var indent = "        ";
        if (indentStart >= 0)
        {
            var lineStart = indentStart + 1;
            var leading = text[lineStart..Math.Min(nextNonWhitespace, text.Length)];
            if (!string.IsNullOrWhiteSpace(leading))
                indent = Regex.Match(leading, @"^\s*").Value;
        }

        var registration = $"{newline}{indent}{servicesName}.Configure<{name}>({configurationName}.GetSection(\"{section}\"));";
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

    private static async Task<IReadOnlyList<string>> FindConsumersAsync(Solution solution, string section)
    {
        var files = new SortedSet<string>(StringComparer.InvariantCulture);
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var text = (await document.GetTextAsync()).ToString();
                if (text.Contains($"\"{section}:", StringComparison.Ordinal)
                    || text.Contains($"GetSection(\"{section}\")", StringComparison.Ordinal))
                {
                    files.Add(document.FilePath!);
                }
            }
        }

        return files.ToList();
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
            _ => "string",
        };
    }

    private static string NormalizeExpression(string expression) =>
        Regex.Replace(expression, @"\s+", "");

    private static string InferJsonType(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out _) => "int",
            JsonValueKind.Number when value.TryGetInt64(out _) => "long",
            JsonValueKind.Number => "double",
            JsonValueKind.True or JsonValueKind.False => "bool",
            _ => "string",
        };

    private static string ToPascalCase(string value)
    {
        var parts = Regex.Split(value, @"[^A-Za-z0-9]+").Where(p => p.Length > 0).ToArray();
        if (parts.Length == 0)
            return "Value";

        return string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
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
}
