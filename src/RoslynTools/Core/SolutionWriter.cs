using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
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
                var newText = await newDoc.GetTextAsync();
                var filePath = newDoc.FilePath;

                if (string.IsNullOrEmpty(filePath))
                {
                    Console.Error.WriteLine($"WARNING: Added document '{newDoc.Name}' has no file path — skipped.");
                    continue;
                }

                if (dryRun)
                {
                    Console.WriteLine($"\n+++ {filePath} (new file)");
                    foreach (var line in newText.Lines)
                        Console.WriteLine($"+{line}");
                }
                else
                {
                    var dir = Path.GetDirectoryName(filePath)!;
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var encoding = newText.Encoding ?? Encoding.UTF8;
                    File.WriteAllText(filePath, newText.ToString(), encoding);
                    Console.WriteLine($"Created: {filePath}");
                }

                fileCount++;
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

    internal static void PrintDiff(string oldText, string newText, string filePath)
    {
        Console.WriteLine($"\n--- {filePath}");
        Console.WriteLine($"+++ {filePath}");

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);

        // Track line numbers for old and new sides
        int oldLine = 1, newLine = 1;
        const int contextSize = 3;

        // Collect all lines with their metadata
        var entries = new List<(ChangeType Type, string Text, int OldLine, int NewLine)>();
        foreach (var line in diff.Lines)
        {
            entries.Add((line.Type, line.Text, oldLine, newLine));
            switch (line.Type)
            {
                case ChangeType.Unchanged:
                case ChangeType.Modified:
                    oldLine++;
                    newLine++;
                    break;
                case ChangeType.Deleted:
                    oldLine++;
                    break;
                case ChangeType.Inserted:
                    newLine++;
                    break;
            }
        }

        // Find changed regions and emit hunks with context
        int i = 0;
        while (i < entries.Count)
        {
            if (entries[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            // Found a change — expand to include context and adjacent changes
            int hunkStart = Math.Max(0, i - contextSize);
            int hunkEnd = i;

            // Walk forward through changes and gaps smaller than 2*context
            while (hunkEnd < entries.Count)
            {
                // Skip past the current change block
                while (hunkEnd < entries.Count && entries[hunkEnd].Type != ChangeType.Unchanged)
                    hunkEnd++;

                // Count consecutive unchanged lines
                int unchangedRun = 0;
                int peek = hunkEnd;
                while (peek < entries.Count && entries[peek].Type == ChangeType.Unchanged)
                {
                    unchangedRun++;
                    peek++;
                }

                // If the gap is small enough and there's another change after, merge
                if (unchangedRun <= contextSize * 2 && peek < entries.Count && entries[peek].Type != ChangeType.Unchanged)
                {
                    hunkEnd = peek;
                }
                else
                {
                    hunkEnd = Math.Min(hunkEnd + contextSize, entries.Count);
                    break;
                }
            }

            // Compute hunk header line numbers
            int oldStart = entries[hunkStart].OldLine;
            int newStart = entries[hunkStart].NewLine;
            int oldCount = 0, newCount = 0;
            for (int k = hunkStart; k < hunkEnd; k++)
            {
                switch (entries[k].Type)
                {
                    case ChangeType.Unchanged:
                        oldCount++;
                        newCount++;
                        break;
                    case ChangeType.Deleted:
                        oldCount++;
                        break;
                    case ChangeType.Inserted:
                        newCount++;
                        break;
                }
            }

            Console.WriteLine($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@");

            for (int k = hunkStart; k < hunkEnd; k++)
            {
                var prefix = entries[k].Type switch
                {
                    ChangeType.Inserted => "+",
                    ChangeType.Deleted => "-",
                    _ => " "
                };
                Console.WriteLine($"{prefix}{entries[k].Text}");
            }

            i = hunkEnd;
        }
    }
}
