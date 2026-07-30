using Microsoft.Extensions.DependencyInjection;

using RoslynTools.Core;
using RoslynTools.Mcp;
using RoslynTools.Mcp.Tools;

namespace RoslynTools.Tests.Mcp.Architecture;

/// <summary>
///     Locks the consistency contract between native MCP handlers and CLI commands
///     (EP-013 Phase 1 Batch 5, US-107):
///     <list type="bullet">
///         <item>
///             Every <see cref="IMcpHandler" /> registered in DI has a
///             <c>ToolName</c> that matches some <see cref="IRtCommand" /> in
///             <see cref="App.Commands" />, after hyphens-to-underscores
///             normalisation (<c>find-refs</c> → <c>find_refs</c>).
///         </item>
///     </list>
///     <para>
///         <b>Override model:</b> the auto-bridge (<see cref="CliCommandBridge" />)
///         exposes every CLI command by default. A native handler overrides the bridge
///         by name. This test asserts only the forward direction (handler name ⊆ command
///         names) so we catch orphan handlers and name drift; the inverse — every command
///         eventually gets a native handler — is owned by US-095 / US-096.
///     </para>
/// </summary>
public sealed class McpHandlerNameContract
{
    /// <summary>
    ///     Documented exceptions: handlers that are intentionally NOT thin adapters
    ///     around a single CLI command but instead operate on the rt command catalog
    ///     itself (meta/discoverability handlers). Each entry MUST cite a backlog story
    ///     so future readers know whether the exception is permanent or pending fix.
    /// </summary>
    private static readonly HashSet<string> KnownMetaHandlerNames = new(StringComparer.Ordinal)
    {
        "suggest_command", // US-106 tracks aligning this with the CLI `rt suggest` meta-command name
        "feedback_add", // FB-373: nested `rt feedback add` command, not an App.Commands entry
        "feedback_list" // FB-373: nested `rt feedback list` command, not an App.Commands entry
    };

    [Fact]
    public void with_every_imcphandler_the_toolname_matches_a_known_cli_command_name()
    {
        var handlerToolNames = ResolveHandlerToolNames();
        var commandNames = App.Commands
            .Select(c => NormalizeToToolName(c.Info.Name))
            .ToHashSet(StringComparer.Ordinal);

        var orphans = handlerToolNames
            .Where(n => !commandNames.Contains(n) && !KnownMetaHandlerNames.Contains(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            "Every native IMcpHandler ToolName must match a CLI command name in App.Commands "
            + "(after hyphens-to-underscores normalisation), unless documented in "
            + "KnownMetaHandlerNames with a backlog story reference. Orphan handlers indicate "
            + "name drift between the handler registration and the underlying command. "
            + $"Orphans ({orphans.Count}): "
            + string.Join(", ", orphans));
    }

    [Fact]
    public void with_every_imcphandler_the_toolname_is_unique()
    {
        // The ToolRegistry already collides at construction if two handlers share a ToolName
        // (ToDictionary throws on duplicate key). This test makes the invariant explicit so a
        // failure points at the handler list, not at a generic dictionary exception.
        var handlerToolNames = ResolveHandlerToolNames();
        var duplicates = handlerToolNames
            .GroupBy(n => n, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} (x{g.Count()})")
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            duplicates.Count == 0,
            "Two native IMcpHandlers cannot share a ToolName — the registry would override one. "
            + $"Duplicates: {string.Join(", ", duplicates)}");
    }

    private static List<string> ResolveHandlerToolNames()
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(new StringReader(string.Empty), new StringWriter());

        using var provider = services.BuildServiceProvider();
        var handlers = provider.GetServices<IMcpHandler>().ToList();

        Assert.True(handlers.Count > 0, "Expected at least one native IMcpHandler registered.");
        return handlers.Select(h => h.ToolName).ToList();
    }

    private static string NormalizeToToolName(string commandName) =>
        commandName.Replace('-', '_');
}
