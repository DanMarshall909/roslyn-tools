namespace RoslynTools.Services.Suggest;

/// <summary>
///     Generates ready-to-run rt command suggestions from inspection command output.
///     Consumed by both CLI and MCP adapters to populate <c>_suggestions</c> in JSON responses.
/// </summary>
internal static class SuggestionHelper
{
    /// <summary>
    ///     Produces up to 5 follow-on suggestions for an <c>rt inspect describe</c> result.
    ///     Static relationship routing is ordered first
    ///     so the most universally useful commands appear even when there are no related types.
    ///     <see cref="CompactDescription.RelatedSymbolFqns" /> seeds additional <c>describe</c>
    ///     entries using the remaining slots.
    /// </summary>
    internal static IReadOnlyList<ToolSuggestion> ForDescribe(CompactDescription cd, string? solutionPath)
    {
        var sol = solutionPath != null ? $" --solution {solutionPath}" : "";

        var suggestions = new List<ToolSuggestion>
        {
            CreateRelationship("References", cd.Fqn, solutionPath),
            cd.Kind.Equals("Interface", StringComparison.OrdinalIgnoreCase)
                ? CreateRelationship("Implementations", cd.Fqn, solutionPath)
                : Create(
                    "source",
                    "source",
                    cd.Fqn,
                    solutionPath),
        };

        foreach (var relFqn in cd.RelatedSymbolFqns.Take(2))
            suggestions.Add(Create("describe", "describe", relFqn, solutionPath));

        if (suggestions.Count < 5)
            suggestions.Add(new ToolSuggestion(
                "rename",
                Args(cd.Fqn, solutionPath, ("new-name", "NewName"), ("dry-run", true)),
                $"rt refactor rename \"{cd.Fqn}\" NewName{sol} --dry-run"));

        return [.. suggestions.Take(5)];
    }

    /// <summary>
    ///     Produces up to 5 follow-on suggestions for an <c>rt inspect find-refs</c> result.
    /// </summary>
    internal static IReadOnlyList<ToolSuggestion> ForFindRefs(string fqn, string? solutionPath)
    {
        var sol = solutionPath != null ? $" --solution {solutionPath}" : "";
        return
        [
            Create("describe", "describe", fqn, solutionPath),
            CreateRelationship("Callers", fqn, solutionPath),
            new ToolSuggestion(
                "rename",
                Args(fqn, solutionPath, ("new-name", "NewName"), ("dry-run", true)),
                $"rt refactor rename \"{fqn}\" NewName{sol} --dry-run"),
        ];
    }

    private static ToolSuggestion CreateRelationship(
        string mode,
        string symbol,
        string? solutionPath)
    {
        var sol = solutionPath != null ? $" --solution {solutionPath}" : "";
        return new ToolSuggestion(
            "relationships",
            Args(symbol, solutionPath, ("mode", mode)),
            $"rt inspect relationships {mode.ToLowerInvariant()} \"{symbol}\"{sol}");
    }

    private static ToolSuggestion Create(
        string toolName,
        string cliName,
        string symbol,
        string? solutionPath,
        string cliGroup = "inspect",
        string argumentName = "symbol")
    {
        var sol = solutionPath != null ? $" --solution {solutionPath}" : "";
        var arguments = Args(symbol, solutionPath);
        if (!string.Equals(argumentName, "symbol", StringComparison.Ordinal))
        {
            arguments.Remove("symbol");
            arguments[argumentName] = symbol;
        }

        return new ToolSuggestion(
            toolName,
            arguments,
            $"rt {cliGroup} {cliName} \"{symbol}\"{sol}");
    }

    private static Dictionary<string, object?> Args(
        string symbol,
        string? solutionPath,
        params (string Name, object? Value)[] extras)
    {
        var args = new Dictionary<string, object?> { ["symbol"] = symbol };
        if (solutionPath is not null)
            args["solution"] = solutionPath;
        foreach (var (name, value) in extras)
            args[name] = value;
        return args;
    }
}
