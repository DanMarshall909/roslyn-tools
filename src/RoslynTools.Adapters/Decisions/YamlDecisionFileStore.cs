using RoslynTools.Core.Decisions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace RoslynTools.Adapters.Decisions;

/// <summary>Preserves the editable YAML decision-file contract.</summary>
internal sealed class YamlDecisionFileStore : IDecisionFileStore
{
    public DecisionStore Load(string path)
    {
        if (!File.Exists(path))
            return DecisionStore.Empty();

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            return DecisionStore.Empty();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(LowerCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        return DecisionStore.FromEntries(deserializer.Deserialize<List<DecisionEntry>>(text) ?? []);
    }

    public void Save(string path, DecisionStore store)
    {
        var serializer = new SerializerBuilder()
            .WithNamingConvention(LowerCaseNamingConvention.Instance)
            .Build();
        var header =
            "# rt decisions — fill `choose:` for each entry, then re-run the same command." + Environment.NewLine +
            "# `choose:` is the integer index of the option you want." + Environment.NewLine;
        File.WriteAllText(path, header + serializer.Serialize(store.Entries));
    }
}
