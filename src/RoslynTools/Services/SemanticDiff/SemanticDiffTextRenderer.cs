using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Services.SemanticDiff;

/// <summary>
///     Host-agnostic renderer that turns a <see cref="SemanticDiffResult" /> into a
///     git-diff-style plain-text report. Returns a string — no <c>Console</c>, no
///     <c>System.CommandLine</c> — so any host (CLI, MCP) can wrap it.
///     <para>
///         The result carries no source text, so each declaration's syntax node is
///         re-located by its FQN through <see cref="DeclarationWalker" /> against the same
///         before/after snapshots the engine compared. Node text is rendered via
///         <c>NormalizeWhitespace().ToFullString()</c> so trivia noise (the original
///         indentation, leading comments) never leaks into the +/- body lines.
///     </para>
///     <para>
///         Per-status shapes (deltas arrive already sorted Ordinal by FQN, so output order
///         is the engine's order):
///         <list type="bullet">
///             <item><c>~ Changed</c> header + a DiffPlex unified hunk of old vs new node text.</item>
///             <item><c>+ Added</c> header + the new node text as <c>+</c> lines.</item>
///             <item><c>- Removed</c> header + the old node text as <c>-</c> lines.</item>
///             <item><c>R Renamed</c> header (<c>OldFqn -&gt; NewFqn</c>) + a hunk only when the bodies differ.</item>
///             <item><c>&gt; Moved</c> one-line relocation note when an Unchanged declaration changed file.</item>
///         </list>
///         An Unchanged declaration on the same path emits nothing. When nothing is emitted
///         at all, the whole render is the terse <see cref="NoChanges" /> line.
///     </para>
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class registered in DI per the static-to-instance migration (EP-006). CA1822 enforces the old static-when-stateless rule that conflicts with this direction; suppress per class until the migration completes and the rule is reviewed project-wide.")]
internal sealed class SemanticDiffTextRenderer : IRtService
{
    /// <summary>Emitted when there is nothing semantically interesting to show.</summary>
    public const string NoChanges = "no semantic changes";

    private const int ContextSize = 3;

    /// <summary>
    ///     Render <paramref name="result" /> against the <paramref name="before" /> and
    ///     <paramref name="after" /> snapshots. The result carries no source text, so each
    ///     declaration's node is re-located by FQN through <see cref="DeclarationWalker" />.
    /// </summary>
    public string Render(
        SemanticDiffResult result,
        IReadOnlyList<SemanticDiffSource> before,
        IReadOnlyList<SemanticDiffSource> after)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var beforeDecls = DeclarationWalker.Collect(before);
        var afterDecls = DeclarationWalker.Collect(after);

        var sb = new StringBuilder();
        foreach (var delta in result.Deltas)
            RenderDelta(sb, delta, beforeDecls, afterDecls);

