namespace RoslynTools.Services.SemanticDiff;

internal interface ISemanticDiffTextRenderer
{
    string Render(SemanticDiffResult result, IReadOnlyList<SemanticDiffSource> before, IReadOnlyList<SemanticDiffSource> after);
}
