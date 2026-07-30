using RoslynTools.Core.Output;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>Shared text and structured projections of a semantic diff operation.</summary>
internal sealed record SemanticDiffOutput(
    IReadOnlyList<DeclarationDelta> Deltas,
    string Text) : IRtOutput, IRtJsonOutput
{
    public void RenderText(TextWriter writer)
    {
        writer.Write(Text);
    }

    public void RenderJson(TextWriter writer)
    {
        writer.Write(SemanticDiffJson.Serialize(Deltas));
    }
}
