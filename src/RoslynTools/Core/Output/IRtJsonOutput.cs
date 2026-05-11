namespace RoslynTools.Core.Output;

/// <summary>
/// An interface for a JSON output system.
/// </summary>
internal interface IRtJsonOutput
{
    void RenderJson(TextWriter writer);
}
