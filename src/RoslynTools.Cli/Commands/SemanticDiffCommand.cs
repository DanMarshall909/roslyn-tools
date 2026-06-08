using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.SemanticDiff;

namespace RoslynTools.Commands;

/// <summary>
///     Thin host adapter for <c>rt analyze semantic-diff</c>: loads two source snapshots
///     from git via <see cref="GitSemanticDiffSourceProvider" />, runs the host-agnostic
///     <see cref="SemanticDiffService" /> engine, and projects the per-declaration deltas as
///     a <see cref="SemanticDiffOutput" />. All three collaborators are constructor-injected
///     from DI; the command owns no comparison logic.
///     <para>
///         Output shape follows the global <c>--json</c> flag via the shared renderer: the
///         default is the git-diff-style text report built by <see cref="SemanticDiffTextRenderer" />,
///         and <c>--json</c> emits a JSON array of deltas (<see cref="RtCommandInfo.JsonOutput" />
///         is <c>true</c> and the command implements <see cref="IStructuredOutputCommand" /> — the
///         two move together to satisfy the <c>App</c> startup biconditional).
///     </para>
/// </summary>
internal sealed class SemanticDiffCommand : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
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
        true,
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
            "rt analyze semantic-diff --no-detect-renames",
            "rt analyze semantic-diff --from HEAD~1 --to HEAD --json"
        ],
        KnownIssues:
        [
            "Rename pairing is per-container and greedy: two trivially identical bodies can mis-pair, "
            + "and a rename combined with a body change stays Added + Removed (tracked in US-126)."
        ],
        // Text (the git-diff report) and Json (the delta array). The shared renderer selects
        // between them from the global --json flag; both projections live on SemanticDiffOutput.
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

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

            // Pre-render the git-diff text here (the handler owns the before/after snapshots the
            // renderer needs) and hand both projections to the shared renderer, which picks text
            // vs. the JSON delta array from the global --json flag. The text is computed even in
            // JSON mode and discarded — cheap, and it keeps SemanticDiffOutput a pure data carrier.
            var text = _renderer.Render(result, before, after);
            var output = new SemanticDiffOutput(result.Deltas, text);

            RtResultRenderer.WriteOutput(RtResult<SemanticDiffOutput>.Ok(output));
        }, fromOpt, toOpt, ignoreAccessibilityOpt, noDetectRenamesOpt);

        return cmd;
    }
}
