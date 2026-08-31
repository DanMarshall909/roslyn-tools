namespace RoslynTools.Core;

/// <summary>Chooses where a host-neutral application operation executes.</summary>
internal enum CommandExecutionPlacement
{
    DaemonPreferred,
    InProcess
}

/// <summary>Adds host-neutral JSON-schema guidance to an application request property.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class CommandPropertyDescriptionAttribute(string description) : Attribute
{
    public string Description { get; } = description;
}

/// <summary>
///     One host-neutral alternative request shape. Property names refer to CLR request
///     properties so host adapters can project their canonical wire names.
/// </summary>
internal sealed record CommandRequestVariant(
    string DiscriminatorProperty,
    string DiscriminatorValue,
    bool DiscriminatorRequired,
    IReadOnlyList<string> RequiredProperties,
    IReadOnlyList<string> ForbiddenProperties);

/// <summary>One transport-neutral recommended follow-on command.</summary>
internal sealed record CommandFollowOn(string ToolName, string WhenToUse);

/// <summary>
///     The host-neutral identity, transport contract, safety, and routing policy for one
///     application command. Host adapters project this definition into their own surfaces.
/// </summary>
internal sealed record ApplicationCommandDefinition
{
    internal ApplicationCommandDefinition(
        RtCommandInfo info,
        Type requestType,
        Type responseType,
        bool mcpVisible,
        CommandSafety safety,
        CommandExecutionPlacement executionPlacement,
        IReadOnlyList<CommandFollowOn> suggestedTools,
        IReadOnlyList<CommandRequestVariant>? requestVariants = null,
        CommandProductMetadata? product = null)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(requestType);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(suggestedTools);

        if (info.Mutates != (safety.Effect == CommandEffect.Mutation))
            throw new ArgumentException(
                $"Command '{info.Name}' metadata and safety disagree about mutation behavior.",
                nameof(safety));

        if (safety.Effect == CommandEffect.Mutation
            && safety.MutationDefault != MutationExecutionDefault.Review)
            throw new ArgumentException(
                $"Mutation command '{info.Name}' must default to review or dry-run during hardening.",
                nameof(safety));

        if (safety.Effect == CommandEffect.Mutation
            && executionPlacement != CommandExecutionPlacement.InProcess)
            throw new ArgumentException(
                $"Mutation command '{info.Name}' must execute in-process until durable dispatch recovery is available.",
                nameof(executionPlacement));

        Info = info;
        RequestType = requestType;
        ResponseType = responseType;
        Product = product ?? CommandProductMetadata.FromLegacy(info, safety);
        // Apply-capable operations are intentionally absent from normal MCP discovery while
        // mutation execution is review-first and in-process only. Plan-only operations can be
        // reintroduced later with a distinct non-applying contract.
        McpVisible = mcpVisible
                     && safety.Effect == CommandEffect.ReadOnly
                     && Product.Audience == CommandAudience.Public;
        Safety = safety;
        ExecutionPlacement = executionPlacement;
        SuggestedTools = suggestedTools;
        RequestVariants = requestVariants ?? [];
    }

    /// <summary>The normalized tool and daemon identity.</summary>
    public string Name => Info.Name.Replace('-', '_');

    public RtCommandInfo Info { get; }

    public Type RequestType { get; }

    public Type ResponseType { get; }

    public bool McpVisible { get; }

    public CommandSafety Safety { get; }

    public CommandExecutionPlacement ExecutionPlacement { get; }

    public IReadOnlyList<CommandFollowOn> SuggestedTools { get; }

    public IReadOnlyList<CommandRequestVariant> RequestVariants { get; }

    public CommandProductMetadata Product { get; }
}
