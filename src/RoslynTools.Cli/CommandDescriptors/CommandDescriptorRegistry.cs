using System.CommandLine;
using RoslynTools.Cli.Prime;
using RoslynTools.Cli.Prepare;
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
        ApplicationCommandCatalog catalogue,
        FeedbackAddOperation feedbackAdd,
        FeedbackListOperation feedbackList,
        FeedbackProcessOperation feedbackProcess,
        FeedbackReportOperation feedbackReport,
        SuggestOperation suggest,
        PrimeOperation prime,
        DurablePrepareOperation durablePrepare,
        IGitClient git)
    {
        var descriptors = App.AllCommands
            .Where(entry => !App.LocalOnlyCommandNames.Contains(entry.Info.Name))
        .Select(entry =>
        {
            var toolName = NormalizeToolName(entry.Info.Name);
            var command = entry.Build();
            var input = DescribeInput(command);
            if (catalogue.TryGetByToolName(toolName, out var application))
            {
                var typedInfo = application.Definition?.Info ?? entry.Info;
                return application with
                {
                    Path = application.Audience == CommandAudience.TrustedExperimental
                        ? ["trusted", typedInfo.Name]
                        : [typedInfo.Category, typedInfo.Name],
                    Description = typedInfo.WhenToUse,
                    Input = input,
                    McpVisible = application.McpVisible
                                 && application.Audience == CommandAudience.Public
                };
            }

            var info = entry.Info;
            var daemonDefinition = DaemonOwnedCommands.All.SingleOrDefault(
                definition => string.Equals(definition.Name, toolName, StringComparison.Ordinal));
            var audience = App.RemovedPublicCommandNames.Contains(info.Name)
                ? CommandAudience.Removed
                : App.TrustedExperimentalCommandNames.Contains(info.Name)
                    ? CommandAudience.TrustedExperimental
                    : CommandAudience.Public;
            var effects = string.Equals(info.Name, "probe-di", StringComparison.Ordinal)
                ? CommandEffects.RepositoryRead | CommandEffects.RepositoryCodeExecution
                : info.Mutates
                ? CommandEffects.RepositoryRead | CommandEffects.RepositoryWrite
                : CommandEffects.RepositoryRead;
            return new ApplicationCommandDescriptor(
                audience == CommandAudience.TrustedExperimental
                    ? ["trusted", info.Name]
                    : [info.Category, info.Name],
                toolName,
                command.Description ?? entry.Info.WhenToUse,
                info.WhenToUse,
                input,
                !entry.Info.Mutates && audience == CommandAudience.Public,
                ToDescriptorSafety(
                    daemonDefinition?.Safety,
                    info.Mutates),
                info.JsonOutput
                || info.SupportedFormats?.Contains(OutputFormat.Json) == true,
                null,
                null,
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
        AddTypedMetaCommand(
            () => SuggestCommand.Build(suggest, git),
            ["suggest"],
            suggest.Definition.Name);
        AddTypedMetaCommand(
            () => PrimeCommand.Build(prime),
            ["prime"],
            prime.Definition.Name);
        AddTypedMetaCommand(
            () => DurablePrepareCommand.Build(durablePrepare),
            ["prepare-operation"],
            durablePrepare.Definition.Name);

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

        void AddTypedMetaCommand(
            Func<Command> build,
            IReadOnlyList<string> path,
            string toolName)
        {
            if (!catalogue.TryGetByToolName(toolName, out var application))
                throw new ArgumentException(
                    "A typed meta command requires a host-neutral definition.",
                    nameof(toolName));

            var command = build();
            descriptors.Add(application with
            {
                Path = path,
                Description = command.Description ?? application.WhenToUse,
                Input = DescribeInput(command)
            });
        }
    }

    private static CommandSafetyPolicy ToDescriptorSafety(
        CommandSafety? daemonSafety,
        bool commandMutates)
    {
        // Descriptor safety distinguishes repository mutation from protected host-local writes.
        // The core contract separately retains apply- versus review-default mutation behavior.
        return daemonSafety?.Effect switch
        {
            CommandEffect.ReadOnly => CommandSafetyPolicy.ReadOnly,
            CommandEffect.LocalStateWrite => CommandSafetyPolicy.LocalWrite,
            CommandEffect.Mutation => CommandSafetyPolicy.DryRunByDefault,
            _ => commandMutates
                ? CommandSafetyPolicy.DryRunByDefault
                : CommandSafetyPolicy.ReadOnly
        };
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
