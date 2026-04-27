using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoslynTools.Core.Decisions;

internal sealed class DecisionStore
{
    private readonly List<DecisionEntry> entries;
    private readonly HashSet<string> ids;

    private DecisionStore(List<DecisionEntry> entries)
    {
        this.entries = entries;
        this.ids = new HashSet<string>(entries.Select(e => e.Id), StringComparer.Ordinal);
    }

    public static DecisionStore Empty() => new(new List<DecisionEntry>());

    public static DecisionStore Load(string path)
    {
        if (!File.Exists(path))
            return Empty();

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return Empty();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(LowerCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var loaded = deserializer.Deserialize<List<DecisionEntry>>(text) ?? new List<DecisionEntry>();
        return new DecisionStore(loaded);
    }

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

    public bool AnyPending => entries.Any(e => e.Choose is null);

    public int PendingCount => entries.Count(e => e.Choose is null);

    public void Save(string path)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(LowerCaseNamingConvention.Instance)
            .Build();

        var header =
            "# rt decisions — fill `choose:` for each entry, then re-run the same command." + Environment.NewLine +
            "# `choose:` is the integer index of the option you want." + Environment.NewLine;

        var yaml = serializer.Serialize(entries);
        File.WriteAllText(path, header + yaml);
    }
}
