using Microsoft.Extensions.DependencyInjection;
using RoslynTools.CommandDescriptors;

namespace RoslynTools.Tests.Mcp.Architecture;

/// <summary>
///     Locks the <em>triggering</em> contract for MCP-visible commands: a command's
///     <see cref="ApplicationCommandDescriptor.WhenToUse" /> — the one field that becomes
///     both the CLI <c>--help</c> "When to use" text and the MCP tool description
///     (<see cref="RoslynTools.Mcp.Tools.ToolSchemaBuilder.Build" />) — must say
///     <b>when</b> to reach for the tool, not only what it does.
///     <para>
///         <b>Why this is a test and not a review note:</b> recent Claude models are
///         deliberately conservative about invoking custom and MCP tools — they will not
///         reach for one unless reasonably sure it is needed. Anthropic's guidance is that
///         prescriptive descriptions stating the trigger condition give measurable lift
///         over descriptions that only state capability, and that the lever belongs on
///         each tool's own description. A capability-only description is therefore not a
///         style problem; it is a tool that silently never gets called, which looks
///         identical to the tool not being installed.
///     </para>
///     <para>
///         Scoped to commands with a typed <see cref="ApplicationCommandDescriptor.Port" />
///         (find_refs, outline, describe, rename, semantic_diff, suggest_command, and the
///         feedback trio) — the commands that received bespoke MCP treatment, the modern
///         equivalent of what used to be a curated set of native <c>IMcpHandler</c>
///         instances before the CLI/MCP descriptor unification. Every other command
///         replays generic CLI-argument schemas and is out of scope here.
///     </para>
///     <para>
///         Both checks are deliberately <em>mechanical proxies</em> for a semantic
///         property. They cannot prove a description is well-written; they catch the
///         regression that actually happens — a new command shipped with a bare "Does X."
///         WhenToUse. Widen <see cref="TriggerMarkers" /> when a genuinely different
///         phrasing appears; do not weaken it to make a lazy description pass.
///     </para>
/// </summary>
public sealed class CommandDescriptorTriggerContract
{
    /// <summary>
    ///     Phrasings that introduce a trigger condition. Matched case-insensitively as
    ///     substrings. This vocabulary is the maintained part of the check — extend it
    ///     when a new description states its trigger a way not listed here.
    /// </summary>
    private static readonly string[] TriggerMarkers =
    [
        "use this when",
        "use when",
        "call this when",
        "reach for this",
        "prefer this",
        "instead of",
        "rather than",
        "when you",
        "when the"
    ];

    /// <summary>
    ///     Port-backed commands whose job overlaps a tool the agent already has, mapped to
    ///     the name of that built-in. Overlap is the case where triggering fails silently:
    ///     the agent reaches for the familiar general tool and never learns the specialised
    ///     one would have been correct. Naming the built-in in WhenToUse is what makes the
    ///     choice explicit at the point of decision.
    /// </summary>
    private static readonly Dictionary<string, string> BuiltInOverlaps = new(StringComparer.Ordinal)
    {
        ["find_refs"] = "Grep",
        ["outline"] = "Read",
        ["describe"] = "Read",
        ["rename"] = "Edit",
        ["semantic_diff"] = "git diff"
    };

    /// <summary>
    ///     Documented exceptions to <see cref="BuiltInOverlaps" />: port-backed commands
    ///     with no built-in counterpart to disambiguate against. Each entry MUST say why,
    ///     so a future reader can tell a deliberate omission from an oversight.
    /// </summary>
    private static readonly HashSet<string> KnownNonOverlappingCommands = new(StringComparer.Ordinal)
    {
        "suggest_command", // Meta/discovery over the rt catalog itself; no built-in does this.
        "feedback_add", // rt's own dogfood-feedback loop; no built-in tracks it.
        "feedback_list",
        "feedback_process"
    };

    [Fact]
    public void with_every_port_backed_descriptor_the_when_to_use_states_a_trigger()
    {
        var offenders = ResolveDescriptors()
            .Where(d => !StatesATrigger(d.WhenToUse))
            .Select(d => d.ToolName)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Every port-backed command's WhenToUse must state when to reach for the tool, not "
            + "only what it does — a capability-only description leaves the agent to guess, and "
            + "it guesses the built-in. Add a trigger clause using one of the documented "
            + $"phrasings ({string.Join(", ", TriggerMarkers.Select(m => $"\"{m}\""))}). "
            + $"Capability-only ({offenders.Count}): {string.Join(", ", offenders)}");
    }

    [Fact]
    public void with_every_overlapping_descriptor_the_when_to_use_names_the_builtin_it_replaces()
    {
        var offenders = ResolveDescriptors()
            .Where(d => !KnownNonOverlappingCommands.Contains(d.ToolName))
            .Where(d => BuiltInOverlaps.TryGetValue(d.ToolName, out var builtIn)
                        && !d.WhenToUse.Contains(builtIn, StringComparison.OrdinalIgnoreCase))
            .Select(d => $"{d.ToolName} (must name '{BuiltInOverlaps[d.ToolName]}')")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A command that overlaps a built-in agent tool must name that built-in in its "
            + "WhenToUse, so the agent can tell which one applies at the moment it chooses. "
            + "Without it the general tool wins by familiarity and the specialised one is never "
            + $"tried. Missing ({offenders.Count}): {string.Join("; ", offenders)}");
    }

    [Fact]
    public void with_every_port_backed_descriptor_an_entry_exists_in_the_overlap_map()
    {
        // Guards the guard: a new port-backed command must be classified as either
        // overlapping a built-in or documented as not overlapping. Without this, adding one
        // silently opts it out of the disambiguation check above.
        var unclassified = ResolveDescriptors()
            .Select(d => d.ToolName)
            .Where(n => !BuiltInOverlaps.ContainsKey(n) && !KnownNonOverlappingCommands.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            unclassified.Count == 0,
            "Every port-backed command must be classified: either map it to the built-in it "
            + "overlaps in BuiltInOverlaps, or document why it has no counterpart in "
            + $"KnownNonOverlappingCommands. Unclassified ({unclassified.Count}): "
            + string.Join(", ", unclassified));
    }

    private static bool StatesATrigger(string whenToUse) =>
        TriggerMarkers.Any(marker => whenToUse.Contains(marker, StringComparison.OrdinalIgnoreCase));

    private static List<ApplicationCommandDescriptor> ResolveDescriptors()
    {
        using var provider = McpTestHost.CreateProvider();
        var descriptors = provider.GetRequiredService<CommandDescriptorRegistry>()
            .All.Where(d => d.Port is not null)
            .ToList();

        Assert.True(descriptors.Count > 0, "Expected at least one port-backed command descriptor.");
        return descriptors;
    }
}
