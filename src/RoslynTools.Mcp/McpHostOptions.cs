using RoslynTools.Core;

namespace RoslynTools.Mcp;

/// <summary>
///     Startup-only MCP host authority. This value is created from process arguments before
///     protocol input is read; ordinary MCP requests cannot modify it.
/// </summary>
internal sealed class McpHostOptions
{
    internal McpHostOptions(
        string? trustedRepositoryRoot = null,
        bool showHelp = false,
        RepositoryTrustRegistry? trustRegistry = null)
    {
        if (trustedRepositoryRoot is not null)
        {
            if (!Path.IsPathFullyQualified(trustedRepositoryRoot))
                throw new ArgumentException(
                    "The trusted repository root must be an absolute path.",
                    nameof(trustedRepositoryRoot));

            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(trustedRepositoryRoot));
            if (!Directory.Exists(fullPath))
                throw new ArgumentException(
                    $"The trusted repository root does not exist: '{fullPath}'.",
                    nameof(trustedRepositoryRoot));

            TrustedRepositoryRoot = fullPath;
        }

        ShowHelp = showHelp;
        TrustRegistry = trustRegistry;
    }

    internal string? TrustedRepositoryRoot { get; }

    internal bool ShowHelp { get; }

    internal RepositoryTrustRegistry? TrustRegistry { get; }

    internal bool CanDiscoverRepositoryWrites =>
        TrustedRepositoryRoot is not null
        || TrustRegistry?.HasAnyTrustedRepository() == true;

    internal IDisposable? EnterTrustScope() => TrustedRepositoryRoot is not null
        ? RepositoryTrustContext.UseExplicitTrust(TrustedRepositoryRoot)
        : TrustRegistry is not null
            ? RepositoryTrustContext.UsePersistentTrust(TrustRegistry)
            : null;

    internal static bool TryParse(
        IReadOnlyList<string> arguments,
        out McpHostOptions options,
        out string? error)
    {
        options = new McpHostOptions();
        error = null;

        if (arguments.Count == 0)
        {
            options = new McpHostOptions(trustRegistry: new RepositoryTrustRegistry());
            return true;
        }

        if (arguments.Count == 1 && arguments[0] is "--help" or "-h")
        {
            options = new McpHostOptions(showHelp: true);
            return true;
        }

        if (arguments[0] != "--trust-repository")
        {
            error = $"Unknown option: '{arguments[0]}'.";
            return false;
        }

        if (arguments.Count != 2)
        {
            error = "--trust-repository requires one absolute repository root.";
            return false;
        }

        try
        {
            options = new McpHostOptions(arguments[1]);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
