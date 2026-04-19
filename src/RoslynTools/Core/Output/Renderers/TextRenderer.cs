namespace RoslynTools.Core.Output.Renderers;

internal static class TextRenderer
{
    internal static void Write(IRtOutput result, TextWriter writer)
    {
        result.RenderText(writer);
    }
}
