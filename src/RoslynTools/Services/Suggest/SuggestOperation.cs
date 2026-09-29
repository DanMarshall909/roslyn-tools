using System.Text.Json.Serialization;
using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

internal sealed record SuggestOperationRequest(
    [property: JsonRequired]
    [property: CommandPropertyDescription("Natural-language description of what you want to do (omit to get git-context suggestions)")]
    string Query,
    [property: CommandPropertyDescription("Maximum number of ranked candidates to return")]
    int Top = 3);

/// <summary>Shared catalog-suggestion operation used by CLI and MCP.</summary>
internal sealed class SuggestOperation
    : CommandApplicationPort<SuggestOperationRequest, SuggestResponse>, IRtService
{
    private readonly SuggestService _suggest;

    public SuggestOperation(SuggestService suggest)
    {
        _suggest = suggest;
    }

    public override ApplicationCommandDefinition Definition => SuggestCommandDefinition.Definition;

    public override Task<Result<SuggestResponse, RtError>> ExecuteAsync(
        SuggestOperationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Result<SuggestResponse, RtError>.Ok(
            _suggest.Suggest(new SuggestRequest(request.Query, request.Top))));
    }
}

internal static class SuggestCommandDefinition
{
    internal static RtCommandInfo Info { get; } = new(
        "suggest-command",
        Mutates: false,
        JsonOutput: true,
        "meta",
        "Call this when you know what you want to do to C# code but aren't sure which rt command does it — describe the task in natural language and get ranked candidates back before falling back to a manual edit. Returns ranked candidates with name, score, when-to-use, and example invocation.",
        ["rt suggest \"rename a symbol\" --json"],
        Placement: CommandPlacement.RootOnly);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SuggestOperationRequest),
        typeof(SuggestResponse),
        mcpVisible: true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.InProcess,
        [],
        product: new CommandProductMetadata(
            CapabilityMaturity.Experimental,
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
                MaximumItems: 100,
                MaximumOutputBytes: 1_048_576),
            Limitations: [],
            Info.Examples));
}
