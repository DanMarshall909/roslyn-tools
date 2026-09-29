using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
/// Handles writing and applying changes to a solution.
/// </summary>
internal static class SolutionWriter
{
    /// <summary>
    /// Applies changes to a solution asynchronously and reports progress to the optional sink.
    /// </summary>
    internal static async Task ApplyChangesAsync(
        Solution originalSolution,
        Solution newSolution,
        bool dryRun,
        ISolutionWriteOutput? output = null)
    {
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

                if (dryRun)
                {
                    output.DocumentDiff(oldText.ToString(), newText.ToString(), filePath);
                }
                else
                {
                    var encoding = newText.Encoding ?? Encoding.UTF8;
                    File.WriteAllText(filePath, newText.ToString(), encoding);
                    output.Result($"Updated: {filePath}");
                }

                fileCount++;
            }

            foreach (var docId in projectChanges.GetAddedDocuments())
            {
                var newDoc = newSolution.GetDocument(docId)!;
                var newText = await newDoc.GetTextAsync();
                var filePath = newDoc.FilePath;

                if (string.IsNullOrEmpty(filePath))
                {
                    output.Warning($"Added document '{newDoc.Name}' has no file path — skipped.");
                    continue;
                }

                if (dryRun)
                {
                    output.Diff($"\n+++ {filePath} (new file)");
                    foreach (var line in newText.Lines)
                        output.Diff($"+{line}");
                }
                else
                {
                    var dir = Path.GetDirectoryName(filePath)!;
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var encoding = newText.Encoding ?? Encoding.UTF8;
                    File.WriteAllText(filePath, newText.ToString(), encoding);
                    output.Result($"Created: {filePath}");
                }

                fileCount++;
            }
        }

        if (fileCount == 0)
            output.Result("No changes.");
        else if (dryRun)
            output.Diff($"\n[dry-run] {fileCount} file(s) would be modified.");
        else
            output.Result($"\nDone. {fileCount} file(s) updated.");
    }
}
