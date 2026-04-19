using System.Collections;
using System.Reflection;

namespace RoslynTools.Core.Output.Renderers;

internal static class LlmRenderer
{
    internal static void Write(IRtOutput result, TextWriter writer)
    {
        WriteRecord(result, writer);
    }

    private static void WriteRecord(IRtOutput result, TextWriter writer)
    {
        var props = result.GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (prop: p, attr: p.GetCustomAttribute<OutputFieldAttribute>()))
            .Where(x => x.attr is not null && x.attr.Level == OutputLevel.Llm)
            .Select(x => (x.prop, x.attr!))
            .ToList();

        foreach (var (prop, attr) in props)
        {
            var value = prop.GetValue(result);

            if (value is null)
                continue;

            if (value is IEnumerable enumerable && value is not string)
            {
                bool first = true;
                foreach (var item in enumerable)
                {
                    if (!first)
                        writer.WriteLine();
                    first = false;

                    if (item is IRtOutput rtItem)
                        WriteRecord(rtItem, writer);
                }
            }
            else
            {
                writer.WriteLine($"{attr.Description}: {value}");
            }
        }
    }
}
