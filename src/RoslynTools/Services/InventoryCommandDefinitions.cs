using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal static class FindUnusedCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "find-unused", false, false, "inspect", Aliases: ["unused"],
        WhenToUse: "Identifying dead code — types and methods with zero references in the solution — before a safe-delete pass or ahead of a major refactor. Use --scope to focus on a specific namespace and filter noise from generated or test code.",
        Examples: [
            "rt inspect find-unused --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect unused --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-unused --scope types --solution RoslynTools.slnx"],
        SeeAlso: ["safe-delete", "find-refs"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
    internal static ApplicationCommandDefinition Definition { get; } = new(Info, typeof(FindUnusedRequest), typeof(Results.FindUnusedAnalysisResult), true, CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, [new("safe_delete", "Use after reviewing high-confidence unused findings.")]);
}
internal static class FindUndocumentedCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "find-undocumented", false, false, "inspect", Aliases: ["undoc"],
        WhenToUse: "Finding types and members that have no XML documentation comment — the first step before bulk-generating docs with an LLM. Use --members to include methods and properties, not just types. Use --level to control which accessibility levels are checked (default: public).",
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
        "Planning a static-to-instance migration by listing every static class with its call-site count, member count, and inbound static coupling. Results are sorted leaf-first so the safest classes to de-static appear at the top.",
        [
            "rt analyze static-class-inventory --solution RoslynTools.slnx",
            "rt analyze static-class-inventory --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt static-class-inventory --solution RoslynTools.slnx --json"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);
    internal static ApplicationCommandDefinition Definition { get; } = new(Info, typeof(StaticClassInventoryRequest), typeof(Results.StaticClassInventoryAnalysisResult), true, CommandSafety.ReadOnly, CommandExecutionPlacement.DaemonPreferred, []);
}
