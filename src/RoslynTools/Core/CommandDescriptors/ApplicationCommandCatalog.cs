namespace RoslynTools.Core.CommandDescriptors;

/// <summary>
///     Host-neutral descriptors for typed application ports. Hosts may project these into
///     their own command, tool, or RPC surfaces without constructing a CLI command.
/// </summary>
internal sealed class ApplicationCommandCatalog
{
    private readonly Lazy<CatalogState> _state;

    internal ApplicationCommandCatalog(Func<IEnumerable<ICommandApplicationPort>> resolvePorts)
    {
        ArgumentNullException.ThrowIfNull(resolvePorts);
        _state = new Lazy<CatalogState>(() => Build(resolvePorts()));
    }

    internal IReadOnlyList<ApplicationCommandDescriptor> All => _state.Value.Descriptors;

    internal bool TryGetByToolName(
        string toolName,
        out ApplicationCommandDescriptor descriptor) =>
        _state.Value.ByToolName.TryGetValue(toolName, out descriptor!);

    private static CatalogState Build(IEnumerable<ICommandApplicationPort> ports)
    {
        var descriptors = ports
            .Where(port => port is not ILegacyCommandApplicationPort)
            .Select(FromPort)
            .ToArray();
        var byToolName = descriptors.ToDictionary(
            descriptor => descriptor.ToolName,
            StringComparer.Ordinal);
        return new CatalogState(descriptors, byToolName);
    }

    private static ApplicationCommandDescriptor FromPort(ICommandApplicationPort port)
    {
        var definition = port.Definition;
        var info = definition.Info;
        var audience = definition.Product.Audience;
        var path = audience == CommandAudience.TrustedExperimental
            ? new[] { "trusted", info.Name }
            : info.Placement == CommandPlacement.RootOnly
                ? [info.Name]
                : [info.Category, info.Name];
        var safety = definition.Safety.Effect switch
        {
            CommandEffect.ReadOnly => CommandSafetyPolicy.ReadOnly,
            CommandEffect.LocalStateWrite => CommandSafetyPolicy.LocalWrite,
            CommandEffect.Mutation => CommandSafetyPolicy.DryRunByDefault,
            _ => throw new ArgumentOutOfRangeException(nameof(port))
        };

        return new ApplicationCommandDescriptor(
            path,
            definition.Name,
            info.WhenToUse,
            info.WhenToUse,
            new ApplicationCommandInput([], []),
            definition.McpVisible,
            safety,
            Structured: true,
            port,
            definition,
            definition.Product.Effects,
            audience);
    }

    private sealed record CatalogState(
        ApplicationCommandDescriptor[] Descriptors,
        Dictionary<string, ApplicationCommandDescriptor> ByToolName);
}
