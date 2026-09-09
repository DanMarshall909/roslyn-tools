using RoslynTools.Core;

namespace RoslynTools.Mcp;

/// <summary>
///     Startup-only MCP host authority. This value is created from process arguments before
///     protocol input is read; ordinary MCP requests cannot modify it.
/// </summary>
internal sealed class McpHostOptions
{
    internal McpHostOptions(string? trustedRepositoryRoot = null, bool showHelp = false)
        : this(trustedRepositoryRoot is null ? [] : new[] { trustedRepositoryRoot }, showHelp)
    {
    }

    internal McpHostOptions(IEnumerable<string> trustedRepositoryRoots, bool showHelp = false)
    {
        ArgumentNullException.ThrowIfNull(trustedRepositoryRoots);
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var roots = new List<string>();
        foreach (var trustedRepositoryRoot in trustedRepositoryRoots)
        {
            if (string.IsNullOrWhiteSpace(trustedRepositoryRoot) || !Path.IsPathFullyQualified(trustedRepositoryRoot))
                throw new ArgumentException(
                    "The trusted repository root must be an absolute path.",
                    nameof(trustedRepositoryRoots));

            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRepositoryRoot));
            if (!Directory.Exists(fullPath))
                throw new ArgumentException(
                    $"The trusted repository root does not exist: '{fullPath}'.",
                    nameof(trustedRepositoryRoots));

            if (!roots.Contains(fullPath, comparer))
                roots.Add(fullPath);
        }

        TrustedRepositoryRoots = roots.AsReadOnly();
        ShowHelp = showHelp;
    }

    internal IReadOnlyList<string> TrustedRepositoryRoots { get; }

    internal bool HasExplicitTrust => TrustedRepositoryRoots.Count > 0;

    internal bool ShowHelp { get; }

    internal IDisposable EnterTrustScope() => RepositoryTrustContext.UseExplicitTrust(TrustedRepositoryRoots);

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out McpHostOptions options,
        out string? error)
    {
        options = new McpHostOptions();
        error = null;

        if (arguments.Count == 0)
            return true;

        if (arguments.Count == 1 && arguments[0] is "--help" or "-h")
        {
            options = new McpHostOptions(showHelp: true);
            return true;
        }

        var roots = new List<string>();
        for (var index = 0; index < arguments.Count; index += 2)
        {
            if (arguments[index] != "--trust-repository")
            {
                error = $"Unknown option: '{arguments[index]}'.";
                return false;
            }

            if (index + 1 >= arguments.Count || arguments[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                error = "Each --trust-repository requires one absolute repository root.";
                return false;
            }

            roots.Add(arguments[index + 1]);
        }

        try
        {
            options = new McpHostOptions(roots);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
