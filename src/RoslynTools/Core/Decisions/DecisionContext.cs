namespace RoslynTools.Core.Decisions;

internal static class DecisionContext
{
    private static readonly AsyncLocal<DecisionStore?> store = new();

    public static DecisionStore? Store
    {
        get => store.Value;
        set => store.Value = value;
    }
}
