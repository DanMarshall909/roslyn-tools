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
                    RtConsole.WriteLine(RtOutputContext.Result, $"Updated: {filePath}");
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
                    RtLog.Warning("Added document '{Name}' has no file path — skipped.", newDoc.Name);
                    continue;
                }

                if (dryRun)
                {
                    RtConsole.WriteLine(RtOutputContext.Diff, $"\n+++ {filePath} (new file)");
                    foreach (var line in newText.Lines)
                        RtConsole.WriteLine(RtOutputContext.Diff, $"+{line}");
                }
                else
                {
                    var dir = Path.GetDirectoryName(filePath)!;
                    if (!Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    var encoding = newText.Encoding ?? Encoding.UTF8;
                    File.WriteAllText(filePath, newText.ToString(), encoding);
                    RtConsole.WriteLine(RtOutputContext.Result, $"Created: {filePath}");
                }

                fileCount++;
            }
        }

        if (fileCount == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No changes.");
        }
        else if (dryRun)
        {
            RtConsole.WriteLine(RtOutputContext.Diff, $"\n[dry-run] {fileCount} file(s) would be modified.");
        }
        else
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"\nDone. {fileCount} file(s) updated.");
        }
    }

    internal static void PrintDiff(string oldText, string newText, string filePath)
    {
        RtConsole.WriteLine(RtOutputContext.Diff, $"\n--- {filePath}");
        RtConsole.WriteLine(RtOutputContext.Diff, $"+++ {filePath}");

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);
        const int contextSize = 3;

        var entries = CollectDiffEntries(diff);

        int i = 0;
        while (i < entries.Count)
        {
            if (entries[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            int hunkStart = Math.Max(0, i - contextSize);
            int hunkEnd = ExtendHunkEnd(entries, i, contextSize);

            EmitHunk(entries, hunkStart, hunkEnd);

            i = hunkEnd;
        }
    }

    private static List<(ChangeType Type, string Text, int OldLine, int NewLine)> CollectDiffEntries(
        DiffPaneModel diff)
    {
        var entries = new List<(ChangeType Type, string Text, int OldLine, int NewLine)>();
        int oldLine = 1, newLine = 1;

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

        return entries;
    }

    private static int ExtendHunkEnd(
        List<(ChangeType Type, string Text, int OldLine, int NewLine)> entries,
        int changeStart,
        int contextSize)
    {
        int hunkEnd = changeStart;

        while (hunkEnd < entries.Count)
        {
            // Skip past the current change block
            while (hunkEnd < entries.Count && entries[hunkEnd].Type != ChangeType.Unchanged)
                hunkEnd++;

            // Count consecutive unchanged lines ahead
            int unchangedRun = 0;
            int peek = hunkEnd;
            while (peek < entries.Count && entries[peek].Type == ChangeType.Unchanged)
            {
                unchangedRun++;
                peek++;
            }

            // Merge when the gap is short enough and another change follows
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

        return hunkEnd;
    }

    private static void EmitHunk(
        List<(ChangeType Type, string Text, int OldLine, int NewLine)> entries,
        int hunkStart,
        int hunkEnd)
    {
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

        RtConsole.WriteLine(RtOutputContext.Diff, $"@@ -{entries[hunkStart].OldLine},{oldCount} +{entries[hunkStart].NewLine},{newCount} @@");

        for (int k = hunkStart; k < hunkEnd; k++)
        {
            var prefix = entries[k].Type switch
            {
                ChangeType.Inserted => "+",
                ChangeType.Deleted => "-",
                _ => " "
            };
            RtConsole.WriteLine(RtOutputContext.Diff, $"{prefix}{entries[k].Text}");
        }
    }
}
