using System.CommandLine;
using RoslynTools.Cli.Suggest;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Feedback;
using RoslynTools.Services.SemanticDiff.Git;
using RoslynTools.Services.Suggest;

namespace RoslynTools.CommandDescriptors;

internal enum CommandSafetyPolicy
{
    ReadOnly,
    DryRunByDefault
}

/// <summary>
///     One authoritative description of a command leaf shared by CLI discovery and MCP.
/// </summary>
internal sealed record ApplicationCommandDescriptor(
    IReadOnlyList<string> Path,
    string ToolName,
    string Description,
    string WhenToUse,
    Func<Command> BuildCliCommand,
    bool McpVisible,
    CommandSafetyPolicy Safety,
    bool Structured,
    ICommandApplicationPort? Port);

/// <summary>
///     Projects the reflection-discovered CLI catalog and explicit meta-command leaves into
///     one descriptor registry. Operational commands are absent unless explicitly added.
/// </summary>
internal sealed class CommandDescriptorRegistry
{
    private readonly ApplicationCommandDescriptor[] _descriptors;
    private readonly Dictionary<string, ApplicationCommandDescriptor> _byToolName;

    public CommandDescriptorRegistry(
        IEnumerable<ICommandApplicationPort> ports,
        FeedbackAddOperation feedbackAdd,
        FeedbackListOperation feedbackList,
        FeedbackProcessOperation feedbackProcess,
        SuggestOperation suggest,
        IGitClient git)
    {
        var portsByName = ports.ToDictionary(p => p.CommandName, StringComparer.Ordinal);
        var descriptors = App.Commands.Select(entry =>
        {
            var toolName = NormalizeToolName(entry.Info.Name);
            portsByName.TryGetValue(toolName, out var port);
            return new ApplicationCommandDescriptor(
                [entry.Info.Category, entry.Info.Name],
                toolName,
                entry.Build().Description ?? entry.Info.WhenToUse,
                entry.Info.WhenToUse,
                entry.Build,
                McpVisible: true,
                entry.Info.Mutates
                    ? CommandSafetyPolicy.DryRunByDefault
                    : CommandSafetyPolicy.ReadOnly,
                entry.Info.JsonOutput
                || entry.Info.SupportedFormats?.Contains(OutputFormat.Json) == true
                || port is not null,
                port);
        }).ToList();

        descriptors.Add(new ApplicationCommandDescriptor(
            ["feedback", "add"],
            "feedback_add",
            "Record rt dogfood feedback, a bug, a missing feature, or unexpected command behavior.",
            "Call this when you hit friction using rt and want to log it for later triage — records a bug, missing feature, or unexpected behavior and returns the allocated feedback ID.",
            () => FeedbackCommand.BuildAdd(feedbackAdd),
            McpVisible: true,
            CommandSafetyPolicy.ReadOnly,
            Structured: true,
            feedbackAdd));
        descriptors.Add(new ApplicationCommandDescriptor(
            ["feedback", "list"],
            "feedback_list",
            "List rt feedback as structured entries, filtered by pending, processed, or all.",
            "Call this when you want to review previously logged rt feedback — lists pending or processed entries as structured JSON.",
            () => FeedbackCommand.BuildList(feedbackList),
            McpVisible: true,
            CommandSafetyPolicy.ReadOnly,
            Structured: true,
            feedbackList));
        descriptors.Add(new ApplicationCommandDescriptor(
            ["feedback", "process"],
            "feedback_process",
            "Mark an rt feedback item as processed with a resolution.",
            "Call this when a pending feedback item has been verified and resolved — marks it processed so pending work only contains unresolved items.",
            () => FeedbackCommand.BuildProcess(feedbackProcess),
            McpVisible: true,
            CommandSafetyPolicy.ReadOnly,
            Structured: true,
            feedbackProcess));
        descriptors.Add(new ApplicationCommandDescriptor(
            ["suggest"],
            "suggest_command",
            "Find the right rt command by describing what you want to do in natural language.",
            "Call this when you know what you want to do to C# code but aren't sure which rt command does it — describe the task in natural language and get ranked candidates back before falling back to a manual edit. Returns ranked candidates with name, score, when-to-use, and example invocation.",
            () => SuggestCommand.Build(suggest, git),
            McpVisible: true,
            CommandSafetyPolicy.ReadOnly,
            Structured: true,
            suggest));

        _descriptors = [.. descriptors];
        _byToolName = _descriptors.ToDictionary(d => d.ToolName, StringComparer.Ordinal);
    }

    public IReadOnlyList<ApplicationCommandDescriptor> All => _descriptors;

    public bool TryGetByToolName(
        string toolName,
        out ApplicationCommandDescriptor descriptor)
    {
        return _byToolName.TryGetValue(toolName, out descriptor!);
    }

    private static string NormalizeToolName(string commandName) =>
        commandName.Replace('-', '_');
}
