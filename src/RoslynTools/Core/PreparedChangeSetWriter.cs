using System.Runtime.ExceptionServices;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     Filesystem seam for staging, commit, rollback, and deterministic failure injection.
/// </summary>
internal interface IPreparedFileSystem
{
    bool FileExists(string path);

    bool DirectoryExists(string path);

    bool DirectoryIsEmpty(string path);

    IEnumerable<string> EnumerateFiles(string path);

    void CreateDirectory(string path);

    void DeleteDirectory(string path);

    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    void MoveFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);

    string ReadAllText(string path);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);
}

/// <summary>Physical filesystem implementation used by the prepared mutation boundary.</summary>
internal sealed class PhysicalPreparedFileSystem : IPreparedFileSystem
{
    bool IPreparedFileSystem.FileExists(string path) => File.Exists(path);

    bool IPreparedFileSystem.DirectoryExists(string path) => Directory.Exists(path);

    bool IPreparedFileSystem.DirectoryIsEmpty(string path) =>
        !Directory.EnumerateFileSystemEntries(path).Any();

    IEnumerable<string> IPreparedFileSystem.EnumerateFiles(string path) =>
        Directory.EnumerateFiles(path);

    void IPreparedFileSystem.CreateDirectory(string path) => Directory.CreateDirectory(path);

    void IPreparedFileSystem.DeleteDirectory(string path) => Directory.Delete(path);

