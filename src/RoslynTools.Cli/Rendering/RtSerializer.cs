using RoslynTools.Core.Output.Renderers;

namespace RoslynTools.Core.Output;

/// <summary>
/// Serializer class for writing structured output to a text writer.
/// </summary>
internal static class RtSerializer
{
    /// <summary>
    /// Method to write structured output to a text writer.
    /// </summary>
    internal static void Write(IRtOutput result, TextWriter? writer = null)
    {
        var target = writer ?? RtConsole.Out;

        switch (RtContext.Format)
        {
            case OutputFormat.Json:
                JsonRenderer.Write(result, target);
                break;
            case OutputFormat.Markdown:
                MarkdownRenderer.Write(result, target);
                break;
            case OutputFormat.Llm:
                LlmRenderer.Write(result, target);
                break;
            case OutputFormat.Yaml:
                YamlRenderer.Write(result, target);
                break;
            default:
                TextRenderer.Write(result, target);
                break;
        }
    }
}
