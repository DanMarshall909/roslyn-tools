using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     A validated, host-neutral description of one mutation. It can combine a Roslyn
///     solution snapshot with filesystem effects without performing I/O while it is prepared.
/// </summary>
internal sealed class PreparedChangeSet
{
    private readonly IReadOnlyList<string> _affectedPaths;
    private int _applicationStarted;

    internal PreparedChangeSet(
        Solution? originalSolution = null,
        Solution? changedSolution = null,
        IReadOnlyList<PreparedFileWrite>? newFiles = null,
        IReadOnlyList<string>? deletedFiles = null,
        IReadOnlyList<PreparedFileMove>? movedFiles = null,
        IReadOnlyList<PreparedFileWrite>? projectFileEdits = null,
        IReadOnlyList<PreparedFileWrite>? textEdits = null)
    {
        if ((originalSolution is null) != (changedSolution is null))
            throw new ArgumentException("Original and changed solutions must be supplied together.");

        OriginalSolution = originalSolution;
        ChangedSolution = changedSolution;
        NewFiles = newFiles?.ToArray() ?? [];
        DeletedFiles = deletedFiles?.ToArray() ?? [];
        MovedFiles = movedFiles?.ToArray() ?? [];
        ProjectFileEdits = projectFileEdits?.ToArray() ?? [];
        TextEdits = textEdits?.ToArray() ?? [];

        _affectedPaths = BuildAffectedPaths();
    }

    internal Solution? OriginalSolution { get; }

    internal Solution? ChangedSolution { get; }

    internal IReadOnlyList<PreparedFileWrite> NewFiles { get; }

    internal IReadOnlyList<string> DeletedFiles { get; }

    internal IReadOnlyList<PreparedFileMove> MovedFiles { get; }

    internal IReadOnlyList<PreparedFileWrite> ProjectFileEdits { get; }

    internal IReadOnlyList<PreparedFileWrite> TextEdits { get; }

    internal IReadOnlyList<string> AffectedPaths => _affectedPaths;

    internal bool HasChanges => AffectedPaths.Count != 0;

    internal void BeginApplication()
    {
        if (Interlocked.CompareExchange(ref _applicationStarted, 1, 0) != 0)
            throw new InvalidOperationException("A prepared change set can be applied only once.");
    }

    private string[] BuildAffectedPaths()
    {
        ValidateMoveRoles();
        var paths = new Dictionary<string, string>(PathComparer);

        if (OriginalSolution is not null && ChangedSolution is not null)
            AddSolutionPaths(paths, OriginalSolution, ChangedSolution);

        foreach (var file in NewFiles)
            AddUnique(paths, file.Path);
        foreach (var file in DeletedFiles)
            AddUnique(paths, file);
        foreach (var move in MovedFiles)
        {
            AddUnique(paths, move.SourcePath, allowExisting: OriginalSolution is not null);
            AddUnique(paths, move.DestinationPath, allowExisting: OriginalSolution is not null);
        }
        foreach (var edit in ProjectFileEdits)
            AddUnique(paths, edit.Path);
        foreach (var edit in TextEdits)
            AddUnique(paths, edit.Path);

        return paths.Values.OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }

    private void ValidateMoveRoles()
    {
        var sources = new HashSet<string>(PathComparer);
        var destinations = new HashSet<string>(PathComparer);

        foreach (var move in MovedFiles)
        {
            var source = Path.GetFullPath(move.SourcePath);
            var destination = Path.GetFullPath(move.DestinationPath);
            if (!sources.Add(source))
                throw new ArgumentException(
                    $"Move source '{move.SourcePath}' is used more than once.",
                    nameof(MovedFiles));
            if (!destinations.Add(destination))
                throw new ArgumentException(
                    $"Move destination '{move.DestinationPath}' is targeted more than once.",
                    nameof(MovedFiles));
        }

        if (sources.Overlaps(destinations))
            throw new ArgumentException(
                "Prepared file moves cannot form chains or cycles.",
                nameof(MovedFiles));
    }

    private static void AddSolutionPaths(
        IDictionary<string, string> paths,
        Solution originalSolution,
        Solution changedSolution)
    {
        var changes = changedSolution.GetChanges(originalSolution);
        foreach (var projectChange in changes.GetProjectChanges())
        {
            foreach (var documentId in projectChange.GetChangedDocuments()
                         .Concat(projectChange.GetAddedDocuments()))
                AddRequiredDocumentPath(changedSolution, documentId, nameof(changedSolution));
            foreach (var documentId in projectChange.GetRemovedDocuments())
                AddRequiredDocumentPath(originalSolution, documentId, nameof(originalSolution));
        }

        void AddRequiredDocumentPath(Solution source, DocumentId documentId, string parameterName)
        {
            var path = source.GetDocument(documentId)?.FilePath;
            if (path is null)
            {
                throw new ArgumentException(
                    $"Affected document '{documentId}' has no file path; an exact preview is not possible.",
                    parameterName);
            }
            AddUnique(paths, path);
        }

        foreach (var project in changes.GetAddedProjects())
            AddProjectPaths(paths, project);
        foreach (var project in changes.GetRemovedProjects())
            AddProjectPaths(paths, project);
    }

