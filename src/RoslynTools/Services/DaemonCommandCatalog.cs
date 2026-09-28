using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

/// <summary>
///     Structured result produced by the daemon before a host adapter renders it.
///     The value and follow-on tools deliberately travel together so CLI and MCP cannot
///     invent different next actions for the same command execution.
/// </summary>
internal sealed record CommandExecutionResult(
    object? Data,
    IReadOnlyList<CommandFollowOn> SuggestedTools);

/// <summary>
///     Application-owned command facts used by the daemon and projected by adapters. This contains
///     no command-line parser, MCP schema, or renderer concept.
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

/// <summary>Canonical, transport-neutral definition for the first migrated command.</summary>
internal static class OutlineCommandDefinition
{
    private static RtCommandInfo Metadata { get; } = new(
        "outline",
        false,
        true,
        "inspect",
        "Getting a JSON summary of all types and member signatures in a file or namespace without reading the raw source. Prefer this over Read when you only need the shape of the code, not its contents — a fraction of the tokens reading the files would cost. The default is a compact per-type view { name, kind, signatures: [...] } with no line numbers (stable across edits) — ideal for agents orienting in an unfamiliar module or diffing public surface area across branches. Use --with-locations for the legacy verbose grouped-members shape with line ranges, or --summary for a one-entry-per-type overview (fqn, kind, line range, optional XML doc description).",
        [
            "rt inspect outline src/backend/TourStore.Api/Controllers/Admin/AdminController.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect outline ProjectX.Core.Services --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect outline RoslynTools.Commands --solution RoslynTools.slnx",
            "rt inspect outline \"src/**/*.cs\" --solution RoslynTools.slnx",
            "rt inspect outline \"RoslynTools.*\" --solution RoslynTools.slnx",
            "rt inspect outline --regex \"Service\\.cs$\" --solution RoslynTools.slnx",
            "rt inspect outline src/RoslynTools/Core --solution RoslynTools.slnx --summary",
            "rt inspect outline RoslynTools.Core --solution RoslynTools.slnx --summary",
            "rt inspect outline src/RoslynTools/Core/WorkspaceLoader.cs --solution RoslynTools.slnx --with-locations"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Metadata,
        typeof(OutlineRequest),
        typeof(RtResultEnvelope<OutlineResult>),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [
            new CommandFollowOn(
                "describe",
                "Use after outline when a returned type or member needs a focused signature view."),
            new CommandFollowOn(
                "find_refs",
                "Use after outline when you need to trace references to a symbol it identified.")
        ],
        product: CommandProductMetadata.FromLegacy(
            Metadata,
            CommandSafety.ReadOnly,
            maturity: CapabilityMaturity.StableCandidate));

    internal static string Name => Definition.Name;
    internal static RtCommandInfo Info => Definition.Info;
}

/// <summary>Canonical daemon metadata for constructor dependency injection.</summary>
internal static class AddDependencyCommandDefinition
{
    private static RtCommandInfo Metadata { get; } = new(
        "add-dependency",
        true,
        false,
        "refactor",
        Aliases: ["ad"],
        WhenToUse:
        "Use this when injecting a new constructor dependency into a class without manually editing the constructor and field declarations. Prefer this over Edit when the class has multiple constructors or when you want the field naming convention (_camelCase) applied consistently.",
        Examples:
        [
            "rt refactor add-dependency --type BookingService --dependency IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ad --type OrderController --dependency IPaymentGateway --field _payment --dry-run",
            "rt refactor add-dependency --type ReportGenerator --dependency IRepository --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Metadata,
        typeof(AddDependencyRequest),
        typeof(MutationExecutionResult<AddDependencyResult>),
        mcpVisible: true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        [
            new CommandFollowOn(
                "describe",
                "Use after the preview or change to review the target constructor and injected field."),
            new CommandFollowOn(
                "find_refs",
                "Use after the change to find call sites that can now use the injected dependency.")
        ]);

    internal static string Name => Definition.Name;
    internal static RtCommandInfo Info => Definition.Info;
}

/// <summary>Canonical daemon metadata for sealing a class with no derived types.</summary>
internal static class MakeSealedCommandDefinition
{
    internal const string Name = "make_sealed";