        return sb.Length == 0 ? NoChanges : sb.ToString().TrimEnd('\n');
    }

    private static void RenderDelta(
        StringBuilder sb,
        DeclarationDelta delta,
        IReadOnlyDictionary<string, DeclEntry> beforeDecls,
        IReadOnlyDictionary<string, DeclEntry> afterDecls)
    {
        var kind = KindLabel(delta.Kind);
        switch (delta.Status)
        {
            case DeclarationStatus.Changed:
                {
                    var oldText = NodeText(beforeDecls[delta.Fqn]);
                    var newText = NodeText(afterDecls[delta.Fqn]);
                    sb.Append(CultureInfo.InvariantCulture, $"~ Changed  {kind} {delta.Fqn}\n");
                    AppendHunk(sb, oldText, newText);
                    sb.Append('\n');
                    break;
                }

            case DeclarationStatus.Added:
                {
                    var newText = NodeText(afterDecls[delta.Fqn]);
                    sb.Append(CultureInfo.InvariantCulture, $"+ Added    {kind} {delta.Fqn}   ({delta.NewPath})\n");
                    AppendPrefixed(sb, newText, '+');
                    sb.Append('\n');
                    break;
                }

            case DeclarationStatus.Removed:
                {
                    var oldText = NodeText(beforeDecls[delta.Fqn]);
                    sb.Append(CultureInfo.InvariantCulture, $"- Removed  {kind} {delta.Fqn}   ({delta.OldPath})\n");
                    AppendPrefixed(sb, oldText, '-');
                    sb.Append('\n');
                    break;
                }

            case DeclarationStatus.Renamed:
                {
                    // The engine only ever emits Renamed for a PURE rename — its rename pass
                    // pairs solely on equivalent-modulo-name bodies, so a name+body change
                    // stays Added+Removed and never reaches here. The two node texts therefore
                    // differ only in the identifier, which makes a body hunk pure noise; the
                    // rename line alone is the whole story.
                    sb.Append(
                        CultureInfo.InvariantCulture,
                        $"R Renamed  {kind} {delta.OldFqn} -> {delta.NewFqn}   ({delta.NewPath})\n\n");
                    break;
                }

            case DeclarationStatus.Unchanged:
                {
                    // Same-path Unchanged is silent; a path change is a relocation note, never
                    // a delete+add pair.
                    if (delta.OldPath != delta.NewPath)
                    {
                        sb.Append(
                            CultureInfo.InvariantCulture,
                            $"> Moved    {kind} {delta.Fqn}   {delta.OldPath} -> {delta.NewPath}\n\n");
                    }

                    break;
                }
        }
    }

    /// <summary>
    ///     Render a declaration's node as canonical text. <c>NormalizeWhitespace</c> strips
    ///     the original trivia so only the structural content drives the diff.
    /// </summary>
    private static string NodeText(DeclEntry entry) =>
        entry.Node.NormalizeWhitespace().ToFullString();

    /// <summary>Emit every line of <paramref name="text" /> with a fixed diff prefix.</summary>
    private static void AppendPrefixed(StringBuilder sb, string text, char prefix)
    {
        foreach (var line in SplitLines(text))
            sb.Append(prefix).Append(line).Append('\n');
    }

    /// <summary>
    ///     Append a git-style unified hunk comparing <paramref name="oldText" /> with
    ///     <paramref name="newText" />, built with DiffPlex's <see cref="InlineDiffBuilder" />.
    ///     <para>
    ///         This is a string-returning sibling of <c>SolutionWriter.PrintDiff</c>, which
    ///         writes the same shape to the console. They are kept as two small, independent
    ///         implementations per the repo's pragmatic-duplication doctrine: two occurrences
    ///         do not yet warrant extracting a shared host-agnostic diff core, and doing so
    ///         would pull <c>SolutionWriter</c>'s console-bound tests into this batch's blast
    ///         radius. Consolidation is a documented follow-up if a third caller appears.
    ///     </para>
    /// </summary>
    private static void AppendHunk(StringBuilder sb, string oldText, string newText)
    {
        var diff = new InlineDiffBuilder(new Differ()).BuildDiffModel(oldText, newText);
        var entries = CollectDiffEntries(diff);

        var i = 0;
        while (i < entries.Count)
        {
            if (entries[i].Type == ChangeType.Unchanged)
            {
                i++;
                continue;
            }

            var hunkStart = Math.Max(0, i - ContextSize);
            var hunkEnd = ExtendHunkEnd(entries, i);
            EmitHunk(sb, entries, hunkStart, hunkEnd);
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
        int changeStart)
    {
        var hunkEnd = changeStart;

        while (hunkEnd < entries.Count)
        {
            while (hunkEnd < entries.Count && entries[hunkEnd].Type != ChangeType.Unchanged)
                hunkEnd++;

            var unchangedRun = 0;
            var peek = hunkEnd;
            while (peek < entries.Count && entries[peek].Type == ChangeType.Unchanged)
            {
                unchangedRun++;
                peek++;
            }

            if (unchangedRun <= ContextSize * 2 && peek < entries.Count && entries[peek].Type != ChangeType.Unchanged)
            {
                hunkEnd = peek;
            }
            else
            {
                hunkEnd = Math.Min(hunkEnd + ContextSize, entries.Count);
                break;
            }
        }

        return hunkEnd;
    }

    private static void EmitHunk(
        StringBuilder sb,
        List<(ChangeType Type, string Text, int OldLine, int NewLine)> entries,
        int hunkStart,
        int hunkEnd)
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

        sb.Append(
            CultureInfo.InvariantCulture,
            $"@@ -{entries[hunkStart].OldLine},{oldCount} +{entries[hunkStart].NewLine},{newCount} @@\n");

        for (var k = hunkStart; k < hunkEnd; k++)
        {
            var prefix = entries[k].Type switch
            {
                ChangeType.Inserted => "+",
                ChangeType.Deleted => "-",
                _ => " ",
            };
            sb.Append(prefix).Append(entries[k].Text).Append('\n');
        }
    }

    /// <summary>Split node text into lines, dropping a single trailing empty line.</summary>
    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static string KindLabel(DeclarationKind kind) => kind switch
    {
        DeclarationKind.Class => "class",
        DeclarationKind.Struct => "struct",
        DeclarationKind.Record => "record",
        DeclarationKind.Interface => "interface",
        DeclarationKind.Enum => "enum",
        DeclarationKind.Delegate => "delegate",
        DeclarationKind.Method => "method",
        DeclarationKind.Constructor => "ctor",
        DeclarationKind.Property => "property",
        DeclarationKind.Field => "field",
        DeclarationKind.Event => "event",
        DeclarationKind.Indexer => "indexer",
        DeclarationKind.Operator => "operator",
        _ => kind.ToString().ToLowerInvariant(),
    };
}
