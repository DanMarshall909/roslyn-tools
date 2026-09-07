using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Services;

internal sealed record PrimeRequest;

/// <summary>
///     Produces the compact session-start summary shared by the CLI and MCP hosts.
///     No MSBuild or solution load is required.
/// </summary>
internal sealed class PrimeOperation
    : CommandApplicationPort<PrimeRequest, PrimeResult>, IRtService
{
    public override ApplicationCommandDefinition Definition => PrimeCommandDefinition.Definition;

    public override Task<Result<PrimeResult, RtError>> ExecuteAsync(
        PrimeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<PrimeResult, RtError>.Ok(CreateResult()));
    }

    private static PrimeResult CreateResult() => new(
        Gate: "rt inspect outline/describe <file> before reading or editing any .cs file",
        Entry: "rt suggest \"<task in English>\" to rank commands by intent",
        Note: "Every inspect JSON result includes _suggestions pre-filled with FQN + solution path",
        HotCommands:
        [
            "rt inspect describe \"<FQN>\" --solution <slnx>",
            "rt inspect outline <file.cs> --solution <slnx>",
            "rt inspect relationships references <Name> --solution <slnx>",
            "rt refactor rename \"<FQN>\" NewName --solution <slnx> --dry-run",
            "rt refactor move --type <FQN> --to-namespace <NS> --solution <slnx> --dry-run",
            "rt suggest \"<task in English>\""
        ]);
}

internal static class PrimeCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "prime",
        false,
        true,
        "session",
        "Call this at the start of a C# task to get the rt inspection gate, entry point, and common commands without loading a solution.",
        [
            "rt prime",
            "rt prime --json"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(PrimeRequest),
        typeof(PrimeResult),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        [],
        product: new CommandProductMetadata(
            CapabilityMaturity.Preview,
            CommandEffects.None,
            ConditionallyMutable: false,
            CommandIdempotency.Idempotent,
            CommandAudience.Public,
            TrustRequirement.None,
            WorkspaceRequirement.None,
            Prerequisites: [],
            CommandCost.Low,
            CancellationSupport.Cooperative,
            new CommandResourceLimits(
                DefaultTimeoutMilliseconds: 5_000,
                MaximumItems: 10,
                MaximumOutputBytes: 32_768),
            Limitations: [],
            Info.Examples));
}
