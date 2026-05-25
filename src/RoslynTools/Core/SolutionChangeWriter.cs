using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Core;

/// <summary>
/// The default <see cref="ISolutionChangeWriter" /> — a thin adapter over the
/// static <c>SolutionWriter</c> helper. DI-registered as the production binding.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class SolutionChangeWriter : ISolutionChangeWriter
{
    public Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun)
    {
        return SolutionWriter.ApplyChangesAsync(originalSolution, newSolution, dryRun);
    }

    public async Task WriteFixesAsync(Solution originalSolution, IReadOnlyList<Fix> fixes, bool dryRun)
    {
        if (fixes.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No changes.");
            return;
        }

        if (dryRun)
        {
            foreach (var fix in fixes)
            {
                RtConsole.WriteLine(RtOutputContext.Result, $"[fix {fix.Id}]");
                foreach (var file in fix.Files)
                {
                    var doc = originalSolution.GetDocument(file.DocumentId)
                        ?? throw new InvalidOperationException($"Fix '{fix.Id}' references unknown document {file.DocumentId}.");
                    var oldText = (await doc.GetTextAsync()).ToString();
                    SolutionWriter.PrintDiff(oldText, file.NewText, file.Path);
                }
            }
            return;
        }

        var composed = originalSolution;
        foreach (var fix in fixes)
        {
            foreach (var file in fix.Files)
                composed = composed.WithDocumentText(file.DocumentId, SourceText.From(file.NewText));
        }

        await SolutionWriter.ApplyChangesAsync(originalSolution, composed, dryRun: false);
    }
}
