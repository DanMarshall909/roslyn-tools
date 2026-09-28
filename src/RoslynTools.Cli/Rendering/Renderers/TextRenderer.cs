namespace RoslynTools.Core.Output.Renderers;

/// <summary>
/// Renderer for generating text output from Roslyn.
/// </summary>
internal static class TextRenderer
{
    /// <summary>
    /// Method to write text output to a text writer.
    /// </summary>
    internal static void Write(IRtOutput result, TextWriter writer)
    {
        result.RenderText(writer);
    }
}
