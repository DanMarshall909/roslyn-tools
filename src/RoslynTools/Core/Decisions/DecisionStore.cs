namespace RoslynTools.Core.Decisions;

/// <summary>
/// DecisionStore
/// </summary>
internal sealed class DecisionStore
{
    private readonly List<DecisionEntry> entries;
    private readonly HashSet<string> ids;

    private DecisionStore(List<DecisionEntry> entries)
    {
        this.entries = entries;
        ids = new HashSet<string>(entries.Select(e => e.Id), StringComparer.Ordinal);
    }

    public int PendingCount => entries.Count(e => e.Choose is null);

    public bool AnyPending => PendingCount > 0;

    public static DecisionStore Empty()
    {
        return new DecisionStore(new List<DecisionEntry>());
    }

    public IReadOnlyList<DecisionEntry> Entries => entries.AsReadOnly();

    public static DecisionStore FromEntries(IEnumerable<DecisionEntry> entries) => new(entries.ToList());

    public bool TryGetAnswer(string id, out int choice)
    {
        var entry = entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.Ordinal));
        if (entry?.Choose is int c)
        {
            choice = c;
            return true;
        }

        choice = -1;
        return false;
    }

    public void Add(DecisionEntry entry)
    {
        if (ids.Add(entry.Id))
            entries.Add(entry);
    }
}
