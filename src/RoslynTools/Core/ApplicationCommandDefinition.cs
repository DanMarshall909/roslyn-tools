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
        IReadOnlyList<CommandRequestVariant>? requestVariants = null)
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

        Info = info;
        RequestType = requestType;
        ResponseType = responseType;
        McpVisible = mcpVisible;
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
}
