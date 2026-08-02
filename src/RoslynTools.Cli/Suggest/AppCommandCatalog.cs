using RoslynTools.Core;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Cli.Suggest;

/// <summary>
///     CLI host's <see cref="ICommandCatalog" /> — wraps <see cref="App.Commands" />.
///     MCP currently shares the same source via its project reference to this assembly;
///     each host registers this in its own DI graph so the dependency direction stays
///     "host depends on Core" rather than "service depends on host".
/// </summary>
internal sealed class AppCommandCatalog : ICommandCatalog
{
    public IReadOnlyList<RtCommandInfo> All { get; } =
        App.Commands.Select(e => e.Info)
            .Append(new RtCommandInfo(
                "feedback",
                true,
                true,
                "meta",
                "Recording rt dogfood feedback, bugs, missing features, and unexpected command behavior. "
                + "Use feedback add to create an item or feedback list to review existing items.",
                [
                    "rt feedback add --description \"rename should also preview related symbols\"",
                    "rt feedback list --status pending"
                ]))
            .ToArray();
}
