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

    /// <summary>Gets a live read-only list view of the stored decision entries.</summary>
    public IReadOnlyList<DecisionEntry> Entries => entries.AsReadOnly();

    /// <summary>Creates a store from a shallow copy of entries, preserving their order and answers.</summary>
    /// <remarks>Entry objects are shared. Existing duplicate IDs are retained; later Add calls for an existing ID are ignored.</remarks>
    /// <returns>A new store with its own entry list and ID index.</returns>
    /// <exception cref="ArgumentNullException">The entries sequence is null.</exception>
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
