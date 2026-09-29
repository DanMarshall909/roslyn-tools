namespace RoslynTools.Core.Decisions;

/// <summary>Persists decision answers without coupling Core to a file format.</summary>
internal interface IDecisionFileStore
{
    DecisionStore Load(string path);

    void Save(string path, DecisionStore store);
}
