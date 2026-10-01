using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
/// Renders a solution change preview without writing files.
/// </summary>
internal static class SolutionWriter
{
    /// <summary>
    /// Validates affected paths before rendering changed and added documents to the
    /// optional output sink. A pathless changed or added document, or conflicting
    /// affected path, fails without output.
    /// </summary>
    internal static async Task PreviewChangesAsync(
        Solution originalSolution,
        Solution newSolution,
        ISolutionWriteOutput? output = null)
    {
        _ = new PreparedChangeSet(originalSolution, newSolution);
        output ??= NullSolutionWriteOutput.Instance;
        var changes = newSolution.GetChanges(originalSolution);
        var fileCount = 0;

        foreach (var projectChanges in changes.GetProjectChanges())
        {
            foreach (var docId in projectChanges.GetChangedDocuments())
            {
                var newDoc = newSolution.GetDocument(docId)!;
                var oldDoc = originalSolution.GetDocument(docId)!;

                var newText = await newDoc.GetTextAsync();
                var oldText = await oldDoc.GetTextAsync();
                var filePath = newDoc.FilePath!;

                output.DocumentDiff(oldText.ToString(), newText.ToString(), filePath);

                fileCount++;
            }

            foreach (var docId in projectChanges.GetAddedDocuments())
            {
                var newDoc = newSolution.GetDocument(docId)!;
                var newText = await newDoc.GetTextAsync();
                var filePath = newDoc.FilePath!;

                output.Diff($"\n+++ {filePath} (new file)");
                foreach (var line in newText.Lines)
                    output.Diff($"+{line}");

                fileCount++;
            }
        }

        if (fileCount == 0)
            output.Result("No changes.");
        else
            output.Diff($"\n[dry-run] {fileCount} file(s) would be modified.");
    }
}
