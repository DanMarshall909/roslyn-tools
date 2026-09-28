using System.Collections;
using System.Reflection;
using System.Text.Json;

namespace RoslynTools.Core.Output.Renderers;

/// <summary>
/// Renderer for generating JSON output from Roslyn.
/// </summary>
internal static class JsonRenderer
{
    /// <summary>
    /// Method to write JSON output to a text writer.
    /// </summary>
    internal static void Write(IRtOutput result, TextWriter writer)
    {
        if (result is IRtJsonOutput custom)
        {
            custom.RenderJson(writer);
            return;
        }

        var obj = BuildObject(result, OutputLevel.Detailed);
        var json = JsonSerializer.Serialize(obj, JsonEncoders.Select(RtContext.RelaxJson));
        writer.Write(json);
    }

    private static Dictionary<string, object?>? BuildObject(IRtOutput result, OutputLevel maxLevel)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var prop in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<OutputFieldAttribute>();
            if (attr is null || attr.Level > maxLevel)
                continue;

            var value = prop.GetValue(result);
            dict[attr.Description] = ConvertValue(value, maxLevel);
        }

        return dict;
    }

    private static object? ConvertValue(object? value, OutputLevel maxLevel)
    {
        if (value is null)
            return null;

        if (value is IRtOutput nested)
            return BuildObject(nested, maxLevel);

        if (value is IEnumerable enumerable && value is not string)
        {
            var list = new List<object?>();
            foreach (var item in enumerable) list.Add(item is IRtOutput rtItem ? BuildObject(rtItem, maxLevel) : item);
            return list;
        }

        return value;
    }
}
