using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     Host-supplied source of rt command metadata. CLI and MCP can share
///     typed application definitions plus host-specific guidance, while tests
///     can supply a synthetic list. Keeps <see cref="SuggestService" />
///     independent of host discovery mechanisms.
/// </summary>
internal interface ICommandCatalog
{
    /// <summary>Every command's <see cref="RtCommandInfo" />, in any order.</summary>
    IReadOnlyList<RtCommandInfo> All { get; }
}
