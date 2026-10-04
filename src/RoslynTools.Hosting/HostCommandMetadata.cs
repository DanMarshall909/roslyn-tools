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
        "Diagnose an invalid rt configuration file or inspect effective host configuration without a solution or repository trust.",
        ["rt config", "rt config --json"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json],
        SeeAlso: ["doctor", "trust"]);

    internal static CommandProductMetadata ConfigProduct { get; } = new(
        CapabilityMaturity.Preview,
        CommandEffects.RepositoryRead | CommandEffects.LocalStateRead,
        ConditionallyMutable: false,
        CommandIdempotency.Idempotent,
        CommandAudience.Public,
        TrustRequirement.None,
        WorkspaceRequirement.None,
        Prerequisites: [],
        CommandCost.Low,
        CancellationSupport.NotSupported,
        new CommandResourceLimits(30_000, 100, 1_048_576),
        Limitations:
        [
            "CLI config inspection does not yet enforce the advertised timeout, item count, or output byte limit."
        ],
        Config.Examples);

    internal static RtCommandInfo Trust { get; } = new(
        "trust",
        Mutates: false,
        JsonOutput: true,
        Category: "control",
        "Use this when inspecting or changing user-scoped trust for a reviewed Git repository. "
        + "The explicit add/remove verb is the local decision; list/status inspect trust.",
        [
            "rt trust add .",
            "rt trust list",
            "rt trust status .",
            "rt trust remove ."
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json],
        SeeAlso: ["doctor"],
        Placement: CommandPlacement.RootOnly);

    internal static CommandProductMetadata TrustProduct { get; } = new(
        CapabilityMaturity.Preview,
        CommandEffects.RepositoryRead | CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite,
        ConditionallyMutable: true,
        CommandIdempotency.Conditional,
        CommandAudience.Public,
        TrustRequirement.None,
        WorkspaceRequirement.None,
        Prerequisites: [],
        CommandCost.Low,
        CancellationSupport.NotSupported,
        new CommandResourceLimits(30_000, 1_000, 1_048_576),
        Limitations:
        [
            "Add and status require a Git worktree; list and removal by stored repository id do not.",
            "Cancellation is checked before registry work; synchronous registry and Git operations cannot be cancelled.",
            "CLI trust commands do not yet enforce the advertised timeout, item count, or output byte limit.",
            "Preview trust storage does not yet implement ADR-0011 leases, audit history, quotas, or evaluator isolation."
        ],
        Trust.Examples);

    internal static RtCommandInfo SemanticDiff { get; } = new(
        "semantic-diff",
        false,
        true,
        "analyze",
        Aliases: ["sdiff"],
        WhenToUse:
        "Compare C# declarations between Git refs such as main and HEAD or between source files. "
        + "Working-tree comparison requires Linux x64; on other platforms use two Git refs "
        + "(--from and --to) or two .cs files. Reviewing what *semantically* changed in C# "
        + "between two states. Prefer this over git diff: "
        + "it ignores comments and whitespace, treats a declaration moved to another file as "
        + "unchanged, and pairs a pure rename into a single Renamed entry. No --solution: it parses sources directly, so it "
        + "works on an un-built or partially-broken tree. Two modes: with no file arguments it "
        + "diffs git versions (default: working tree vs HEAD; use --from alone "
        + "for a non-HEAD working-tree baseline, or --from and --to for two refs); with "
        + "exactly two .cs file paths it diffs those files on disc, which needs no git repository. "
        + "Default output is a git-diff-style text report; --json emits a stable array with one "
        + "object per surfaced declaration: { fqn, kind, status (Added|Removed|Changed|Renamed|"
        + "Unchanged), oldPath, newPath, oldFqn, newFqn }. The same JSON shape is emitted in both "
        + "git and two-files modes, and the git-mode JSON is byte-identical to the MCP "
        + "semantic_diff handler (which shares the serializer).",
        Examples:
        [
            "rt analyze semantic-diff --from HEAD~1 --to HEAD",
            "rt analyze semantic-diff",
            "rt analyze sdiff --from main --to HEAD",
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
            + "with a body change stays Added + Removed.",
            "Working-tree comparison includes committed changes since --from, staged changes, "
            + "unstaged changes, and untracked files; it is not an unstaged-only comparison."
        ],
        // Text (the git-diff report) and Json (the delta array). The shared renderer selects
        // between them from the global --json flag; both projections live on SemanticDiffOutput.
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static RtCommandInfo Feedback { get; } = new(
        "feedback",
        true,
        true,
        "meta",
        "Record feedback about an rt tool problem: dogfood bugs, missing features, and unexpected command behavior. "
        + "Use rt maintainer feedback add to create an item or rt maintainer feedback list to review existing items.",
        [
            "rt maintainer feedback add --description \"rename should also preview related symbols\"",
            "rt maintainer feedback list --status pending"
        ]);
}
