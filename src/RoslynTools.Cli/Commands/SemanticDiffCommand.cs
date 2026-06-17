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
    private readonly FileSemanticDiffSourceProvider _fileSource;
    private readonly SemanticDiffService _engine;
    private readonly SemanticDiffTextRenderer _renderer;

    public SemanticDiffCommand(
        GitSemanticDiffSourceProvider source,
        FileSemanticDiffSourceProvider fileSource,
        SemanticDiffService engine,
        SemanticDiffTextRenderer renderer)
    {
        _source = source;
        _fileSource = fileSource;
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
        + "rename into a single Renamed entry. No --solution: it parses sources directly, so it "
        + "works on an un-built or partially-broken tree. Two modes: with no file arguments it "
        + "diffs git versions (default: working tree vs HEAD; --from/--to select others); with "
        + "exactly two .cs file paths it diffs those files on disc, which needs no git repository. "
        + "Default output is a git-diff-style text report; --json emits a stable array with one "
        + "object per surfaced declaration: { fqn, kind, status (Added|Removed|Changed|Renamed|"
        + "Unchanged), oldPath, newPath, oldFqn, newFqn }. The same JSON shape is emitted in both "
        + "git and two-files modes, and the git-mode JSON is byte-identical to the MCP "
        + "semantic_diff handler (which shares the serializer).",
        Examples:
        [
            "rt analyze semantic-diff",
            "rt analyze semantic-diff --from HEAD~1 --to HEAD",
            "rt analyze sdiff --from main",
            "rt analyze semantic-diff --from v1.0 --to v1.1 --ignore-accessibility",
            "rt analyze semantic-diff --no-detect-renames",
            "rt analyze semantic-diff --from HEAD~1 --to HEAD --json",
            "rt analyze semantic-diff old/Widget.cs new/Widget.cs",
            "rt analyze sdiff before.cs after.cs --json"
        ],
        KnownIssues:
        [
            "Rename pairing is greedy within a container: two trivially identical bodies in the same "
            + "type/namespace can pair arbitrarily (deterministic by FQN order), and a rename combined "
            + "with a body change stays Added + Removed."
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
        var filesArg = new Argument<string[]>(
            "files",
            () => [],
            "Two .cs files to compare directly on disc (works outside git). Omit to diff git versions; "
            + "supply exactly two. Cannot be combined with --from/--to.")
        {
            Arity = ArgumentArity.ZeroOrMore,
        };

        var cmd = new Command(
            "semantic-diff",
            "Semantic C# diff between two git versions or two files on disc (ignores comments/whitespace, detects moves and renames)");
        cmd.AddAlias("sdiff");
        cmd.Add(filesArg);
        cmd.Add(fromOpt);
        cmd.Add(toOpt);
        cmd.Add(ignoreAccessibilityOpt);
        cmd.Add(noDetectRenamesOpt);

        cmd.SetHandler((files, from, to, ignoreAccessibility, noDetectRenames) =>
        {
            var twoFileMode = files.Length == 2;
            var (before, after) = LoadSources(files, from, to);

            var options = new SemanticDiffOptions(
                IgnoreAccessibility: ignoreAccessibility,
                DetectRenames: !noDetectRenames);

            var result = _engine.Compare(new SemanticDiffRequest(before, after, options));

            if (twoFileMode)
                result = SuppressSpuriousMoves(result);

            // Pre-render the git-diff text here (the handler owns the before/after snapshots the
            // renderer needs) and hand both projections to the shared renderer, which picks text
            // vs. the JSON delta array from the global --json flag. The text is computed even in
            // JSON mode and discarded — cheap, and it keeps SemanticDiffOutput a pure data carrier.
            var text = _renderer.Render(result, before, after);
            var output = new SemanticDiffOutput(result.Deltas, text);

            RtResultRenderer.Write(RtResult<SemanticDiffOutput>.Ok(output));
        }, filesArg, fromOpt, toOpt, ignoreAccessibilityOpt, noDetectRenamesOpt);

        return cmd;
    }

    /// <summary>
    ///     Pick the source mode from the parsed arguments and load the before/after snapshots.
    ///     No positional files → git mode (<c>--from</c>/<c>--to</c> select versions, defaulting
    ///     to working-tree-vs-HEAD). Exactly two files → two-files-on-disc mode, which never
    ///     touches git so it works outside a repository. Any other shape is a usage error and
    ///     throws <see cref="ArgumentException" />, which the CLI renders as a clean
    ///     exit-1 stderr message rather than a stack trace.
    /// </summary>
    private (IReadOnlyList<SemanticDiffSource> Before, IReadOnlyList<SemanticDiffSource> After) LoadSources(
        string[] files,
        string? from,
        string? to)
    {
        if (files.Length == 0)
            // Git mode: --from/--to map straight through to the provider, which owns the
            // HEAD-default and working-tree-vs-ref switch (do not re-implement here).
            return _source.Load(from, to);

        if (files.Length != 2)
            throw new ArgumentException(
                $"semantic-diff takes exactly two files to compare on disc (got {files.Length}); "
                + "supply both, or none to diff git versions.");

        if (from is not null || to is not null)
            throw new ArgumentException(
                "semantic-diff cannot combine two files on disc with --from/--to; "
                + "--from/--to select git versions, the file arguments compare files directly.");

        return _fileSource.Load(files[0], files[1]);
    }

    /// <summary>
    ///     Two-files mode has exactly one document per side, so a declaration present in both is
    ///     in (left, right) by construction — there is nowhere to "move" from or to. Move
    ///     detection (an Unchanged delta whose paths differ, rendered as <c>&gt; Moved</c>) is a
    ///     multi-file-changeset concept that only makes sense in git mode; here it is pure noise
    ///     driven by the two input files simply having different names. Collapse each Unchanged
    ///     delta's paths to equal so it falls out of <see cref="SemanticDiffDelta.IsSurfaced" />
    ///     and goes silent in both the text and JSON projections. Added / Removed / Changed /
    ///     Renamed deltas are left untouched, keeping their true, distinct file paths.
    /// </summary>
    private static SemanticDiffResult SuppressSpuriousMoves(SemanticDiffResult result)
    {
        var deltas = result.Deltas
            .Select(static d => d.Status == DeclarationStatus.Unchanged && d.OldPath != d.NewPath
                ? d with { OldPath = d.NewPath }
                : d)
            .ToList();

        return new SemanticDiffResult(deltas);
    }
}
