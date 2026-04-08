using Microsoft.CodeAnalysis;
using System.Text;

namespace RoslynTools.Core;

internal static class SolutionWriter
{
    internal static async Task ApplyChangesAsync(
        Solution originalSolution,
        Solution newSolution,
        bool dryRun)
    {
        var changes = newSolution.GetChanges(originalSolution);
        int fileCount = 0;

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
                    PrintDiff(oldText.ToString(), newText.ToString(), filePath);
                }
                else
                {
                    var encoding = newText.Encoding ?? Encoding.UTF8;
                    File.WriteAllText(filePath, newText.ToString(), encoding);
                    Console.WriteLine($"Updated: {filePath}");
                }

                fileCount++;
            }

            foreach (var docId in projectChanges.GetAddedDocuments())
            {
                var newDoc = newSolution.GetDocument(docId)!;
                Console.WriteLine($"WARNING: Document addition detected for '{newDoc.Name}' — not written automatically. Create this file manually.");
            }
        }

        if (fileCount == 0)
        {
            Console.WriteLine("No changes.");
        }
        else if (dryRun)
        {
            Console.WriteLine($"\n[dry-run] {fileCount} file(s) would be modified.");
        }
        else
        {
            Console.WriteLine($"\nDone. {fileCount} file(s) updated.");
        }
    }

    private static void PrintDiff(string oldText, string newText, string filePath)
    {
        Console.WriteLine($"\n--- {filePath}");
        Console.WriteLine($"+++ {filePath}");

        var oldLines = oldText.Split('\n');
        var newLines = newText.Split('\n');

        // Simple line-by-line diff — finds changed blocks and prints context
        int i = 0, j = 0;
        while (i < oldLines.Length || j < newLines.Length)
        {
            if (i < oldLines.Length && j < newLines.Length && oldLines[i] == newLines[j])
            {
                i++;
                j++;
                continue;
            }

            // Find the extent of the differing block
            int oldStart = i, newStart = j;
            while (i < oldLines.Length && j < newLines.Length && oldLines[i] != newLines[j])
            {
                i++;
                j++;
            }

            Console.WriteLine($"@@ -{oldStart + 1},{i - oldStart} +{newStart + 1},{j - newStart} @@");
            for (int k = oldStart; k < i; k++)
                Console.WriteLine($"-{oldLines[k]}");
            for (int k = newStart; k < j; k++)
                Console.WriteLine($"+{newLines[k]}");
        }
    }
}
