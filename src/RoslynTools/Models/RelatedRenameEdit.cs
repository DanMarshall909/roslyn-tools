namespace RoslynTools.Models;

internal sealed record RelatedRenameEdit(
    string Id,
    string Kind,
    string OldText,
    string NewText,
    IReadOnlyList<string> Paths,
    int Occurrences,
    bool Prose);

internal sealed record RelatedRenamePlan(
    IReadOnlyList<RelatedRenameEdit> Edits);

internal sealed record RenameRequest(
    string Symbol,
    string NewName,
    string? Kind,
    bool DryRun,
    IReadOnlySet<string> RelatedEditIds);

internal sealed record RenameResult(
    string Symbol,
    string NewName,
    int ChangedFiles,
    IReadOnlyList<string> ChangedFilePaths,
    bool Applied,
    IReadOnlyList<string> SelectedRelatedEdits,
    IReadOnlyList<RelatedRenameEdit> RelatedRenamePlan);
