namespace RoslynTools.Core.Output;

/// <summary>
/// An interface for a general output system.
/// </summary>
internal interface IRtOutput
{
    void RenderText(TextWriter writer);
}
