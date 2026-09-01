using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal static class SourceApplicationDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "source",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Reading the bounded source of a specific method, property, type, or field by FQN without opening the file. Returns the declaration including XML doc comments, then lists source symbols referenced by its signature or body. Use include-related to retrieve those one-hop declarations too, and continue an explicitly truncated result with its revision-bound token. Prefer this over Read when you know the exact member you need.",
        Examples:
        [
            "rt inspect source RoslynTools.Services.OutlineService.BuildTypeSummary --solution RoslynTools.slnx",
            "rt inspect source RoslynTools.Core.WorkspaceLoader --solution RoslynTools.slnx",
            "rt inspect source RoslynTools.Services.SourceOperation.ExecuteAsync --include-related --related-limit 5 --solution RoslynTools.slnx",
            "rt inspect source RoslynTools.Services.SourceOperation.ExecuteAsync --max-source-bytes 16384 --solution RoslynTools.slnx",
            "rt inspect source ProjectX.Core.Services.BookingService.CreateBookingAsync --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SourceRequest),
        typeof(SourceResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
