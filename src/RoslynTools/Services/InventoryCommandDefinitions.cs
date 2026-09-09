using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal static class FindUnusedCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "find-unused", false, false, "inspect", Aliases: ["unused"],
        WhenToUse: "Use this instead of Grep when identifying possible dead code. Findings are candidates within the loaded solution, not proof about reflection, runtime activation, generated use, or external consumers. Use --scope to select all, types, or methods. Unverified workspace acquisition produces advisory findings without deletion suggestions.",
        Examples: [
            "rt inspect find-unused --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect unused --scope methods --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-unused --scope types --solution RoslynTools.slnx"],
        SeeAlso: ["delete-unreferenced", "relationships"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
    internal static ApplicationCommandDefinition Definition { get; } = new(Info, typeof(FindUnusedRequest), typeof(RtResultEnvelope<Results.FindUnusedAnalysisResult>), true, CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, [new("delete_unreferenced", "Use only after reviewing a high-confidence candidate from a complete workspace.")]);
}
internal static class FindUndocumentedCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "find-undocumented", false, false, "inspect", Aliases: ["undoc"],
        WhenToUse: "Use this instead of Grep when finding types and members that have no XML documentation comment — the first step before bulk-generating docs with an LLM. Use --members to include methods and properties, not just types. Use --level to control which accessibility levels are checked (default: public).",
        Examples: [
            "rt inspect find-undocumented --solution RoslynTools.slnx",
            "rt inspect undoc --members --level internal --solution RoslynTools.slnx",
            "rt inspect undoc --members --level public,internal --scope RoslynTools.Core --solution RoslynTools.slnx",
            "rt inspect find-undocumented --level all --json --solution RoslynTools.slnx"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
    internal static ApplicationCommandDefinition Definition { get; } = new(Info, typeof(FindUndocumentedRequest), typeof(Results.FindUndocumentedAnalysisResult), true, CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, []);
}
internal static class StaticClassInventoryCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "static-class-inventory", false, true, "analyze",
        "Use this instead of Read when planning a static-to-instance migration by listing every static class with its call-site count, member count, and inbound static coupling. Results are sorted leaf-first so the safest classes to de-static appear at the top.",
        [
            "rt analyze static-class-inventory --solution RoslynTools.slnx",
            "rt analyze static-class-inventory --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt static-class-inventory --solution RoslynTools.slnx --json"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
    internal static ApplicationCommandDefinition Definition { get; } = new(Info, typeof(StaticClassInventoryRequest), typeof(Results.StaticClassInventoryAnalysisResult), true, CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, []);
}
