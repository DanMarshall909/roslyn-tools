using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticDiff;

namespace RoslynTools.Commands;

/// <summary>
///     Thin host adapter for <c>rt analyze semantic-diff</c>: loads two source snapshots
///     from git via <see cref="GitSemanticDiffSourceProvider" />, runs the host-agnostic
///     <see cref="SemanticDiffService" /> engine, and renders the per-declaration deltas as
///     a git-diff-style text report with <see cref="SemanticDiffTextRenderer" />. All three
///     collaborators are constructor-injected from DI; the command owns no comparison logic.
///     <para>
///         TEXT output only in US-122 (<see cref="RtCommandInfo.JsonOutput" /> is
///         <c>false</c> and the command deliberately does <i>not</i> implement
///         <c>IStructuredOutputCommand</c>); a native-JSON shape arrives in US-124, which
///         flips both together.
///     </para>
/// </summary>
internal sealed class SemanticDiffCommand : IRtCommand, IAnalyzeCommand
{
    private readonly GitSemanticDiffSourceProvider _source;
    private readonly SemanticDiffService _engine;
    private readonly SemanticDiffTextRenderer _renderer;

    public SemanticDiffCommand(
        GitSemanticDiffSourceProvider source,
        SemanticDiffService engine,
        SemanticDiffTextRenderer renderer)
    {
        _source = source;
        _engine = engine;
        _renderer = renderer;
    }

    public static RtCommandInfo Info { get; } = new(
        "semantic-diff",
        false,
        false,
        "analyze",
        Aliases: ["sdiff"],
        WhenToUse:
        "Reviewing what *semantically* changed in C# between two states — ignores comments and "
        + "whitespace, treats a declaration moved to another file as unchanged, and pairs a pure "
        + "rename into a single Renamed entry. No --solution: it parses git versions directly, so "
        + "it works on an un-built or partially-broken tree. Default compares the working tree "
        + "against HEAD; --from/--to select other versions.",
        Examples:
        [
            "rt analyze semantic-diff",
            "rt analyze semantic-diff --from HEAD~1 --to HEAD",
            "rt analyze sdiff --from main",
            "rt analyze semantic-diff --from v1.0 --to v1.1 --ignore-accessibility",
            "rt analyze semantic-diff --no-detect-renames"
        ],
        KnownIssues:
        [
            "Rename pairing is per-container and greedy: two trivially identical bodies can mis-pair, "
            + "and a rename combined with a body change stays Added + Removed (tracked in US-126)."
        ],
        // Text is the only shape in US-122. A native structured JSON delta — and the matching
        // JsonOutput=true + IStructuredOutputCommand pair the App.cs biconditional requires —
        // arrives in US-124. Declaring Json here would advertise a format the handler does not
        // actually emit (the handler always renders text via WriteText).
        SupportedFormats: [OutputFormat.Text]);

    public Command Build()
    {
        var fromOpt = new Option<string?>(
            "--from",
            "Git ref for the 'before' side. Omit to default to HEAD.");
        var toOpt = new Option<string?>(
            "--to",
            "Git ref for the 'after' side. Omit to compare against the working tree.");
        var ignoreAccessibilityOpt = new Option<bool>(
            "--ignore-accessibility",
            "Treat declarations that differ only in accessibility modifiers as unchanged.");
        var noDetectRenamesOpt = new Option<bool>(
            "--no-detect-renames",
            "Report a pure rename as a separate Added + Removed pair instead of one Renamed entry.");

        var cmd = new Command(
            "semantic-diff",
            "Semantic C# diff between two git versions (ignores comments/whitespace, detects moves and renames)");
        cmd.AddAlias("sdiff");
        cmd.Add(fromOpt);
        cmd.Add(toOpt);
        cmd.Add(ignoreAccessibilityOpt);
        cmd.Add(noDetectRenamesOpt);

        cmd.SetHandler((from, to, ignoreAccessibility, noDetectRenames) =>
        {
            // --from/--to map straight through to the provider, which owns the
            // HEAD-default and working-tree-vs-ref switch (do not re-implement here).
            var (before, after) = _source.Load(from, to);

            var options = new SemanticDiffOptions(
                IgnoreAccessibility: ignoreAccessibility,
                DetectRenames: !noDetectRenames);

            var result = _engine.Compare(new SemanticDiffRequest(before, after, options));
            var text = _renderer.Render(result, before, after);

            // Route through the shared renderer (not RtConsole directly) so the result
            // contract stays uniform across CLI/MCP/daemon; WriteText emits the raw string.
            RtResultRenderer.WriteText(RtResult<string>.Ok(text));
        }, fromOpt, toOpt, ignoreAccessibilityOpt, noDetectRenamesOpt);

        return cmd;
    }
}