    void IPreparedFileSystem.CopyFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    void IPreparedFileSystem.MoveFile(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    void IPreparedFileSystem.DeleteFile(string path) => File.Delete(path);

    string IPreparedFileSystem.ReadAllText(string path) => File.ReadAllText(path);

    Task IPreparedFileSystem.WriteAllTextAsync(
        string path,
        string content,
        CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(path, content, cancellationToken);
}

/// <summary>
///     Applies a prepared Roslyn and filesystem set using staged writes and compensating
///     rollback. Cancellation is observed until commit starts and ignored during commit.
/// </summary>
internal sealed class PreparedChangeSetWriter : IPreparedChangeSetWriter
{
    private readonly IPreparedFileSystem _fileSystem;
    private readonly string? _repositoryRoot;

    internal PreparedChangeSetWriter(IPreparedFileSystem fileSystem, string? repositoryRoot = null)
    {
        _fileSystem = fileSystem;
        _repositoryRoot = repositoryRoot;
    }

    /// <summary>
    ///     Requires a trusted repository root and checks every affected path before staging.
    ///     It then stages and commits without observing cancellation mid-commit. A failed
    ///     commit restores affected files from backup; failed restoration retains named
    ///     recovery evidence. Path checks do not exclude concurrent filesystem swaps.
    /// </summary>
    /// <param name="changeSet">The validated, non-replayable mutation description.</param>
    /// <param name="cancellationToken">Cancellation observed before commit starts.</param>
    /// <exception cref="IOException">
    ///     Trust or path containment cannot be proven, or a filesystem effect fails.
    /// </exception>
    public async Task ApplyAsync(
        PreparedChangeSet changeSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changeSet);
        cancellationToken.ThrowIfCancellationRequested();

        var plan = await BuildPlanAsync(changeSet, cancellationToken);
        ValidateAffectedPaths(changeSet, plan, ResolveRepositoryRoot(changeSet));
        ValidatePlan(plan);
        ValidateCurrentState(plan);

        var transactionId = Guid.NewGuid().ToString("N");
        var recoveryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rt-prepared-{transactionId}");
        var stagedWrites = new List<StagedWrite>();
        var backups = new List<Backup>();
        var createdDirectories = new List<string>();
        var commitStarted = false;

        try
        {
            _fileSystem.CreateDirectory(recoveryDirectory);
            await StageAsync(
                plan,
                transactionId,
                recoveryDirectory,
                stagedWrites,
                backups,
                createdDirectories,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            ValidateCurrentState(plan);
            commitStarted = true;
            Commit(plan, stagedWrites, createdDirectories);
        }
        catch (Exception commitFailure)
        {
            var rollbackFailures = commitStarted
                ? RollBack(
                    plan,
                    stagedWrites,
                    backups,
                    createdDirectories)
                : CleanPreparation(stagedWrites, createdDirectories);
            if (rollbackFailures.Count != 0)
            {
                throw new IOException(
                    "Prepared mutation application and rollback both failed. "
                    + $"Recovery evidence was retained at '{recoveryDirectory}'.",
                    new AggregateException([commitFailure, .. rollbackFailures]));
            }

            TryDeleteRecoveryDirectory(recoveryDirectory);
            ExceptionDispatchInfo.Capture(commitFailure).Throw();
            throw;
        }

        CleanStagedFiles(stagedWrites);
        TryDeleteRecoveryDirectory(recoveryDirectory);
    }

    private string ResolveRepositoryRoot(PreparedChangeSet changeSet)
    {
        if (_repositoryRoot is not null)
            return RepositoryTrustRegistry.CanonicalizeExistingPath(Path.GetFullPath(_repositoryRoot));

        if (RepositoryTrustContext.TrustedRepositoryRoot is { Length: > 0 } trustedRoot)
            return RepositoryTrustRegistry.CanonicalizeExistingPath(Path.GetFullPath(trustedRoot));

        if (changeSet.OriginalSolution?.FilePath is { Length: > 0 } solutionPath)
            return RepositoryTrustContext.ResolveWorkspaceIdentity(solutionPath).WorktreeRoot;

        throw new IOException("Prepared mutation has no trusted repository root; no file was staged.");
    }

    private static void ValidateAffectedPaths(
        PreparedChangeSet changeSet,
        MutationPlan plan,
        string repositoryRoot)
    {
        if (!Directory.Exists(repositoryRoot))
            throw new IOException(
                $"Trusted repository root '{repositoryRoot}' does not exist; no file was staged.");

        foreach (var write in changeSet.NewFiles
                     .Concat(changeSet.ProjectFileEdits)
                     .Concat(changeSet.TextEdits))
            ValidateContainedPath(write.Path, repositoryRoot);
        foreach (var deletion in changeSet.DeletedFiles)
            ValidateContainedPath(deletion, repositoryRoot);
        foreach (var move in changeSet.MovedFiles)
        {
            ValidateContainedPath(move.SourcePath, repositoryRoot);
            ValidateContainedPath(move.DestinationPath, repositoryRoot);
        }

        if (changeSet.OriginalSolution is not null && changeSet.ChangedSolution is not null)
        {
            var changes = changeSet.ChangedSolution.GetChanges(changeSet.OriginalSolution);
            foreach (var projectChange in changes.GetProjectChanges())
            {
                foreach (var documentId in projectChange.GetChangedDocuments()
                             .Concat(projectChange.GetAddedDocuments())
                             .Concat(projectChange.GetRemovedDocuments()))
                {
                    var path = changeSet.ChangedSolution.GetDocument(documentId)?.FilePath
                               ?? changeSet.OriginalSolution.GetDocument(documentId)?.FilePath;
                    if (path is not null)
                        ValidateContainedPath(path, repositoryRoot);
                }
            }
        }

        foreach (var path in plan.Writes.Select(write => write.Path)
                     .Concat(plan.Deletions.Select(deletion => deletion.Path))
                     .Concat(plan.Moves.SelectMany(move => new[] { move.SourcePath, move.DestinationPath }))
                     .Concat(plan.AffectedPaths))
            ValidateContainedPath(path, repositoryRoot);
    }

    private static void ValidateContainedPath(string path, string repositoryRoot)
    {
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains("..", StringComparer.Ordinal))
            throw new IOException($"Prepared path '{path}' contains parent traversal; no file was staged.");

        var fullPath = Path.GetFullPath(path);
        if (!RepositoryTrustRegistry.IsContained(repositoryRoot, fullPath)
            || PathComparer.Equals(repositoryRoot, fullPath))
            throw new IOException(
                $"Prepared path '{path}' is outside the trusted repository root '{repositoryRoot}'; no file was staged.");

        var current = Path.GetPathRoot(fullPath)!;
        ValidateExistingComponent(current, path);
        foreach (var segment in Path.GetRelativePath(current, fullPath)
                     .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, segment);
            try
            {
                ValidateExistingComponent(current, path);
            }
            catch (Exception exception) when (exception is FileNotFoundException
                                               or DirectoryNotFoundException)
            {
                break;
            }
        }
    }

