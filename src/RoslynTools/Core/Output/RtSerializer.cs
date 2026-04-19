using RoslynTools.Core.Output.Renderers;

namespace RoslynTools.Core.Output;

internal static class RtSerializer
{
    internal static void Write(IRtOutput result, TextWriter? writer = null)
    {
        var target = writer ?? Console.Out;

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
            default:
                TextRenderer.Write(result, target);
                break;
        }
    }
}
