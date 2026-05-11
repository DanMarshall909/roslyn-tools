namespace RoslynTools.Core.Output;

/// <summary>
/// An interface for a Markdown output system.
/// </summary>
internal interface IRtMarkdownOutput
{
    void RenderMarkdown(TextWriter writer);
}
