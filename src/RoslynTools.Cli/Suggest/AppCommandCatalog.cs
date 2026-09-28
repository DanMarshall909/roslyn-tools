using RoslynTools.Core;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Cli.Suggest;

/// <summary>
///     CLI host's <see cref="ICommandCatalog" /> — wraps <see cref="App.CatalogCommands" />.
///     MCP currently shares the same source via its project reference to this assembly;
///     each host registers this in its own DI graph so the dependency direction stays
///     "host depends on Core" rather than "service depends on host".
/// </summary>
internal sealed class AppCommandCatalog : ICommandCatalog
{
    public IReadOnlyList<RtCommandInfo> All { get; } =
        App.CatalogCommands.Select(e => e.Info)
            .Append(HostCommandMetadata.Feedback)
            .ToArray();
}
