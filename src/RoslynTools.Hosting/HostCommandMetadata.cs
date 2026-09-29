using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools;

/// <summary>
/// Shared metadata for CLI discovery and suggestions where typed ports do not own the CLI projection.
/// </summary>
internal static class HostCommandMetadata
{
    internal static RtCommandInfo Config { get; } = new(
        "config",
        Mutates: false,
        JsonOutput: true,
        Category: "inspect",
        "Inspecting effective global and worktree host configuration or diagnosing an invalid file.",
        ["rt config", "rt config --json"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json],
        SeeAlso: ["doctor", "trust"]);

    internal static RtCommandInfo Trust { get; } = new(
        "trust",
        Mutates: true,
        JsonOutput: true,
        Category: "refactor",
        "Managing preview user-scoped trust for a reviewed Git repository. "
        + "Use trust add/remove only as an explicit local user decision; use list/status to inspect it.",
        [
            "rt trust add .",
            "rt trust list",
            "rt trust status .",
            "rt trust remove ."
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json],
        SeeAlso: ["doctor"]);

    internal static RtCommandInfo SemanticDiff { get; } = new(
        "semantic-diff",
        false,
        true,
        "analyze",
        Aliases: ["sdiff"],
        WhenToUse:
        "Reviewing what *semantically* changed in C# between two states. Prefer this over git diff: "
        + "it ignores comments and whitespace, treats a declaration moved to another file as "
        + "unchanged, and pairs a pure rename into a single Renamed entry. No --solution: it parses sources directly, so it "
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

    internal static RtCommandInfo Feedback { get; } = new(
        "feedback",
        true,
        true,
        "meta",
        "Recording rt dogfood feedback, bugs, missing features, and unexpected command behavior. "
        + "Use rt maintainer feedback add to create an item or rt maintainer feedback list to review existing items.",
        [
            "rt maintainer feedback add --description \"rename should also preview related symbols\"",
            "rt maintainer feedback list --status pending"
        ]);
}
