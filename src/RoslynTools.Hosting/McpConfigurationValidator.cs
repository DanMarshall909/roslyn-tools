using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;

namespace RoslynTools.Configuration;

/// <summary>Validates configured exact MCP names against the host's public descriptor surface.</summary>
internal static class McpConfigurationValidator
{
    internal static HostConfigurationResolution Validate(
        HostConfigurationResolution configuration,
        ApplicationCommandCatalog descriptors)
    {
        var publicTools = descriptors.All
            .Where(descriptor => descriptor.McpVisible
                                 && descriptor.Port is not null
                                 && descriptor.Audience == CommandAudience.Public)
            .Select(descriptor => descriptor.ToolName)
            .ToHashSet(StringComparer.Ordinal);
        var diagnostics = configuration.ConfiguredMcpTools
            .Where(reference => !publicTools.Contains(reference.Name))
            .Select(reference => new HostConfigurationDiagnostic(
                reference.Path,
                $"Invalid rt configuration '{reference.Path}': MCP tool name "
                + $"'{reference.Name}' does not match a public MCP descriptor."))
            .ToArray();
        if (diagnostics.Length == 0)
            return configuration;

        return configuration with
        {
            GlobalFile = MarkInvalid(configuration.GlobalFile, diagnostics),
            RepositoryFile = MarkInvalid(configuration.RepositoryFile, diagnostics),
            Diagnostics = configuration.Diagnostics.Concat(diagnostics).ToArray()
        };
    }

    private static HostConfigurationFileState MarkInvalid(
        HostConfigurationFileState state,
        IReadOnlyList<HostConfigurationDiagnostic> diagnostics)
    {
        var errors = diagnostics
            .Where(diagnostic => string.Equals(diagnostic.Path, state.Path, StringComparison.Ordinal))
            .Select(diagnostic => diagnostic.Message)
            .ToArray();
        return errors.Length == 0
            ? state
            : state with { IsValid = false, Error = string.Join(" ", errors) };
    }
}
