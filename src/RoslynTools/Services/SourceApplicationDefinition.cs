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
        "Reading the full source of a specific method, property, type, or field by FQN without opening the file. Returns the declaration including XML doc comments, with file path and line numbers. Prefer this over Read when you know the exact member you need.",
        Examples:
        [
            "rt inspect source RoslynTools.Services.OutlineService.BuildTypeSummary --solution RoslynTools.slnx",
            "rt inspect source RoslynTools.Core.WorkspaceLoader --solution RoslynTools.slnx",
            "rt inspect source ProjectX.Core.Services.BookingService.CreateBookingAsync --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SourceRequest),
        typeof(IReadOnlyList<SourceDeclaration>),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
