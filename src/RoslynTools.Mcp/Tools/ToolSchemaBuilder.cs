using System.CommandLine;

namespace RoslynTools.Mcp.Tools;

/// <summary>
/// ToolSchemaBuilder
/// </summary>
internal sealed class ToolSchemaBuilder
{
    public IReadOnlyList<ToolDefinition> Build()
    {
        return App.Commands
            .Select(entry =>
            {
                var cmd = entry.Build();
                return new ToolDefinition(
                    entry.Info.Name.Replace('-', '_'),
                    entry.Info.WhenToUse,
                    BuildSchema(cmd));
            })
            .ToList();
    }

    private static Dictionary<string, object> BuildSchema(Command cmd)
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
            var prop = new Dictionary<string, object> { ["type"] = ToJsonType(option.ValueType) };
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

    private static string ToJsonType(Type type)
    {
        var t = Nullable.GetUnderlyingType(type) ?? type;
        return t == typeof(bool) ? "boolean"
            : t == typeof(int) || t == typeof(long) ? "integer"
            : "string";
    }
}
