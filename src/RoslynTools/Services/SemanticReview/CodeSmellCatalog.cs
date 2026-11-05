using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RoslynTools.Services.SemanticReview;

internal sealed record CodeSmellCatalog(JsonObject Document, string Hash)
{
    internal JsonArray Rules => Document["rules"]!.AsArray();
    internal string Version => Document["catalogVersion"]!.GetValue<string>();
    private const int MaximumBytes = 1_048_576;

    internal static JsonObject Bundled()
    {
        using var stream = typeof(CodeSmellCatalog).Assembly.GetManifestResourceStream("RoslynTools.code-smells.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    }

    internal static async Task<CodeSmellCatalog> ReadAsync(string repository, string? config, CancellationToken token)
    {
        var path = Path.GetFullPath(config ?? "code-smells.json", repository);
        byte[] bytes;
        if (config is null && !File.Exists(path))
            bytes = System.Text.Encoding.UTF8.GetBytes(Bundled().ToJsonString());
        else
        {
            await using var stream = File.OpenRead(path);
            if (stream.Length > MaximumBytes) throw new JsonException("Catalogue exceeds 1 MiB.");
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int count;
            while ((count = await stream.ReadAsync(chunk, token)) != 0)
            {
                if (buffer.Length + count > MaximumBytes) throw new JsonException("Catalogue exceeds 1 MiB.");
                buffer.Write(chunk, 0, count);
            }
            bytes = buffer.ToArray();
        }
        using var parsed = JsonDocument.Parse(bytes);
        CheckDuplicateKeys(parsed.RootElement);
        var document = JsonNode.Parse(bytes) as JsonObject ?? throw new JsonException("Catalogue must be an object.");
        var baseline = Bundled();
        Shape(document, baseline, "catalogue");
        if (document["schemaVersion"]!.GetValue<int>() != 1) throw new JsonException("Unsupported schemaVersion.");
        if (!JsonNode.DeepEquals(document["responseSchema"], baseline["responseSchema"]))
            throw new JsonException("responseSchema must match the runtime result contract.");
        var policy = document["reviewPolicy"]!;
        foreach (var key in new[] { "minimumAutomaticProbability", "maximumContradictoryProbability" })
        {
            var value = policy[key]!.GetValue<double>();
            if (!double.IsFinite(value) || value is < 0 or > 1) throw new JsonException($"Invalid policy {key}.");
        }
        if (policy["incompleteEvidenceClassification"]!.GetValue<string>() != "uncertain"
            || policy["noAcceptedExceptionId"]!.GetValue<string>() != "no_accepted_exception")
            throw new JsonException("Unsupported review policy vocabulary.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in document["rules"]!.AsArray())
        {
            var rule = item!;
            var id = rule["id"]!.GetValue<string>();
            if (!ids.Add(id)) throw new JsonException($"Duplicate rule '{id}'.");
            if (rule["severity"]!.GetValue<string>() is not ("info" or "warning" or "error"))
                throw new JsonException($"Rule '{id}': unsupported severity.");
            var detection = rule["detection"]!;
            var selector = detection["selector"]!.GetValue<string>();
            var kind = detection["kind"]!.GetValue<string>();
            string[] keys = (kind, selector) switch
            {
                ("roslyn", "LargeClass") => ["maxClassLines"],
                ("roslyn", "LongMethod") => ["maxMethodLines"],
                ("roslyn", "TooManyDependencies") => ["maxParams"],
                ("roslyn", "SRP") => ["minMethods", "minDeps"],
                ("roslyn", "ISP") => ["minMembers"],
                ("roslyn", "OCP") => ["minBranches"],
                ("roslyn", "DIP" or "LSP") => [],
                ("semantic", "class" or "method") => [],
                _ => throw new JsonException($"Rule '{id}': unsupported detector '{kind}/{selector}'.")
            };
            foreach (var threshold in detection["thresholds"]!.AsObject())
                if (!keys.Contains(threshold.Key) || threshold.Value is null
                    || !threshold.Value.AsValue().TryGetValue<int>(out var number) || number < 0)
                    throw new JsonException($"Rule '{id}': invalid threshold '{threshold.Key}'.");
            var exceptions = new HashSet<string>(StringComparer.Ordinal) { "no_accepted_exception" };
            foreach (var exception in rule["review"]!["acceptedExceptions"]!.AsArray())
                if (!exceptions.Add(exception!["id"]!.GetValue<string>()))
                    throw new JsonException($"Rule '{id}': duplicate or reserved exception ID.");
        }
        return new(document, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    private static void CheckDuplicateKeys(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException($"Duplicate JSON key '{property.Name}'.");
                CheckDuplicateKeys(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) CheckDuplicateKeys(child);
    }

    private static void Shape(JsonNode? value, JsonNode template, string path)
    {
        var booleanTemplate = template.GetValueKind() is JsonValueKind.True or JsonValueKind.False;
        var booleanValue = value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False;
        if (value is null || (value.GetValueKind() != template.GetValueKind() && !(booleanTemplate && booleanValue)))
            throw new JsonException($"{path}: required value has the wrong type.");
        if (path == "catalogue.responseSchema") return;
        if (value is JsonObject obj && template is JsonObject expected)
        {
            if (path.EndsWith(".thresholds", StringComparison.Ordinal)) return;
            foreach (var key in obj.Select(pair => pair.Key))
                if (!expected.ContainsKey(key)) throw new JsonException($"{path}: unknown member '{key}'.");
            foreach (var pair in expected) Shape(obj[pair.Key], pair.Value!, $"{path}.{pair.Key}");
        }
        else if (value is JsonArray array && template is JsonArray examples && examples.Count > 0)
            foreach (var child in array) Shape(child, examples[0]!, path + "[" + (child is JsonObject rule ? rule["id"]?.ToJsonString() : "") + "]");
        else if (value.GetValueKind() == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetValue<string>()))
            throw new JsonException($"{path}: required text cannot be blank.");
    }
}
