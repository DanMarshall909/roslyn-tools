namespace RoslynTools.Core.Decisions;

/// <summary>Persists decision answers without coupling Core to a file format.</summary>
internal interface IDecisionFileStore
{
    /// <summary>Loads editable decisions, returning an empty store for a missing or empty file.</summary>
    /// <remarks>Read and format errors propagate to the host.</remarks>
    DecisionStore Load(string path);

    /// <summary>Writes the store's entries and answers to the selected file.</summary>
    /// <remarks>Write errors propagate to the host.</remarks>
    void Save(string path, DecisionStore store);
}
