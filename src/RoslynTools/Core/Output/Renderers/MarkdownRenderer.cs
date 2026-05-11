using System.Collections;
using System.Reflection;

namespace RoslynTools.Core.Output.Renderers;

/// <summary>
/// Renderer for generating markdown output from Roslyn.
/// </summary>
internal static class MarkdownRenderer
{
    /// <summary>
    /// Method to write markdown output to a text writer.
    /// </summary>
    internal static void Write(IRtOutput result, TextWriter writer)
    {
        if (result is IRtMarkdownOutput md)
        {
            md.RenderMarkdown(writer);
            return;
        }

        var maxLevel = OutputLevel.Default;
        var props = GetAnnotatedProperties(result.GetType(), maxLevel);

        foreach (var (prop, attr) in props)
        {
            var value = prop.GetValue(result);

            if (value is IEnumerable enumerable && value is not string)
            {
                WriteTable(enumerable, attr.Description, writer);
            }
            else
            {
                var display = value is IRtOutput nested ? nested.ToString() : value?.ToString() ?? string.Empty;
                writer.WriteLine($"**{attr.Description}:** {display}");
            }
        }
    }

    private static void WriteTable(IEnumerable items, string heading, TextWriter writer)
    {
        var rows = items.Cast<object?>().ToList();
        if (rows.Count == 0)
            return;

        IRtOutput? first = null;
        foreach (var row in rows)
            if (row is IRtOutput rt)
            {
                first = rt;
                break;
            }

        if (first is null)
            return;

        var childProps = GetAnnotatedProperties(first.GetType(), OutputLevel.Default);
        var headers = childProps.Select(p => p.attr.Description).ToList();

        writer.WriteLine($"| {string.Join(" | ", headers)} |");
        writer.WriteLine($"| {string.Join(" | ", headers.Select(_ => "---"))} |");

        foreach (var row in rows)
        {
            if (row is not IRtOutput rtRow)
                continue;

            var cells = childProps.Select(p =>
            {
                var v = p.prop.GetValue(rtRow);
                return v?.ToString() ?? string.Empty;
            });

            writer.WriteLine($"| {string.Join(" | ", cells)} |");
        }
    }

    private static List<(PropertyInfo prop, OutputFieldAttribute attr)> GetAnnotatedProperties(Type type,
        OutputLevel maxLevel)
    {
        return type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => (prop: p, attr: p.GetCustomAttribute<OutputFieldAttribute>()))
            .Where(x => x.attr is not null && x.attr.Level <= maxLevel)
            .Select(x => (x.prop, x.attr!))
            .ToList();
    }
}
