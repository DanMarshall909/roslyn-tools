using System.CommandLine;
using RoslynTools.Cli.Prime;
using RoslynTools.Cli.Suggest;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Core.Output;
using RoslynTools.Feedback;
using RoslynTools.Services;
using RoslynTools.Services.SemanticDiff.Git;
using RoslynTools.Services.Suggest;

namespace RoslynTools.CommandDescriptors;

/// <summary>
///     Projects CLI commands into host-neutral application descriptors. CLI command factories
///     stay private to this adapter so shared descriptors contain no parser concepts.
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
        FeedbackReportOperation feedbackReport,
        SuggestOperation suggest,
        PrimeOperation prime,
        IGitClient git)
    {
        var portsByName = ports.ToDictionary(
            port => port.Definition.Name,
            StringComparer.Ordinal);
        var descriptors = App.AllCommands.Select(entry =>
        {
            var toolName = NormalizeToolName(entry.Info.Name);
            portsByName.TryGetValue(toolName, out var port);
            var applicationDefinition = port is null or ILegacyCommandApplicationPort
                ? null
                : port.Definition;
            var daemonDefinition = DaemonOwnedCommands.All.SingleOrDefault(
                definition => string.Equals(definition.Name, toolName, StringComparison.Ordinal));
            var info = applicationDefinition?.Info ?? entry.Info;
            var command = entry.Build();
            var audience = applicationDefinition?.Product.Audience
                ?? (App.RemovedPublicCommandNames.Contains(info.Name)
                ? CommandAudience.Removed
                : App.TrustedExperimentalCommandNames.Contains(info.Name)
                    ? CommandAudience.TrustedExperimental
                    : CommandAudience.Public);
            var effects = applicationDefinition?.Product.Effects
                ?? (string.Equals(info.Name, "probe-di", StringComparison.Ordinal)
                    ? CommandEffects.RepositoryRead | CommandEffects.RepositoryCodeExecution
                    : info.Mutates
                    ? CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite
                    : CommandEffects.RepositoryRead);
            return new ApplicationCommandDescriptor(
                audience == CommandAudience.TrustedExperimental
                    ? ["trusted", info.Name]
                    : [info.Category, info.Name],
                applicationDefinition?.Name ?? toolName,
                applicationDefinition?.Info.WhenToUse
                ?? command.Description
                ?? entry.Info.WhenToUse,
                info.WhenToUse,
                DescribeInput(command),
                (applicationDefinition?.McpVisible ?? !entry.Info.Mutates)
                && audience == CommandAudience.Public,
                ToDescriptorSafety(
                    applicationDefinition?.Safety ?? daemonDefinition?.Safety,
                    info.Mutates),
                info.JsonOutput
                || info.SupportedFormats?.Contains(OutputFormat.Json) == true
                || port is not null,
                port,
                applicationDefinition,
                effects,
                audience);
        }).ToList();

        AddMetaCommand(
            "feedback_add",
            () => FeedbackCommand.BuildAdd(feedbackAdd),
            ["maintainer", "feedback", "add"],
            "Record rt dogfood feedback, a bug, a missing feature, or unexpected command behavior.",
            "Call this when you hit friction using rt and want to log it for later triage — records a bug, missing feature, or unexpected behavior and returns the allocated feedback ID.",
            CommandSafetyPolicy.LocalWrite,
            true,
            feedbackAdd,
            CommandEffects.LocalStateWrite);
        AddMetaCommand(
            "feedback_list",
            () => FeedbackCommand.BuildList(feedbackList),
            ["maintainer", "feedback", "list"],
            "List rt feedback as structured entries, filtered by pending, processed, or all.",
            "Call this when you want to review previously logged rt feedback — lists pending or processed entries as structured JSON.",
            CommandSafetyPolicy.ReadOnly,
            true,
            feedbackList,
            CommandEffects.LocalStateRead);
        AddMetaCommand(
            "feedback_process",
            () => FeedbackCommand.BuildProcess(feedbackProcess),
            ["maintainer", "feedback", "process"],
            "Mark an rt feedback item as processed with a resolution.",
            "Call this when a pending feedback item has been verified and resolved — marks it processed so pending work only contains unresolved items.",
            CommandSafetyPolicy.LocalWrite,
            true,
            feedbackProcess,
            CommandEffects.LocalStateRead | CommandEffects.LocalStateWrite);
        AddMetaCommand(
            "feedback_report",
            () => FeedbackCommand.BuildReport(feedbackReport),
            ["maintainer", "feedback", "report"],
            "Review duplicate GitHub issues, then explicitly link feedback or create an issue.",
            "Call this when a human explicitly asks to promote locally recorded rt feedback to GitHub — it previews by default and requires an explicit apply choice before changing GitHub.",
            CommandSafetyPolicy.ExternalWriteReviewByDefault,
            true,
            feedbackReport,
            CommandEffects.LocalStateRead
            | CommandEffects.LocalStateWrite
            | CommandEffects.ExternalRead
            | CommandEffects.ExternalWrite);
        AddMetaCommand(
            "suggest_command",
            () => SuggestCommand.Build(suggest, git),
            ["suggest"],
            "Find the right rt command by describing what you want to do in natural language.",
            "Call this when you know what you want to do to C# code but aren't sure which rt command does it — describe the task in natural language and get ranked candidates back before falling back to a manual edit. Returns ranked candidates with name, score, when-to-use, and example invocation.",
            CommandSafetyPolicy.ReadOnly,
            true,
            suggest,
            CommandEffects.RepositoryRead,
            CommandAudience.Public);
        AddMetaCommand(
            "prime",
            () => PrimeCommand.Build(prime),
            ["prime"],
            "Return the compact rt session primer without loading a solution.",
            PrimeCommandDefinition.Info.WhenToUse,
            CommandSafetyPolicy.ReadOnly,
            true,
            prime,
            CommandEffects.None,
            CommandAudience.Public);

        _descriptors = [.. descriptors];
        _byToolName = _descriptors.ToDictionary(d => d.ToolName, StringComparer.Ordinal);

        void AddMetaCommand(
            string toolName,
            Func<Command> build,
            IReadOnlyList<string> path,
            string description,
            string whenToUse,
            CommandSafetyPolicy safety,
            bool structured,
            ICommandApplicationPort port,
            CommandEffects effects,
            CommandAudience audience = CommandAudience.Maintainer)
        {
            var command = build();
            var definition = port is ILegacyCommandApplicationPort
                ? null
                : port.Definition;
            descriptors.Add(new ApplicationCommandDescriptor(
                path,
                toolName,
                description,
                whenToUse,
                DescribeInput(command),
                McpVisible: definition?.McpVisible ?? audience == CommandAudience.Public,
                safety,
                structured,
                port,
                definition,
                definition?.Product.Effects ?? effects,
                definition?.Product.Audience ?? audience));
        }
    }

    private static CommandSafetyPolicy ToDescriptorSafety(
        CommandSafety? daemonSafety,
        bool commandMutates)
    {
        // The descriptor bridge still supports only the legacy two-state projection. The core
        // contract retains whether a mutation is apply- or review-default for daemon adapters.
        return daemonSafety?.Effect == CommandEffect.ReadOnly
            ? CommandSafetyPolicy.ReadOnly
            : commandMutates || daemonSafety?.Effect == CommandEffect.Mutation
                ? CommandSafetyPolicy.DryRunByDefault
                : CommandSafetyPolicy.ReadOnly;
    }

    public IReadOnlyList<ApplicationCommandDescriptor> All => _descriptors;

    public bool TryGetByToolName(
        string toolName,
        out ApplicationCommandDescriptor descriptor)
    {
        return _byToolName.TryGetValue(toolName, out descriptor!);
    }

    private static ApplicationCommandInput DescribeInput(Command command)
    {
        return new ApplicationCommandInput(
            command.Arguments.Select(argument => new ApplicationCommandParameter(
                argument.Name,
                FriendlyTypeName(argument.ValueType),
                argument.Arity.MinimumNumberOfValues > 0,
                argument.Description)).ToList(),
            command.Options.Select(option => new ApplicationCommandParameter(
                option.Name,
                FriendlyTypeName(option.ValueType),
                option.IsRequired,
                option.Description)).ToList());
    }

    private static string FriendlyTypeName(Type type)
    {
        var valueType = Nullable.GetUnderlyingType(type) ?? type;
        if (valueType == typeof(bool)) return "boolean";
        if (valueType == typeof(int) || valueType == typeof(long)) return "integer";
        if (valueType.IsArray) return FriendlyTypeName(valueType.GetElementType()!) + "[]";
        return "string";
    }

    private static string NormalizeToolName(string commandName) =>
        commandName.Replace('-', '_');
}
