using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using RoslynTools.Core;

namespace RoslynTools.Adapters;

/// <summary>Renders legacy solution previews through the host output sink.</summary>
internal static class SolutionDiffRenderer
{
    /// <summary>
    /// Reports the difference between two strings to the optional sink.
    /// </summary>
    internal static void PrintDiff(
        string oldText, string newText, string filePath, ISolutionWriteOutput output)
    {
        output.Diff($"\n--- {filePath}");
        output.Diff($"+++ {filePath}");

        var diffBuilder = new InlineDiffBuilder(new Differ());
        var diff = diffBuilder.BuildDiffModel(oldText, newText);
        const int contextSize = 3;

        var entries = CollectDiffEntries(diff);

        var i = 0;
        while (i < entries.Count)
        {
            if (entries[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            var hunkStart = Math.Max(0, i - contextSize);
            var hunkEnd = ExtendHunkEnd(entries, i, contextSize);

            EmitHunk(entries, hunkStart, hunkEnd, output);

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
        var hunkEnd = changeStart;

        while (hunkEnd < entries.Count)
        {
            // Skip past the current change block
            while (hunkEnd < entries.Count && entries[hunkEnd].Type != ChangeType.Unchanged)
                hunkEnd++;

            // Count consecutive unchanged lines ahead
            var unchangedRun = 0;
            var peek = hunkEnd;
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
        int hunkEnd,
        ISolutionWriteOutput output)
    {
        int oldCount = 0, newCount = 0;
        for (var k = hunkStart; k < hunkEnd; k++)
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

        output.Diff(
            $"@@ -{entries[hunkStart].OldLine},{oldCount} +{entries[hunkStart].NewLine},{newCount} @@");

        for (var k = hunkStart; k < hunkEnd; k++)
        {
            var prefix = entries[k].Type switch
            {
                ChangeType.Inserted => "+",
                ChangeType.Deleted => "-",
                _ => " "
            };
            output.Diff($"{prefix}{entries[k].Text}");
        }
    }
}
