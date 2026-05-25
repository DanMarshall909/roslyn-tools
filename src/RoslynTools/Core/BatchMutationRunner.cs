namespace RoslynTools.Core;

/// <summary>
/// Pipeline for batch mutation commands: takes a candidate fix list plus the user's
/// --apply / --skip CSVs, validates flag exclusivity and ID existence, and returns
/// the surviving fix set (or errors for the host to print and exit 1).
/// </summary>
internal static class BatchMutationRunner
{
    public static BatchSelection Select(
        IReadOnlyList<Fix> candidates,
        string? applyCsv,
        string? skipCsv)
    {
        var hasApply = !string.IsNullOrWhiteSpace(applyCsv);
        var hasSkip = !string.IsNullOrWhiteSpace(skipCsv);

        if (hasApply && hasSkip)
            return Error("--apply and --skip cannot be combined in the same invocation.");

        if (hasApply)
            return Filter(candidates, applyCsv!, keepRequested: true);

        if (hasSkip)
            return Filter(candidates, skipCsv!, keepRequested: false);

        // Explicit empty/whitespace --apply with no --skip: select nothing, no error.
        if (applyCsv is not null)
            return new BatchSelection(Array.Empty<Fix>(), Array.Empty<string>());

        return new BatchSelection(candidates, Array.Empty<string>());
    }

    private static BatchSelection Filter(IReadOnlyList<Fix> candidates, string csv, bool keepRequested)
    {
        var requested = csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        var knownIds = candidates.Select(f => f.Id).ToHashSet(StringComparer.Ordinal);
        var unknown = requested.Where(id => !knownIds.Contains(id)).ToList();
        if (unknown.Count > 0)
            return Error($"Unknown fix ID(s): {string.Join(", ", unknown)}");

        var surviving = candidates
            .Where(f => requested.Contains(f.Id) == keepRequested)
            .ToList();
        return new BatchSelection(surviving, Array.Empty<string>());
    }

    private static BatchSelection Error(string message) =>
        new(Array.Empty<Fix>(), [message]);
}

internal sealed record BatchSelection(
    IReadOnlyList<Fix> Surviving,
    IReadOnlyList<string> Errors);
