using System.CommandLine;
using System.Reflection;
using System.Text.Json.Serialization;
using RoslynTools.CommandDescriptors;

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
            .Select(descriptor =>
            {
                var cmd = descriptor.BuildCliCommand();
                return new ToolDefinition(
                    descriptor.ToolName,
                    descriptor.WhenToUse,
                    descriptor.Port is null
                        ? BuildCliSchema(cmd)
                        : BuildPortSchema(cmd, descriptor.Port.RequestType),
                    descriptor.Structured
                        ? JsonSchemaBuilder.StructuredOutput()
                        : null);
            })
            .ToList();
    }

    private static Dictionary<string, object> BuildCliSchema(Command cmd)
    {
        var properties = new Dictionary<string, Dictionary<string, object>>();
        var required = new List<string>();

        foreach (var arg in cmd.Arguments)
        {
            var prop = new Dictionary<string, object> { ["type"] = "string" };
            if (!string.IsNullOrEmpty(arg.Description))
                prop["description"] = arg.Description;
            properties[arg.Name] = prop;
            if (arg.Arity.MinimumNumberOfValues > 0)
                required.Add(arg.Name);
        }

        foreach (var option in cmd.Options)
        {
            var prop = BuildPropertySchema(option.ValueType);
            if (!string.IsNullOrEmpty(option.Description))
                prop["description"] = option.Description;
            properties[option.Name] = prop;
            if (option.IsRequired)
                required.Add(option.Name);
        }

        return JsonSchemaBuilder.Object(
            required.Count > 0 ? [.. required] : null,
            properties.Count > 0 ? properties : null);
    }

    private static Dictionary<string, object> BuildPortSchema(
        Command command,
        Type requestType)
    {
        var descriptions = command.Arguments
            .Select(argument => (Name: ToCamelCase(argument.Name), argument.Description))
            .Concat(command.Options.Select(option =>
                (Name: ToCamelCase(option.Name), option.Description)))
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
            var name = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                       ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];
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
        if (type.IsArray)
        {
            var elementType = type.GetElementType() ?? typeof(string);
            return new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object> { ["type"] = ToJsonType(elementType) }
            };
        }

        return new Dictionary<string, object> { ["type"] = ToJsonType(type) };
    }

    private static string ToJsonType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(bool) ? "boolean"
            : t == typeof(int) || t == typeof(long) ? "integer"
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
