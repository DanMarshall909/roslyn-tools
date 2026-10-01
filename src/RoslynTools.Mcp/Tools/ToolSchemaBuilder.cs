using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;

namespace RoslynTools.Mcp.Tools;

/// <summary>
/// ToolSchemaBuilder
/// </summary>
internal sealed class ToolSchemaBuilder(
    ApplicationCommandCatalog descriptors,
    McpHostOptions hostOptions)
{
    private static readonly JsonSerializerOptions ModeJsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ApplicationCommandCatalog _descriptors = descriptors;
    private readonly McpHostOptions _hostOptions = hostOptions;

    public IReadOnlyList<ToolDefinition> Build()
    {
        return _descriptors.All
            .Where(descriptor => McpToolAvailability.IsAvailable(descriptor, _hostOptions))
            .Select(Build)
            .ToList();
    }

    internal static ToolDefinition Build(ApplicationCommandDescriptor descriptor)
    {
        var product = descriptor.Definition?.Product;
        var writes = descriptor.Effects.HasFlag(CommandEffects.RepositoryWrite)
                     || descriptor.Effects.HasFlag(CommandEffects.LocalStateWrite)
                     || descriptor.Effects.HasFlag(CommandEffects.ExternalWrite);
        var external = descriptor.Effects.HasFlag(CommandEffects.ExternalRead)
                       || descriptor.Effects.HasFlag(CommandEffects.ExternalWrite);

        var metadata = new Dictionary<string, object>
        {
            ["rt/maturity"] = (product?.Maturity ?? CapabilityMaturity.Experimental).ToString(),
            ["rt/effects"] = CommandProductMetadata.EffectNames(descriptor.Effects),
            ["rt/audience"] = descriptor.Audience.ToString(),
            ["rt/workspaceRequirement"] =
                (product?.WorkspaceRequirement ?? WorkspaceRequirement.Semantic).ToString(),
            ["rt/defaultTimeoutMilliseconds"] =
                product?.Limits.DefaultTimeoutMilliseconds ?? 30_000,
            ["rt/maximumItems"] = product?.Limits.MaximumItems ?? 100,
            ["rt/maximumOutputBytes"] = product?.Limits.MaximumOutputBytes ?? 1_048_576,
            ["rt/cliExamples"] = descriptor.Definition?.Info.Examples ?? []
        };
        if (product?.Modes is { Count: > 0 } modes)
            metadata["rt/modes"] = JsonSerializer.SerializeToElement(
                modes.Select(mode => mode.ToDescriptor()).ToArray(),
                ModeJsonOptions);

        return new ToolDefinition(
            descriptor.ToolName,
            descriptor.WhenToUse,
            descriptor.Definition is not null
                ? BuildPortSchema(descriptor.Definition)
                : descriptor.Port is not null
                    ? BuildLegacyPortSchema(descriptor.Input, descriptor.Port.RequestType)
                    : BuildInputSchema(descriptor.Input),
            descriptor.Structured
                ? JsonSchemaBuilder.StructuredOutput()
                : null,
            new ToolAnnotations(
                descriptor.ToolName.Replace('_', ' '),
                readOnlyHint: !writes,
                destructiveHint: writes,
                idempotentHint: product?.Idempotency == CommandIdempotency.Idempotent || !writes,
                openWorldHint: external),
            metadata);
    }

    private static Dictionary<string, object> BuildInputSchema(ApplicationCommandInput input)
    {
        var properties = new Dictionary<string, Dictionary<string, object>>();
        var required = new List<string>();

        foreach (var argument in input.Arguments)
        {
            var property = BuildParameterSchema(argument.ValueType);
            if (!string.IsNullOrEmpty(argument.Description))
                property["description"] = argument.Description;
            properties[argument.Name] = property;
            if (argument.Required)
                required.Add(argument.Name);
        }

        foreach (var option in input.Options)
        {
            var property = BuildParameterSchema(option.ValueType);
            if (!string.IsNullOrEmpty(option.Description))
                property["description"] = option.Description;
            properties[option.Name] = property;
            if (option.Required)
                required.Add(option.Name);
        }

        return JsonSchemaBuilder.Object(
            required.Count > 0 ? [.. required] : null,
            properties.Count > 0 ? properties : null);
    }

    private static Dictionary<string, object> BuildPortSchema(ApplicationCommandDefinition definition)
    {
        var requestType = definition.RequestType;
        var properties = new Dictionary<string, Dictionary<string, object>>(
            StringComparer.Ordinal);
        var required = new List<string>();

        foreach (var property in requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsAlwaysIgnored(property))
                continue;

            var name = JsonPropertyName(property);
            var propertySchema = BuildPropertySchema(property.PropertyType);
            var description = property.GetCustomAttribute<CommandPropertyDescriptionAttribute>();
            if (description is not null)
                propertySchema["description"] = description.Description;
            properties[name] = propertySchema;

            if (property.GetCustomAttribute<JsonRequiredAttribute>() is not null)
                required.Add(name);
        }

        var schema = JsonSchemaBuilder.Object(
            required.Count > 0 ? [.. required] : null,
            properties.Count > 0 ? properties : null);
        if (definition.RequestVariants.Count > 0)
            schema["oneOf"] = definition.RequestVariants
                .Select(variant => BuildRequestVariantSchema(requestType, variant, properties, required))
                .ToArray();

        return schema;
    }

    private static Dictionary<string, object> BuildRequestVariantSchema(
        Type requestType,
        CommandRequestVariant variant,
        IReadOnlyDictionary<string, Dictionary<string, object>> rootProperties,
        IReadOnlyList<string> rootRequired)
    {
        var discriminator = ResolveRequestProperty(requestType, variant.DiscriminatorProperty);
        var discriminatorName = JsonPropertyName(discriminator);
        var discriminatorSchema = BuildPropertySchema(discriminator.PropertyType);
        discriminatorSchema["enum"] = new[] { variant.DiscriminatorValue };

        var required = rootRequired.Concat(variant.RequiredProperties
            .Select(name => JsonPropertyName(ResolveRequestProperty(requestType, name))))
            .ToList();
        if (variant.DiscriminatorRequired)
            required.Add(discriminatorName);

        var forbidden = variant.ForbiddenProperties
            .Select(name => JsonPropertyName(ResolveRequestProperty(requestType, name)))
            .ToHashSet(StringComparer.Ordinal);
        var properties = rootProperties
            .Where(property => !forbidden.Contains(property.Key))
            .ToDictionary(property => property.Key, property => property.Value, StringComparer.Ordinal);
        properties[discriminatorName] = discriminatorSchema;
        var schema = JsonSchemaBuilder.Object(
            [.. required.Distinct(StringComparer.Ordinal)], properties);
        if (variant.ForbiddenProperties.Count > 0)
        {
            schema["not"] = new Dictionary<string, object>
            {
                ["anyOf"] = variant.ForbiddenProperties
                    .Select(name => (object)new Dictionary<string, object>
                    {
                        ["required"] = new[]
                        {
                            JsonPropertyName(ResolveRequestProperty(requestType, name))
                        }
                    })
                    .ToArray()
            };
        }

        return schema;
    }

    private static PropertyInfo ResolveRequestProperty(Type requestType, string propertyName) =>
        requestType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)
        ?? throw new InvalidOperationException(
            $"Request variant property '{propertyName}' does not exist on '{requestType.Name}'.");

    private static Dictionary<string, object> BuildLegacyPortSchema(
        ApplicationCommandInput input,
        Type requestType)
    {
        var descriptions = input.Arguments
            .Concat(input.Options)
            .Select(parameter => (Name: ToCamelCase(parameter.Name), parameter.Description))
            .Where(item => !string.IsNullOrEmpty(item.Description))
            .GroupBy(item => item.Name, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Description!,
                StringComparer.Ordinal);

        var properties = new Dictionary<string, Dictionary<string, object>>(
            StringComparer.Ordinal);
        var required = new List<string>();

        foreach (var property in requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsAlwaysIgnored(property))
                continue;

            var name = JsonPropertyName(property);
            var schema = BuildPropertySchema(property.PropertyType);
            if (descriptions.TryGetValue(ToCamelCase(name), out var description))
                schema["description"] = description;
            properties[name] = schema;

            if (property.GetCustomAttribute<JsonRequiredAttribute>() is not null)
                required.Add(name);
        }

        return JsonSchemaBuilder.Object(
            required.Count > 0 ? [.. required] : null,
            properties.Count > 0 ? properties : null);
    }

    private static Dictionary<string, object> BuildPropertySchema(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        var elementType = valueType.IsArray
            ? valueType.GetElementType()
            : valueType.IsGenericType
              && valueType.GetGenericArguments() is [var itemType]
              && typeof(IEnumerable<>).MakeGenericType(itemType).IsAssignableFrom(valueType)
                ? itemType
                : null;
        if (elementType is not null)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = BuildPropertySchema(elementType)
            };
        }

        if (valueType.IsEnum)
        {
            return new Dictionary<string, object>
            {
                ["type"] = "string",
                ["enum"] = valueType.GetFields(BindingFlags.Public | BindingFlags.Static)
                    .Select(field => field.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                                     ?? field.Name)
                    .ToArray()
            };
        }

        return new Dictionary<string, object> { ["type"] = ToJsonType(valueType) };
    }

    private static Dictionary<string, object> BuildParameterSchema(string valueType)
    {
        if (valueType.EndsWith("[]", StringComparison.Ordinal))
        {
            return new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object>
                {
                    ["type"] = valueType[..^2]
                }
            };
        }

        return new Dictionary<string, object> { ["type"] = valueType };
    }

    private static bool IsAlwaysIgnored(PropertyInfo property) =>
        property.GetCustomAttribute<JsonIgnoreAttribute>()?.Condition
        == JsonIgnoreCondition.Always;

    private static string JsonPropertyName(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

    private static string ToJsonType(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        return valueType == typeof(bool) ? "boolean"
            : valueType == typeof(int) || valueType == typeof(long) ? "integer"
            : "string";
    }

    private static string ToCamelCase(string value)
    {
        var parts = value.Split('-', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
            return value;

        return parts[0] + string.Concat(parts.Skip(1).Select(part =>
            char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
