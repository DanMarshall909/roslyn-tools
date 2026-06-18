namespace RoslynTools.Services.Suggest;

/// <summary>
///     Generates ready-to-run rt command suggestions from inspection command output.
///     Consumed by both CLI and MCP adapters to populate <c>_suggestions</c> in JSON responses.
/// </summary>
internal static class SuggestionHelper
{
    /// <summary>
    ///     Produces up to 5 follow-on suggestions for an <c>rt inspect describe</c> result.
    ///     Static routing (find-refs, find-callers, find-impl/analyze-interface) is ordered first
    ///     so the most universally useful commands appear even when there are no related types.
    ///     <see cref="CompactDescription.RelatedSymbolFqns" /> seeds additional <c>describe</c>
    ///     entries using the remaining slots.
    /// </summary>
    internal static IReadOnlyList<string> ForDescribe(CompactDescription cd, string? solutionPath)
    {
        var sol = solutionPath != null ? $" --solution {solutionPath}" : "";

        var suggestions = new List<string>
        {
            $"rt inspect find-refs \"{cd.Fqn}\"{sol}",
            $"rt inspect find-callers \"{cd.Fqn}\"{sol}",
            cd.Kind.Equals("Interface", StringComparison.OrdinalIgnoreCase)
                ? $"rt inspect find-impl \"{cd.Fqn}\"{sol}"
                : $"rt analyze analyze-interface \"{cd.Fqn}\"{sol}",
        };

        foreach (var relFqn in cd.RelatedSymbolFqns.Take(2))
            suggestions.Add($"rt inspect describe \"{relFqn}\"{sol}");

        if (suggestions.Count < 5)
            suggestions.Add($"rt refactor rename \"{cd.Fqn}\" NewName{sol} --dry-run");

        return [.. suggestions.Take(5)];
    }

    /// <summary>
    ///     Produces up to 5 follow-on suggestions for an <c>rt inspect find-refs</c> result.
    /// </summary>
    internal static IReadOnlyList<string> ForFindRefs(string fqn, string? solutionPath)
    {
        var sol = solutionPath != null ? $" --solution {solutionPath}" : "";
        return
        [
            $"rt inspect describe \"{fqn}\"{sol}",
            $"rt inspect find-callers \"{fqn}\"{sol}",
            $"rt refactor rename \"{fqn}\" NewName{sol} --dry-run",
        ];
    }
}
