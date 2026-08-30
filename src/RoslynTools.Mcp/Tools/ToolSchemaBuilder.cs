using System.Reflection;
using System.Text.Json.Serialization;
using RoslynTools.CommandDescriptors;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;

namespace RoslynTools.Mcp.Tools;

/// <summary>
/// ToolSchemaBuilder
/// </summary>
internal sealed class ToolSchemaBuilder(CommandDescriptorRegistry descriptors)
{
    private readonly CommandDescriptorRegistry _descriptors = descriptors;

    public IReadOnlyList<ToolDefinition> Build()
    {
        return _descriptors.All
            .Where(descriptor => descriptor.McpVisible)
            .Select(Build)
            .ToList();
    }

    internal static ToolDefinition Build(ApplicationCommandDescriptor descriptor) => new(
        descriptor.ToolName,
        descriptor.WhenToUse,
        descriptor.Definition is not null
            ? BuildPortSchema(descriptor.Definition.RequestType)
            : descriptor.Port is not null
                ? BuildLegacyPortSchema(descriptor.Input, descriptor.Port.RequestType)
                : BuildInputSchema(descriptor.Input),
        descriptor.Structured
            ? JsonSchemaBuilder.StructuredOutput()
            : null);

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

    private static Dictionary<string, object> BuildPortSchema(Type requestType)
    {
        var properties = new Dictionary<string, Dictionary<string, object>>(
            StringComparer.Ordinal);
        var required = new List<string>();

        foreach (var property in requestType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (IsAlwaysIgnored(property))
                continue;

            var name = JsonPropertyName(property);
            var schema = BuildPropertySchema(property.PropertyType);
            var description = property.GetCustomAttribute<CommandPropertyDescriptionAttribute>();
            if (description is not null)
                schema["description"] = description.Description;
            properties[name] = schema;

            if (property.GetCustomAttribute<JsonRequiredAttribute>() is not null)
                required.Add(name);
        }

        return JsonSchemaBuilder.Object(
            required.Count > 0 ? [.. required] : null,
            properties.Count > 0 ? properties : null);
    }

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
        if (valueType.IsArray)
        {
            var elementType = valueType.GetElementType() ?? typeof(string);
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
                ["enum"] = Enum.GetNames(valueType)
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