    private static void ValidateExistingComponent(string component, string preparedPath)
    {
        if ((File.GetAttributes(component) & FileAttributes.ReparsePoint) != 0)
            throw new IOException(
                $"Prepared path '{preparedPath}' traverses a symbolic link or reparse point at '{component}'; no file was staged.");
    }

    private List<Exception> CleanPreparation(
        IEnumerable<StagedWrite> stagedWrites,
        IEnumerable<string> createdDirectories)
    {
        var failures = new List<Exception>();
        CleanStagedFiles(stagedWrites, failures);
        RemoveCreatedDirectories(createdDirectories, failures);
        return failures;
    }

    private static async Task<MutationPlan> BuildPlanAsync(
        PreparedChangeSet changeSet,
        CancellationToken cancellationToken)
    {
        var writes = new List<PlannedWrite>();
        var deletions = new List<PlannedDeletion>();

        if (changeSet.OriginalSolution is not null && changeSet.ChangedSolution is not null)
        {
            var changes = changeSet.ChangedSolution.GetChanges(changeSet.OriginalSolution);
            if (changes.GetAddedProjects().Any() || changes.GetRemovedProjects().Any())
                throw new NotSupportedException(
                    "Prepared filesystem application does not support adding or removing projects.");

            foreach (var projectChange in changes.GetProjectChanges())
            {
                foreach (var documentId in projectChange.GetChangedDocuments())
                {
                    var original = changeSet.OriginalSolution.GetDocument(documentId)!;
                    var changed = changeSet.ChangedSolution.GetDocument(documentId)!;
                    writes.Add(new PlannedWrite(
                        RequirePath(changed),
                        (await changed.GetTextAsync(cancellationToken)).ToString(),
                        MustExist: true,
                        (await original.GetTextAsync(cancellationToken)).ToString()));
                }

                foreach (var documentId in projectChange.GetAddedDocuments())
                {
                    var document = changeSet.ChangedSolution.GetDocument(documentId)!;
                    writes.Add(new PlannedWrite(
                        RequirePath(document),
                        (await document.GetTextAsync(cancellationToken)).ToString(),
                        MustExist: false,
                        ExpectedContent: null));
                }

                foreach (var documentId in projectChange.GetRemovedDocuments())
                {
                    var document = changeSet.OriginalSolution.GetDocument(documentId)!;
                    deletions.Add(new PlannedDeletion(
                        RequirePath(document),
                        (await document.GetTextAsync(cancellationToken)).ToString()));
                }
            }
        }

        writes.AddRange(changeSet.NewFiles.Select(file => new PlannedWrite(
            Path.GetFullPath(file.Path),
            file.Content,
            MustExist: false,
            file.ExpectedContent)));
        writes.AddRange(changeSet.ProjectFileEdits
            .Concat(changeSet.TextEdits)
            .Select(file => new PlannedWrite(
                Path.GetFullPath(file.Path),
                file.Content,
                MustExist: true,
                file.ExpectedContent)));
        deletions.AddRange(changeSet.DeletedFiles.Select(path =>
            new PlannedDeletion(Path.GetFullPath(path), ExpectedContent: null)));

        var moves = changeSet.MovedFiles.Select(move => new PlannedMove(
            Path.GetFullPath(move.SourcePath),
            Path.GetFullPath(move.DestinationPath))).ToArray();
        return new MutationPlan(writes, deletions, moves, changeSet.AffectedPaths);
    }

    private static string RequirePath(Document document) =>
        document.FilePath is { Length: > 0 } path
            ? Path.GetFullPath(path)
            : throw new InvalidOperationException(
                $"Prepared document '{document.Name}' has no file path.");

