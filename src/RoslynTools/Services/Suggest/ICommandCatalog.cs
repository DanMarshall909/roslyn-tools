using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     Host-supplied source of the rt command catalog. CLI wraps
///     <c>App.Commands</c>; MCP does the same. Other hosts (future tools,
///     tests) can supply a synthetic list. Keeps <see cref="SuggestService" />
///     independent of any one host's discovery mechanism.
/// </summary>
internal interface ICommandCatalog
{
    /// <summary>Every command's <see cref="RtCommandInfo" />, in any order.</summary>
    IReadOnlyList<RtCommandInfo> All { get; }
}
