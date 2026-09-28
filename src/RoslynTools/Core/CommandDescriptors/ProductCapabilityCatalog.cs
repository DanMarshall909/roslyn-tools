using RoslynTools.Core;

namespace RoslynTools.Core.CommandDescriptors;

/// <summary>
///     Exposes typed definitions from the reusable application catalogue when
///     operations such as doctor request the product inventory.
/// </summary>
internal sealed class ProductCapabilityCatalog(ApplicationCommandCatalog catalogue)
    : IProductCapabilityCatalog
{
    private readonly ApplicationCommandCatalog _catalogue = catalogue;

    IReadOnlyList<ApplicationCommandDefinition> IProductCapabilityCatalog.GetDefinitions() => _catalogue.All
        .Select(descriptor => descriptor.Definition!)
        .ToArray();
}