    private static void ValidatePlan(MutationPlan plan)
    {
        var comparer = PathComparer;
        var writePaths = new HashSet<string>(comparer);
        var deletionPaths = new HashSet<string>(comparer);
        foreach (var write in plan.Writes)
            if (!writePaths.Add(write.Path))
                throw new ArgumentException($"Path '{write.Path}' has more than one prepared write.");
        foreach (var deletion in plan.Deletions)
            if (!deletionPaths.Add(deletion.Path))
                throw new ArgumentException($"Path '{deletion.Path}' has more than one prepared deletion.");

        foreach (var move in plan.Moves)
        {
            if (!comparer.Equals(Path.GetPathRoot(move.SourcePath), Path.GetPathRoot(move.DestinationPath)))
                throw new IOException(
                    $"Cross-volume move '{move.SourcePath}' -> '{move.DestinationPath}' is not supported.");
            if (writePaths.Contains(move.DestinationPath)
                || deletionPaths.Contains(move.SourcePath)
                || deletionPaths.Contains(move.DestinationPath))
                throw new ArgumentException(
                    $"Move '{move.SourcePath}' -> '{move.DestinationPath}' conflicts with another path role.");
        }
    }

    private void ValidateCurrentState(MutationPlan plan)
    {
        foreach (var write in plan.Writes)
        {
            var exists = _fileSystem.FileExists(write.Path);
            if (write.MustExist && !exists)
                throw new FileNotFoundException("Prepared edit target was not found.", write.Path);
            if (!write.MustExist && exists)
                throw new IOException($"Cannot create '{write.Path}' because it already exists.");
            ValidateExpectedContent(write.Path, write.ExpectedContent);
        }

        foreach (var deletion in plan.Deletions)
        {
            if (!_fileSystem.FileExists(deletion.Path))
                throw new FileNotFoundException("Prepared deletion target was not found.", deletion.Path);
            ValidateExpectedContent(deletion.Path, deletion.ExpectedContent);
        }

        foreach (var move in plan.Moves)
        {
            if (!_fileSystem.FileExists(move.SourcePath))
                throw new FileNotFoundException("Prepared move source was not found.", move.SourcePath);
            if (_fileSystem.FileExists(move.DestinationPath))
                throw new IOException(
                    $"Cannot move '{move.SourcePath}' to '{move.DestinationPath}' because the destination already exists.");
        }
    }

    private void ValidateExpectedContent(string path, string? expectedContent)
    {
        if (expectedContent is not null
            && !string.Equals(
                _fileSystem.ReadAllText(path),
                expectedContent,
                StringComparison.Ordinal))
            throw new IOException(
                $"Prepared input '{path}' changed after preparation; rebuild the change set before applying it.");
    }

    private async Task StageAsync(
        MutationPlan plan,
        string transactionId,
        string recoveryDirectory,
        ICollection<StagedWrite> stagedWrites,
        ICollection<Backup> backups,
        ICollection<string> createdDirectories,
        CancellationToken cancellationToken)
    {
        var affectedFullPaths = plan.AffectedPaths
            .Select(Path.GetFullPath)
            .Distinct(PathComparer)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        for (var index = 0; index < affectedFullPaths.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = affectedFullPaths[index];
            if (!_fileSystem.FileExists(path))
                continue;

            var backupPath = Path.Combine(recoveryDirectory, $"{index:D4}.backup");
            _fileSystem.CopyFile(path, backupPath, overwrite: false);
            backups.Add(new Backup(path, backupPath));
        }

        foreach (var write in plan.Writes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = Path.GetDirectoryName(write.Path)!;
            EnsureDirectory(directory, createdDirectories);
            var stagePath = Path.Combine(
                directory,
                $".{Path.GetFileName(write.Path)}.rt-{transactionId}.stage");
            await _fileSystem.WriteAllTextAsync(
                stagePath,
                write.Content,
                cancellationToken);
            stagedWrites.Add(new StagedWrite(write.Path, stagePath));
        }

        foreach (var move in plan.Moves)
            EnsureDirectory(Path.GetDirectoryName(move.DestinationPath)!, createdDirectories);

        var manifest = string.Join(
            Environment.NewLine,
            affectedFullPaths.Select(path => path));
        await _fileSystem.WriteAllTextAsync(
            Path.Combine(recoveryDirectory, "affected-paths.txt"),
            manifest,
            cancellationToken);
    }

