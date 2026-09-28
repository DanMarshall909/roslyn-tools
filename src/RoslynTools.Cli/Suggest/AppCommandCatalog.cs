using RoslynTools.Core;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Cli.Suggest;

/// <summary>
///     Historical CLI command snapshot retained for equivalence tests while hosts
///     migrate to <see cref="SharedSuggestionCatalog" />.
/// </summary>
internal sealed class AppCommandCatalog : ICommandCatalog
{
    public IReadOnlyList<RtCommandInfo> All { get; } =
        App.CatalogCommands.Select(e => e.Info)
            .Append(HostCommandMetadata.Feedback)
            .ToArray();
}
