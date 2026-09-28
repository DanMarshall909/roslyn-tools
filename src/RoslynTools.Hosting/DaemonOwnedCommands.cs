using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

/// <summary>
///     Legacy daemon command facts projected by CLI and MCP adapters while those
///     commands migrate onto the shared application catalogue.
/// </summary>
internal sealed record DaemonCommandDefinition(
    string Name,
    RtCommandInfo Info,
    CommandSafety Safety,
    Type RequestType,
    Type ResponseType,
    IReadOnlyList<CommandFollowOn> SuggestedTools);

/// <summary>
///     The initial daemon-owned command catalogue. Add a command here only when its CLI
///     and MCP adapters both bind its typed request and call the daemon operation.
/// </summary>
internal static class DaemonOwnedCommands
{
    internal static IReadOnlyList<DaemonCommandDefinition> All { get; } =
    [
        new DaemonCommandDefinition(
            OutlineCommandDefinition.Name,
            OutlineCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(OutlineRequest),
            typeof(RtResultEnvelope<OutlineResult>),
            [
                new CommandFollowOn(
                    "describe",
                    "Use after outline when a returned type or member needs a focused signature view."),
                new CommandFollowOn(
                    "find_refs",
                    "Use after outline when you need to trace references to a symbol it identified.")
            ]),
        new DaemonCommandDefinition(
            AddDependencyCommandDefinition.Name,
            AddDependencyCommandDefinition.Info,
            CommandSafety.ReviewByDefault,
            typeof(AddDependencyRequest),
            typeof(MutationExecutionResult<AddDependencyResult>),
            [
                new CommandFollowOn(
                    "describe",
                    "Use after the preview or change to review the target constructor and injected field."),
                new CommandFollowOn(
                    "find_refs",
                    "Use after the change to find call sites that can now use the injected dependency.")
            ]),
        new DaemonCommandDefinition(
            MakeSealedCommandDefinition.Name,
            MakeSealedCommandDefinition.Info,
            CommandSafety.ReviewByDefault,
            typeof(MakeSealedRequest),
            typeof(MutationExecutionResult<MakeSealedResult>),
            [
                new CommandFollowOn(
                    "describe",
                    "Use after the preview or change to inspect the class declaration and its modifiers."),
                new CommandFollowOn(
                    "find_refs",
                    "Use after sealing to review references to the class before committing the change.")
            ]),
        new DaemonCommandDefinition(
            CircularDepsCommandDefinition.Name,
            CircularDepsCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(CircularDepsRequest),
            typeof(CircularDepsAnalysisResult),
            []),
        new DaemonCommandDefinition(
            FindMagicStringsCommandDefinition.Name,
            FindMagicStringsCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(FindMagicStringsRequest),
            typeof(FindMagicStringsResult),
            []),
        new DaemonCommandDefinition(
            FindPatternCommandDefinition.Name,
            FindPatternCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(FindPatternRequest),
            typeof(FindPatternResult),
            [
                new CommandFollowOn(
                    "source",
                    "Use after a match to inspect the complete declaration containing that location."),
                new CommandFollowOn(
                    "find_duplicates",
                    "Use when structural matches suggest a wider solution-level duplication problem.")
            ]),
        new DaemonCommandDefinition(
            LanguageFeaturesCommandDefinition.Name,
            LanguageFeaturesCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(LanguageFeaturesRequest),
            typeof(LanguageFeaturesResult),
            [
                new CommandFollowOn(
                    "source",
                    "Use after a location drill-down to inspect the declaration around a refactoring candidate."),
                new CommandFollowOn(
                    "suggest_command",
                    "Use when a reported convention exception needs a matching rt refactoring command.")
            ]),
        new DaemonCommandDefinition(
            ListImportsCommandDefinition.Name,
            ListImportsCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(ListImportsRequest),
            typeof(ListImportsResult),
            []),
        new DaemonCommandDefinition(
            FindCallersCommandDefinition.Name,
            FindCallersCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(FindCallersRequest),
            typeof(FindCallersOperationResult),
            []),
        new DaemonCommandDefinition(
            FindImplementationsCommandDefinition.Name,
            FindImplementationsCommandDefinition.Info,
            CommandSafety.ReadOnly,
            typeof(FindImplementationsRequest),
            typeof(FindImplementationsResult),
            [])
    ];

    internal static bool Contains(string commandName) =>
        All.Any(command => string.Equals(command.Name, commandName, StringComparison.Ordinal));

    internal static IReadOnlyList<CommandFollowOn> SuggestedToolsFor(string commandName)
    {
        return All.FirstOrDefault(command =>
            string.Equals(command.Name, commandName, StringComparison.Ordinal))
            ?.SuggestedTools ?? [];
    }
}
