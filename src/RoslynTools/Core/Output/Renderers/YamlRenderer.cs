using System.Collections;
using System.Reflection;
using YamlDotNet.Serialization;

namespace RoslynTools.Core.Output.Renderers;

internal static class YamlRenderer
{
    private static readonly ISerializer Serializer = new SerializerBuilder().Build();

    internal static void Write(IRtOutput result, TextWriter writer)
    {
        if (result is IRtYamlOutput custom)
        {
            custom.RenderYaml(writer);
            return;
        }

        var obj = BuildObject(result, OutputLevel.Detailed);
        writer.Write(Serializer.Serialize(obj));
    }

    private static Dictionary<string, object?> BuildObject(IRtOutput result, OutputLevel maxLevel)
    {
        var dict = new Dictionary<string, object?>();

        foreach (var prop in result.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var attr = prop.GetCustomAttribute<OutputFieldAttribute>();
            if (attr is null || attr.Level > maxLevel)
                continue;

            var value = prop.GetValue(result);
            dict[prop.Name] = ConvertValue(value, maxLevel);
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
            foreach (var item in enumerable)
            {
                list.Add(item is IRtOutput rtItem ? BuildObject(rtItem, maxLevel) : item);
            }
            return list;
        }

        return value;
    }
}
