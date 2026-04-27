namespace RoslynTools.Core.Decisions;

internal sealed class DecisionEntry
{
    public string Id { get; set; } = "";
    public string Q { get; set; } = "";
    public Dictionary<int, string> Options { get; set; } = new();
    public int? Choose { get; set; }
}
