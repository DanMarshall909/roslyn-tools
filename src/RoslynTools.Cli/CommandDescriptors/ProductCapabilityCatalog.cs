using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;

namespace RoslynTools.CommandDescriptors;

/// <summary>
///     Resolves definitions only when requested, avoiding a construction cycle for
///     operations such as doctor that describe the catalogue they belong to.
/// </summary>
internal sealed class ProductCapabilityCatalog(IServiceProvider services)
    : IProductCapabilityCatalog
{
    private readonly IServiceProvider _services = services;

    public IReadOnlyList<ApplicationCommandDefinition> GetDefinitions() => _services
        .GetServices<ICommandApplicationPort>()
        .Where(port => port is not ILegacyCommandApplicationPort)
        .Select(port => port.Definition)
        .GroupBy(definition => definition.Name, StringComparer.Ordinal)
        .Select(group => group.First())
        .ToArray();
}
