using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;

namespace RoslynTools.Mcp.Tools;

/// <summary>Keeps MCP discovery and dispatch on the same runtime-authorized surface.</summary>
internal static class McpToolAvailability
{
    internal static bool IsAvailable(
        ApplicationCommandDescriptor descriptor,
        McpHostOptions hostOptions)
    {
        return descriptor.McpVisible
               && descriptor.Audience == CommandAudience.Public
               && (hostOptions.HasExplicitTrust
                   || !descriptor.Effects.HasFlag(CommandEffects.RepositoryWrite));
    }
}