    internal static RtCommandInfo Info { get; } = new(
        "make-sealed",
        true,
        false,
        "refactor",
        Aliases: ["ms"],
        WhenToUse:
        "Use this when adding the sealed modifier to a class that has no derived types in the solution. Prefer it over Edit because the command verifies no derived classes exist before modifying. Sealing classes improves runtime dispatch performance and communicates design intent.",
        Examples:
        [
            "rt refactor make-sealed --type BookingValidator --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ms --type CsvFormatter --dry-run",
            "rt refactor make-sealed --type RtCommandInfo --solution RoslynTools.slnx"
        ]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(MakeSealedRequest),
        typeof(MutationExecutionResult<MakeSealedResult>),
        true,
        CommandSafety.ReviewByDefault,
        CommandExecutionPlacement.InProcess,
        [
            new CommandFollowOn(
                "describe",
                "Use after the preview or change to inspect the class declaration and its modifiers."),
            new CommandFollowOn(
                "find_refs",
                "Use after sealing to review references to the class before committing the change.")
        ]);
}

/// <summary>Canonical daemon metadata for circular dependency analysis.</summary>
internal static class CircularDepsCommandDefinition
{
    internal const string Name = "circular_deps";

    internal static RtCommandInfo Info { get; } = new(
        "circular-deps",
        false,
        false,
        "analyze",
        Aliases: ["cdeps"],
        WhenToUse:
        "Use this when detecting circular namespace dependencies before a refactor, or after adding inter-namespace references. Use --scope to limit analysis to domain namespaces and ignore third-party noise.",
        Examples:
        [
            "rt analyze circular-deps --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze cdeps --scope ProjectX.Core --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze circular-deps --solution RoslynTools.slnx --scope RoslynTools"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
}

/// <summary>Canonical daemon metadata for repeated string literal analysis.</summary>
internal static class FindMagicStringsCommandDefinition
{
    internal const string Name = "find_magic_strings";

    internal static RtCommandInfo Info { get; } = new(
        "find-magic-strings",
        false,
        false,
        "analyze",
        Aliases: ["magic-strings"],
        WhenToUse:
        "Use this when finding repeated string literals that should become named constants before cleanup or domain refactoring. Generated files, const string declarations, and .rtignore matches are skipped.",
        Examples:
        [
            "rt analyze find-magic-strings --solution RoslynTools.slnx",
            "rt analyze magic-strings --min-length 4 --min-occurrences 3 --solution C:\\code\\app\\App.sln",
            "rt analyze find-magic-strings --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
}

/// <summary>Canonical daemon metadata for structural source-pattern analysis.</summary>
internal static class FindPatternCommandDefinition
{
    internal const string Name = "find_pattern";

    internal static RtCommandInfo Info { get; } = new(
        "find-pattern",
        false,
        true,
        "inspect",
        Aliases: ["fp"],
        WhenToUse:
        "Use this when a known statement sequence may be duplicated elsewhere with different identifiers or literal values. Prefer it over Grep because it compares Roslyn syntax structure rather than source text.",
        Examples:
        [
            "rt inspect find-pattern --file src/Services/BookingService.cs --start-line 42 --end-line 50 --solution RoslynTools.slnx",
            "rt inspect fp --file src/Controllers/AdminController.cs --start-line 110 --end-line 125 --scope RoslynTools.Core --solution RoslynTools.slnx"
        ],
        SeeAlso: ["find-duplicates", "source", "complexity"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(FindPatternRequest),
        typeof(FindPatternResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [
            new CommandFollowOn(
                "source",
                "Use after a match to inspect the complete declaration containing that location."),
            new CommandFollowOn(
                "find_duplicates",
                "Use when structural matches suggest a wider solution-level duplication problem.")
        ]);
}

/// <summary>Canonical daemon metadata for language-feature density analysis.</summary>
internal static class LanguageFeaturesCommandDefinition
{
    internal const string Name = "language_features";

    internal static RtCommandInfo Info { get; } = new(
        "language-features",
        false,
        true,
        "analyze",
        Aliases: ["lang-features"],
        WhenToUse:
        "Use this when planning C# modernization or consistency refactors and you need to know which language features are established conventions, how dense their usage is, and where mechanically comparable exceptions remain. Prefer it over Grep because it counts Roslyn syntax and semantic constructs, reports stable feature IDs and valid denominators, and distinguishes unsupported compiler features from zero usage.",
        Examples:
        [
            "rt analyze language-features --solution RoslynTools.slnx",
            "rt analyze language-features --introduced-since 12 --solution RoslynTools.slnx --json",
            "rt analyze language-features --refactorable-only --include-locations --solution RoslynTools.slnx",
            "rt analyze lang-features --feature collection_expression --include-locations --solution C:\\code\\app\\App.slnx --json"
        ],
        SeeAlso: ["complexity", "list-smells", "source", "find-pattern"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Yaml, OutputFormat.Llm]);
}

/// <summary>Canonical daemon metadata for import-directive analysis.</summary>
internal static class ListImportsCommandDefinition
{
    internal const string Name = "list_imports";

    internal static RtCommandInfo Info { get; } = new(
        "list-imports",
        false,
        false,
        "analyze",
        Aliases: ["imports"],
        WhenToUse:
        "Use this when auditing C# using directives before consolidating imports or introducing static member imports. Prefer it over Read when you need global, static, and alias directives across the solution.",
        Examples:
        [
            "rt analyze list-imports --kind global --solution RoslynTools.slnx",
            "rt analyze imports --kind static --solution C:\\code\\app\\App.sln",
            "rt analyze list-imports --kind all --json --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);
}

/// <summary>Canonical daemon metadata for call-site analysis.</summary>
internal static class FindCallersCommandDefinition
{
    internal const string Name = "find_callers";

    internal static RtCommandInfo Info { get; } = new(
        "find-callers",
        false,
        false,
        "inspect",
        Aliases: ["callers"],
        WhenToUse:
        "Use this when finding method invocation sites before renaming or deleting a method, or when assessing a signature change. Prefer it over Grep because it filters to semantic call sites and excludes nameof() references.",
        Examples:
        [
            "rt inspect find-callers --symbol SendEmail --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect callers --symbol ProcessPayment --include-tests --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-callers --symbol BuildGraphAsync --solution RoslynTools.slnx"
        ],
        SeeAlso: ["relationships", "change-signature"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(FindCallersRequest),
        typeof(FindCallersOperationResult),
        false,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReadOnly,
            audience: CommandAudience.Removed));
}

/// <summary>Canonical daemon metadata for implementation discovery.</summary>
internal static class FindImplementationsCommandDefinition
{
    internal const string Name = "find_impl";

    internal static RtCommandInfo Info { get; } = new(
        "find-impl",
        false,
        true,
        "inspect",
        Aliases: ["impls"],
        WhenToUse:
        "Use this when discovering implementations of an interface or derived classes of an abstract base before changing a contract. Prefer it over Grep because it resolves semantic implementations across the solution.",
        Examples:
        [
            "rt inspect find-impl IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect impls ProjectX.Core.Services.IBookingRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-impl IRtCommand --solution RoslynTools.slnx"
        ],
        SeeAlso: ["relationships", "implement-interface", "extract-interface"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(FindImplementationsRequest),
        typeof(FindImplementationsResult),
        false,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        [],
        product: CommandProductMetadata.FromLegacy(
            Info,
            CommandSafety.ReadOnly,
            audience: CommandAudience.Removed));
}