    private void Commit(
        MutationPlan plan,
        IEnumerable<StagedWrite> stagedWrites,
        ICollection<string> createdDirectories)
    {
        foreach (var staged in stagedWrites)
            _fileSystem.MoveFile(staged.StagePath, staged.TargetPath, overwrite: true);

        foreach (var move in plan.Moves)
        {
            EnsureDirectory(Path.GetDirectoryName(move.DestinationPath)!, createdDirectories);
            _fileSystem.MoveFile(move.SourcePath, move.DestinationPath, overwrite: false);
        }

        foreach (var deletion in plan.Deletions)
            _fileSystem.DeleteFile(deletion.Path);
    }

    private List<Exception> RollBack(
        MutationPlan plan,
        IEnumerable<StagedWrite> stagedWrites,
        IEnumerable<Backup> backups,
        IEnumerable<string> createdDirectories)
    {
        var failures = new List<Exception>();
        foreach (var path in plan.AffectedPaths
                     .Select(Path.GetFullPath)
                     .Distinct(PathComparer)
                     .Reverse())
        {
            try
            {
                if (_fileSystem.FileExists(path))
                    _fileSystem.DeleteFile(path);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        foreach (var backup in backups.Reverse())
        {
            try
            {
                EnsureDirectory(Path.GetDirectoryName(backup.OriginalPath)!, []);
                _fileSystem.CopyFile(backup.BackupPath, backup.OriginalPath, overwrite: true);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        CleanStagedFiles(stagedWrites, failures);
        RemoveCreatedDirectories(createdDirectories, failures);
        return failures;
    }

    private void EnsureDirectory(string directory, ICollection<string> createdDirectories)
    {
        var missing = new Stack<string>();
        for (var current = directory;
             !string.IsNullOrEmpty(current) && !_fileSystem.DirectoryExists(current);
             current = Path.GetDirectoryName(current))
            missing.Push(current);

        while (missing.TryPop(out var path))
        {
            _fileSystem.CreateDirectory(path);
            createdDirectories.Add(path);
        }
    }

    private void CleanStagedFiles(
        IEnumerable<StagedWrite> stagedWrites,
        List<Exception>? failures = null)
    {
        foreach (var staged in stagedWrites)
        {
            try
            {
                if (_fileSystem.FileExists(staged.StagePath))
                    _fileSystem.DeleteFile(staged.StagePath);
            }
            catch (Exception exception)
            {
                failures?.Add(exception);
            }
        }
    }

    private void RemoveCreatedDirectories(
        IEnumerable<string> createdDirectories,
        List<Exception> failures)
    {
        foreach (var directory in createdDirectories.Reverse())
        {
            try
            {
                if (_fileSystem.DirectoryExists(directory)
                    && _fileSystem.DirectoryIsEmpty(directory))
                    _fileSystem.DeleteDirectory(directory);
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }
    }

    private void TryDeleteRecoveryDirectory(string recoveryDirectory)
    {
        try
        {
            if (!_fileSystem.DirectoryExists(recoveryDirectory))
                return;

            foreach (var file in _fileSystem.EnumerateFiles(recoveryDirectory))
                _fileSystem.DeleteFile(file);
            if (_fileSystem.DirectoryIsEmpty(recoveryDirectory))
                _fileSystem.DeleteDirectory(recoveryDirectory);
        }
        catch
        {
            // A leftover recovery folder is safe and intentionally preferable to obscuring
            // the mutation result because best-effort cleanup itself failed.
        }
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record MutationPlan(
        IReadOnlyList<PlannedWrite> Writes,
        IReadOnlyList<PlannedDeletion> Deletions,
        IReadOnlyList<PlannedMove> Moves,
        IReadOnlyList<string> AffectedPaths);

    private sealed record PlannedWrite(
        string Path,
        string Content,
        bool MustExist,
        string? ExpectedContent);

    private sealed record PlannedDeletion(string Path, string? ExpectedContent);

    private sealed record PlannedMove(string SourcePath, string DestinationPath);

    private sealed record StagedWrite(string TargetPath, string StagePath);

    private sealed record Backup(string OriginalPath, string BackupPath);
}
