using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools;

/// <summary>
/// Metadata for CLI-owned commands that have no typed application port.
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

    internal static RtCommandInfo Feedback { get; } = new(
        "feedback",
        true,
        true,
        "meta",
        "Recording rt dogfood feedback, bugs, missing features, and unexpected command behavior. "
        + "Use feedback add to create an item or feedback list to review existing items.",
        [
            "rt feedback add --description \"rename should also preview related symbols\"",
            "rt feedback list --status pending"
        ]);
}
