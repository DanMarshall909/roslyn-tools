using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal static class DescribeApplicationDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "describe",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Getting a type's surface area as a compact JSON signature array { fqn, kind, signatures: [...] } — one canonical signature per member, no line numbers (stable across edits). Prefer this over Read when you need one type's or member's signature rather than the whole file. Drill into a single member by passing its signature ('Type.Method(paramType, ...)') for full detail. Add --with-locations for the legacy verbose shape (per-member line numbers + locations).",
        Examples:
        [
            "rt inspect describe BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect describe \"RoslynTools.Core.SymbolFormatter.ExtractMembers(INamedTypeSymbol, bool)\" --solution RoslynTools.slnx",
            "rt inspect describe IRtCommand --solution RoslynTools.slnx --with-locations"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(DescribeRequest),
        typeof(DescribeResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
