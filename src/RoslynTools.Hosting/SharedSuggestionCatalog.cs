using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Services;
using RoslynTools.Services.Suggest;

namespace RoslynTools;

/// <summary>
/// Supplies command suggestions from typed application definitions and shared host metadata.
/// </summary>
internal sealed class SharedSuggestionCatalog : ICommandCatalog
{
    private readonly Lazy<IReadOnlyList<RtCommandInfo>> _all;

    public SharedSuggestionCatalog(ApplicationCommandCatalog catalogue)
    {
        // Port construction can require ICommandCatalog. Read the typed catalogue only
        // after DI has completed that construction, when suggestions are requested.
        _all = new Lazy<IReadOnlyList<RtCommandInfo>>(() => Build(catalogue));
    }

    public IReadOnlyList<RtCommandInfo> All => _all.Value;

    private static RtCommandInfo[] Build(ApplicationCommandCatalog catalogue) =>
        catalogue.All
            .Where(descriptor => descriptor.Audience == CommandAudience.Public)
            .Select(descriptor => descriptor.Definition!.Info.Name == HostCommandMetadata.SemanticDiff.Name
                ? HostCommandMetadata.SemanticDiff
                : descriptor.Definition.Info)
            .Where(info => info.Name != PrimeCommandDefinition.Info.Name
                           && info.Name != SuggestCommandDefinition.Info.Name)
            .Append(HostCommandMetadata.Config)
            .Append(HostCommandMetadata.Trust)
            .Append(HostCommandMetadata.Feedback)
            .OrderBy(info => info.Name, StringComparer.Ordinal)
            .ToArray();
}
