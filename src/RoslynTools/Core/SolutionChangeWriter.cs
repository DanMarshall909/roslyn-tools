using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynTools.Core;

/// <summary>
/// The default <see cref="ISolutionChangeWriter" />. Legacy dry runs retain their diff
/// report when a sink is supplied; non-preview writes use the prepared mutation boundary.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service registered through DI for dependency inversion.")]
internal sealed class SolutionChangeWriter : ISolutionChangeWriter
{
    private readonly ISolutionWriteOutput _output;

    internal SolutionChangeWriter(ISolutionWriteOutput? output = null)
    {
        _output = output ?? NullSolutionWriteOutput.Instance;
    }

    /// <summary>
    ///     Preserves legacy dry-run reports and applies non-preview solution deltas through
    ///     the staged prepared writer.
    /// </summary>
    public Task ApplyChangesAsync(Solution originalSolution, Solution newSolution, bool dryRun)
    {
        if (dryRun)
            return SolutionWriter.ApplyChangesAsync(originalSolution, newSolution, dryRun: true, _output);

        var changeSet = new PreparedChangeSet(originalSolution, newSolution);
        if (!changeSet.HasChanges)
            return Task.CompletedTask;
        return new PreparedChangeSetWriter(new PhysicalPreparedFileSystem())
            .ApplyAsync(changeSet);
    }

    /// <summary>
    ///     Renders fix diffs during preview or composes and silently applies all fixes through
    ///     the staged writer. An empty set retains the legacy no-change message.
    /// </summary>
    public async Task WriteFixesAsync(Solution originalSolution, IReadOnlyList<Fix> fixes, bool dryRun)
    {
        if (fixes.Count == 0)
        {
            _output.Result("No changes.");
            return;
        }

        if (dryRun)
        {
            foreach (var fix in fixes)
            {
                _output.Result($"[fix {fix.Id}]");
                foreach (var file in fix.Files)
                {
                    var doc = originalSolution.GetDocument(file.DocumentId)
                        ?? throw new InvalidOperationException($"Fix '{fix.Id}' references unknown document {file.DocumentId}.");
                    var oldText = (await doc.GetTextAsync()).ToString();
                    _output.DocumentDiff(oldText, file.NewText, file.Path);
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

        await ApplyChangesAsync(originalSolution, composed, dryRun: false);
    }
}
