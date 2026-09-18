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
               && hostOptions.ToolPolicy.Allows(descriptor.ToolName)
               && (hostOptions.CanDiscoverRepositoryWrites
                   || !descriptor.Effects.HasFlag(CommandEffects.RepositoryWrite));
    }

    internal static string? ValidatePolicy(
        IEnumerable<ApplicationCommandDescriptor> descriptors,
        McpToolPolicy policy)
    {
        var known = descriptors
            .Where(descriptor => descriptor.McpVisible
                                 && descriptor.Audience == CommandAudience.Public)
            .Select(descriptor => descriptor.ToolName)
            .ToHashSet(StringComparer.Ordinal);
        var unknown = policy.ConfiguredTools
            .Except(known, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length == 0)
            return null;

        var sources = policy.SourceFiles.Count == 0
            ? "the effective configuration"
            : string.Join(", ", policy.SourceFiles.Select(path => $"'{path}'"));
        return $"Invalid MCP tool policy in {sources}: unknown exact tool name(s): {string.Join(", ", unknown)}.";
    }
}