    private static void AddProjectPaths(IDictionary<string, string> paths, Project? project)
    {
        if (project is null)
            return;

        if (project.FilePath is not null)
            AddUnique(paths, project.FilePath);
        foreach (var document in project.Documents)
            if (document.FilePath is not null)
                AddUnique(paths, document.FilePath);
    }

    private static void AddUnique(
        IDictionary<string, string> paths,
        string path,
        bool allowExisting = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var displayPath = NormalizeDisplayPath(path);
        var comparisonPath = Path.GetFullPath(displayPath);
        if (!paths.TryAdd(comparisonPath, displayPath) && !allowExisting)
            throw new ArgumentException($"Path '{displayPath}' is affected more than once.", nameof(path));
    }

    private static string NormalizeDisplayPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.IsPathFullyQualified(path)
            ? fullPath
            : Path.GetRelativePath(Directory.GetCurrentDirectory(), fullPath)
                .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}

/// <summary>Content for a new file or an edit to an existing text/project file.</summary>
internal sealed record PreparedFileWrite
{
    internal PreparedFileWrite(string path, string content, string? expectedContent = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);
        Path = path;
        Content = content;
        ExpectedContent = expectedContent;
    }

    internal string Path { get; }

    internal string Content { get; }

    internal string? ExpectedContent { get; }
}

/// <summary>A filesystem move whose source and destination are both affected paths.</summary>
internal sealed record PreparedFileMove
{
    internal PreparedFileMove(string sourcePath, string destinationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        if (pathComparer.Equals(
                System.IO.Path.GetFullPath(sourcePath),
                System.IO.Path.GetFullPath(destinationPath)))
            throw new ArgumentException("A file move must change the path.", nameof(destinationPath));

        SourcePath = sourcePath;
        DestinationPath = destinationPath;
    }

    internal string SourcePath { get; }

    internal string DestinationPath { get; }
}

/// <summary>The I/O boundary used to execute an already validated change set.</summary>
internal interface IPreparedChangeSetWriter
{
    Task ApplyAsync(PreparedChangeSet changeSet, CancellationToken cancellationToken = default);
}

/// <summary>Executes a solution-only prepared set through the existing change writer.</summary>
internal sealed class SolutionPreparedChangeSetWriter(ISolutionChangeWriter solutionChangeWriter)
    : IPreparedChangeSetWriter
{
    private readonly ISolutionChangeWriter _solutionChangeWriter = solutionChangeWriter;

    /// <summary>
    ///     Applies a non-empty, solution-only prepared set through the existing solution writer.
    /// </summary>
    /// <param name="changeSet">The already validated Roslyn solution delta to write.</param>
    /// <param name="cancellationToken">
    ///     Cancellation observed before writing begins. The underlying legacy writer has no
    ///     cancellation seam once application starts.
    /// </param>
    /// <returns>A task that completes after the solution writer finishes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="changeSet" /> is null.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested before writing.</exception>
    /// <exception cref="InvalidOperationException">
    ///     The set has no paired Roslyn snapshots or also contains standalone filesystem changes.
    /// </exception>
    public async Task ApplyAsync(
        PreparedChangeSet changeSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        cancellationToken.ThrowIfCancellationRequested();

        if (changeSet.OriginalSolution is null || changeSet.ChangedSolution is null)
            throw new InvalidOperationException("The solution writer requires a Roslyn solution change.");

        if (changeSet.NewFiles.Count != 0
            || changeSet.DeletedFiles.Count != 0
            || changeSet.MovedFiles.Count != 0
            || changeSet.ProjectFileEdits.Count != 0
            || changeSet.TextEdits.Count != 0)
            throw new InvalidOperationException("The solution writer cannot apply standalone filesystem changes.");

        await _solutionChangeWriter.ApplyChangesAsync(
            changeSet.OriginalSolution,
            changeSet.ChangedSolution,
            dryRun: false);
    }
}

/// <summary>Creates mutation envelopes and enforces at-most-once prepared-set application.</summary>
internal sealed class PreparedChangeSetExecutor(IPreparedChangeSetWriter writer)
{
    private readonly IPreparedChangeSetWriter _writer = writer;

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Preview and Apply are intentionally one execution policy surface.")]
    internal MutationExecutionResult<T> Preview<T>(
        T? value,
        PreparedChangeSet changeSet,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        EnsureNotEmpty(changeSet);
        return MutationExecutionResult<T>.DryRun(value, changeSet.AffectedPaths, diagnostics);
    }

    internal async Task<MutationExecutionResult<T>> ApplyAsync<T>(
        T? value,
        PreparedChangeSet changeSet,
        IReadOnlyList<MutationDiagnostic>? diagnostics = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        EnsureNotEmpty(changeSet);
        cancellationToken.ThrowIfCancellationRequested();
        changeSet.BeginApplication();
        await _writer.ApplyAsync(changeSet, cancellationToken);
        return MutationExecutionResult<T>.Applied(value, changeSet.AffectedPaths, diagnostics);
    }

    private static void EnsureNotEmpty(PreparedChangeSet changeSet)
    {
        if (!changeSet.HasChanges)
            throw new InvalidOperationException("An empty prepared change set cannot be previewed or applied.");
    }
}
