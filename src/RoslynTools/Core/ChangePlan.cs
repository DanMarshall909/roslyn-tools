using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace RoslynTools.Core;

internal static class ChangePlanSchema
{
    internal const string Version = "rt.change-plan.v1";
    internal const string HashAlgorithm = "SHA-256";
}

internal enum ChangePlanEffectKind
{
    Create,
    Replace,
    Delete,
    Move
}

internal enum ChangePlanArtifactKind
{
    File,
    ProjectFile,
    RoslynDocument,
    AdditionalDocument,
    AnalyzerConfigDocument
}

internal enum ChangePlanPathKind
{
    Missing,
    RegularFile,
    Directory,
    SymbolicLink,
    Other
}

internal enum ChangePlanGuardKind
{
    ExternalWriteGuard,
    ConditionalEffect
}

internal enum ChangePlanDependencyKind
{
    SemanticInput,
    ProjectInput,
    DirectoryEntry
}

internal enum ChangePlanCoordinationDomain
{
    ExclusivePrincipal,
    SharedAuthority
}

internal enum ChangePlanCoordinationMode
{
    ExternalWriteGuards,
    ConditionalEffects
}

internal enum ChangePlanCaseSensitivity
{
    Sensitive,
    Insensitive
}

internal enum ChangePlanLongPathPolicy
{
    Reject,
    Allow
}

internal enum ChangePlanReadOnlyPolicy
{
    Reject,
    Preserve,
    Replace
}

internal enum ChangePlanSymlinkPolicy
{
    Reject,
    PreserveLink,
    FollowTarget
}

internal enum ChangePlanHardLinkPolicy
{
    Reject,
    Preserve,
    BreakByReplacement
}

internal enum ChangePlanCleanupPolicy
{
    RetainUntilVerifiedTerminal
}

internal enum ChangePlanRecoveryPolicy
{
    RequireDurableJournal
}

internal sealed class ChangePlanValue
{
    internal ChangePlanValue(string name, string? value)
    {
        Name = ChangePlanValidation.Required(name, nameof(name));
        Value = value;
    }

    internal string Name { get; }
    internal string? Value { get; }
}

internal sealed class ChangePlanRepository
{
    internal ChangePlanRepository(
        string repositoryId,
        string repositoryRoot,
        string resolvedRepositoryRoot,
        string rootPlatformIdentity,
        string baseRevision,
        string sourceFingerprint)
    {
        RepositoryId = ChangePlanValidation.Required(repositoryId, nameof(repositoryId));
        RepositoryRoot = ChangePlanValidation.NormalizeAbsolutePath(repositoryRoot, nameof(repositoryRoot));
        ResolvedRepositoryRoot = ChangePlanValidation.NormalizeAbsolutePath(
            resolvedRepositoryRoot,
            nameof(resolvedRepositoryRoot));
        RootPlatformIdentity = ChangePlanValidation.Required(rootPlatformIdentity, nameof(rootPlatformIdentity));
        BaseRevision = ChangePlanValidation.Required(baseRevision, nameof(baseRevision));
        SourceFingerprint = ChangePlanValidation.NormalizeSha256(sourceFingerprint, nameof(sourceFingerprint));
    }

    internal string RepositoryId { get; }
    internal string RepositoryRoot { get; }
    internal string ResolvedRepositoryRoot { get; }
    internal string RootPlatformIdentity { get; }
    internal string BaseRevision { get; }
    internal string SourceFingerprint { get; }
}

internal sealed class ChangePlanOperation
{
    internal ChangePlanOperation(
        int sequence,
        string name,
        string stableInput,
        IEnumerable<ChangePlanValue> arguments)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Operation sequence must be positive.");

        Sequence = sequence;
        Name = ChangePlanValidation.Required(name, nameof(name));
        StableInput = ChangePlanValidation.Required(stableInput, nameof(stableInput));
        Arguments = ChangePlanValidation.FreezeValues(arguments, nameof(arguments));
    }

    internal int Sequence { get; }
    internal string Name { get; }
    internal string StableInput { get; }
    internal ImmutableArray<ChangePlanValue> Arguments { get; }
}

internal sealed class ChangePlanPathState
{
    internal ChangePlanPathState(
        bool exists,
        ChangePlanPathKind kind,
        string? contentHash,
        string? platformFileIdentity,
        int? linkCount,
        IEnumerable<string> knownHardLinkAliases,
        string? encodingName,
        bool? hasByteOrderMark,
        string? newline,
        string? fileMode,
        bool? executable,
        bool? readOnly,
        string? symlinkTarget)
    {
        ArgumentNullException.ThrowIfNull(knownHardLinkAliases);

        if (!exists)
        {
            if (kind != ChangePlanPathKind.Missing
                || contentHash is not null
                || platformFileIdentity is not null
                || linkCount is not null
                || knownHardLinkAliases.Any()
                || encodingName is not null
                || hasByteOrderMark is not null
                || newline is not null
                || fileMode is not null
                || executable is not null
                || readOnly is not null
                || symlinkTarget is not null)
            {
                throw new ArgumentException(
                    "A missing path state cannot carry file metadata.",
                    nameof(exists));
            }
        }
        else
        {
            if (kind == ChangePlanPathKind.Missing)
                throw new ArgumentException("An existing path cannot have kind Missing.", nameof(kind));
            if (kind == ChangePlanPathKind.RegularFile && contentHash is null)
                throw new ArgumentException("An existing regular file requires a content hash.", nameof(contentHash));
            if (linkCount is <= 0)
                throw new ArgumentOutOfRangeException(nameof(linkCount), "Link count must be positive when known.");
            if (kind == ChangePlanPathKind.SymbolicLink && string.IsNullOrWhiteSpace(symlinkTarget))
                throw new ArgumentException("A symbolic link requires its exact link target.", nameof(symlinkTarget));
            if (kind != ChangePlanPathKind.SymbolicLink && symlinkTarget is not null)
                throw new ArgumentException("Only a symbolic link can declare a link target.", nameof(symlinkTarget));
        }

        Exists = exists;
        Kind = kind;
        ContentHash = contentHash is null
            ? null
            : ChangePlanValidation.NormalizeSha256(contentHash, nameof(contentHash));
        PlatformFileIdentity = platformFileIdentity;
        LinkCount = linkCount;
        KnownHardLinkAliases = ChangePlanValidation.FreezeKnownPaths(
            knownHardLinkAliases,
            nameof(knownHardLinkAliases));
        EncodingName = encodingName;
        HasByteOrderMark = hasByteOrderMark;
        Newline = newline;
        FileMode = fileMode;
        Executable = executable;
        ReadOnly = readOnly;
        SymlinkTarget = symlinkTarget;
    }

    internal bool Exists { get; }
    internal ChangePlanPathKind Kind { get; }
    internal string? ContentHash { get; }
    internal string? PlatformFileIdentity { get; }
    internal int? LinkCount { get; }
    internal ImmutableArray<string> KnownHardLinkAliases { get; }
    internal string? EncodingName { get; }
    internal bool? HasByteOrderMark { get; }
    internal string? Newline { get; }
    internal string? FileMode { get; }
    internal bool? Executable { get; }
    internal bool? ReadOnly { get; }
    internal string? SymlinkTarget { get; }

    internal static ChangePlanPathState Missing { get; } = new(
        false,
        ChangePlanPathKind.Missing,
        contentHash: null,
        platformFileIdentity: null,
        linkCount: null,
        knownHardLinkAliases: [],
        encodingName: null,
        hasByteOrderMark: null,
        newline: null,
        fileMode: null,
        executable: null,
        readOnly: null,
        symlinkTarget: null);
}

internal sealed class ChangePlanGuardRequirement
{
    internal ChangePlanGuardRequirement(
        string path,
        ChangePlanGuardKind kind,
        string expectedToken)
    {
        Path = ChangePlanValidation.NormalizeRepositoryPath(path, nameof(path));
        Kind = kind;
        ExpectedToken = ChangePlanValidation.Required(expectedToken, nameof(expectedToken));
    }

    internal string Path { get; }
    internal ChangePlanGuardKind Kind { get; }
    internal string ExpectedToken { get; }
}

internal sealed class ChangePlanDependency
{
    internal ChangePlanDependency(
        string path,
        string resolvedPath,
        ChangePlanDependencyKind kind,
        ChangePlanPathState expectedState,
        ChangePlanGuardRequirement guard)
    {
        ArgumentNullException.ThrowIfNull(expectedState);
        ArgumentNullException.ThrowIfNull(guard);
        Path = ChangePlanValidation.NormalizeRepositoryPath(path, nameof(path));
        ResolvedPath = ChangePlanValidation.NormalizeAbsolutePath(resolvedPath, nameof(resolvedPath));
        if (!string.Equals(Path, guard.Path, StringComparison.Ordinal))
            throw new ArgumentException("A dependency guard must identify the dependency path.", nameof(guard));
        if (!expectedState.Exists)
            throw new ArgumentException("A reviewed read dependency must exist.", nameof(expectedState));
        if ((kind == ChangePlanDependencyKind.DirectoryEntry
             && expectedState.Kind != ChangePlanPathKind.Directory)
            || (kind != ChangePlanDependencyKind.DirectoryEntry
                && expectedState.Kind != ChangePlanPathKind.RegularFile))
        {
            throw new ArgumentException(
                "A directory-entry dependency requires a directory; semantic and project inputs require regular files.",
                nameof(expectedState));
        }

        Kind = kind;
        ExpectedState = expectedState;
        Guard = guard;
    }

    internal string Path { get; }
    internal string ResolvedPath { get; }
    internal ChangePlanDependencyKind Kind { get; }
    internal ChangePlanPathState ExpectedState { get; }
    internal ChangePlanGuardRequirement Guard { get; }
}

internal sealed class ChangePlanEffect
{
    private readonly ImmutableArray<byte> _originalContent;
    private readonly ImmutableArray<byte> _replacementContent;

    internal ChangePlanEffect(
        int sequence,
        ChangePlanEffectKind kind,
        ChangePlanArtifactKind artifactKind,
        string sourcePath,
        string sourceResolvedPath,
        string? destinationPath,
        string? destinationResolvedPath,
        ChangePlanPathState sourceBefore,
        ChangePlanPathState sourceAfter,
        ChangePlanPathState? destinationBefore,
        ChangePlanPathState? destinationAfter,
        byte[]? originalContent,
        byte[]? replacementContent,
        IEnumerable<ChangePlanGuardRequirement> guards)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Effect sequence must be positive.");
        ArgumentNullException.ThrowIfNull(sourceBefore);
        ArgumentNullException.ThrowIfNull(sourceAfter);
        ArgumentNullException.ThrowIfNull(guards);

        Sequence = sequence;
        Kind = kind;
        ArtifactKind = artifactKind;
        SourcePath = ChangePlanValidation.NormalizeRepositoryPath(sourcePath, nameof(sourcePath));
        SourceResolvedPath = ChangePlanValidation.NormalizeAbsolutePath(sourceResolvedPath, nameof(sourceResolvedPath));
        DestinationPath = destinationPath is null
            ? null
            : ChangePlanValidation.NormalizeRepositoryPath(destinationPath, nameof(destinationPath));
        DestinationResolvedPath = destinationResolvedPath is null
            ? null
            : ChangePlanValidation.NormalizeAbsolutePath(destinationResolvedPath, nameof(destinationResolvedPath));
        SourceBefore = sourceBefore;
        SourceAfter = sourceAfter;
        DestinationBefore = destinationBefore;
        DestinationAfter = destinationAfter;
        _originalContent = originalContent is null
            ? default
            : ImmutableArray.CreateRange(originalContent);
        _replacementContent = replacementContent is null
            ? default
            : ImmutableArray.CreateRange(replacementContent);
        var guardArray = guards.ToArray();
        if (guardArray.Any(requirement => requirement is null))
            throw new ArgumentException("Guard requirements cannot contain null values.", nameof(guards));
        Guards = guardArray
            .OrderBy(requirement => requirement.Path, StringComparer.Ordinal)
            .ToImmutableArray();
        if (Guards.Select(requirement => requirement.Path)
                .Distinct(StringComparer.Ordinal).Count() != Guards.Length)
        {
            throw new ArgumentException(
                "An effect must declare one unique guard requirement for each affected path.",
                nameof(guards));
        }

        ValidateShape(originalContent, replacementContent);
        ValidateGuards();
    }

    internal int Sequence { get; }
    internal ChangePlanEffectKind Kind { get; }
    internal ChangePlanArtifactKind ArtifactKind { get; }
    internal string SourcePath { get; }
    internal string SourceResolvedPath { get; }
    internal string? DestinationPath { get; }
    internal string? DestinationResolvedPath { get; }
    internal ChangePlanPathState SourceBefore { get; }
    internal ChangePlanPathState SourceAfter { get; }
    internal ChangePlanPathState? DestinationBefore { get; }
    internal ChangePlanPathState? DestinationAfter { get; }
    internal ImmutableArray<byte> OriginalContent => _originalContent;
    internal bool HasOriginalContent => !_originalContent.IsDefault;
    internal ImmutableArray<byte> ReplacementContent => _replacementContent;
    internal bool HasReplacementContent => !_replacementContent.IsDefault;
    internal ImmutableArray<ChangePlanGuardRequirement> Guards { get; }

    internal IEnumerable<string> AffectedPaths
    {
        get
        {
            yield return SourcePath;
            if (DestinationPath is not null)
                yield return DestinationPath;
        }
    }

    private void ValidateShape(byte[]? originalContent, byte[]? replacementContent)
    {
        ValidateFileStates();
        switch (Kind)
        {
            case ChangePlanEffectKind.Create:
                RequireNoDestination();
                RequireStates(sourceMustExistBefore: false, sourceMustExistAfter: true);
                if (originalContent is not null)
                    throw new ArgumentException("A create effect cannot contain original content.", nameof(originalContent));
                RequireReplacementContent(replacementContent, SourceAfter);
                break;
            case ChangePlanEffectKind.Replace:
                RequireNoDestination();
                RequireStates(sourceMustExistBefore: true, sourceMustExistAfter: true);
                RequireOriginalContent(originalContent);
                RequireReplacementContent(replacementContent, SourceAfter);
                break;
            case ChangePlanEffectKind.Delete:
                RequireNoDestination();
                RequireStates(sourceMustExistBefore: true, sourceMustExistAfter: false);
                RequireOriginalContent(originalContent);
                if (replacementContent is not null)
                    throw new ArgumentException("A delete effect cannot contain replacement content.", nameof(replacementContent));
                break;
            case ChangePlanEffectKind.Move:
                if (DestinationPath is null
                    || DestinationResolvedPath is null
                    || DestinationBefore is null
                    || DestinationAfter is null)
                    throw new ArgumentException("A move effect requires a destination, resolved destination, and both destination states.");
                if (string.Equals(SourcePath, DestinationPath, StringComparison.Ordinal))
                    throw new ArgumentException("A move source and destination must be different.");
                RequireStates(sourceMustExistBefore: true, sourceMustExistAfter: false);
                RequireOriginalContent(originalContent);
                if (DestinationBefore.Exists || !DestinationAfter.Exists)
                    throw new ArgumentException("A move requires an absent destination before and an existing destination after.");
                if (replacementContent is not null)
                    RequireContentHash(replacementContent, DestinationAfter);
                else if (!string.Equals(
                             SourceBefore.ContentHash,
                             DestinationAfter.ContentHash,
                             StringComparison.Ordinal))
                    throw new ArgumentException("A content-preserving move requires matching source and destination hashes.");
                break;
            default:
                throw new InvalidOperationException($"Unsupported change-plan effect kind: {Kind}.");
        }
    }

    private void ValidateFileStates()
    {
        foreach (var state in new[]
                 {
                     SourceBefore,
                     SourceAfter,
                     DestinationBefore,
                     DestinationAfter
                 }.OfType<ChangePlanPathState>().Where(state => state.Exists))
        {
            if (state.Kind != ChangePlanPathKind.RegularFile)
                throw new ArgumentException("rt.change-plan.v1 effects are restricted to regular files.");
        }
    }

    private void ValidateGuards()
    {
        var affected = AffectedPaths.Order(StringComparer.Ordinal).ToArray();
        var guarded = Guards.Select(requirement => requirement.Path)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (!affected.SequenceEqual(guarded, StringComparer.Ordinal))
        {
            throw new ArgumentException(
                "An effect must declare one guard requirement for each affected path.");
        }
    }

    private void RequireNoDestination()
    {
        if (DestinationPath is not null
            || DestinationResolvedPath is not null
            || DestinationBefore is not null
            || DestinationAfter is not null)
            throw new ArgumentException($"A {Kind} effect cannot declare a destination.");
    }

    private void RequireStates(bool sourceMustExistBefore, bool sourceMustExistAfter)
    {
        if (SourceBefore.Exists != sourceMustExistBefore || SourceAfter.Exists != sourceMustExistAfter)
            throw new ArgumentException($"The before/after states do not match a {Kind} effect.");
    }

    private static void RequireReplacementContent(byte[]? replacementContent, ChangePlanPathState after)
    {
        if (replacementContent is null)
            throw new ArgumentException("A create or replace effect requires exact replacement content.", nameof(replacementContent));
        RequireContentHash(replacementContent, after);
    }

    private void RequireOriginalContent(byte[]? originalContent)
    {
        if (originalContent is null)
            throw new ArgumentException("An existing source requires exact original content.", nameof(originalContent));
        RequireContentHash(originalContent, SourceBefore);
    }

    private static void RequireContentHash(byte[] replacementContent, ChangePlanPathState after)
    {
        var actual = Convert.ToHexString(SHA256.HashData(replacementContent)).ToLowerInvariant();
        if (!string.Equals(actual, after.ContentHash, StringComparison.Ordinal))
            throw new ArgumentException("Replacement content does not match the declared postcondition hash.", nameof(replacementContent));
    }
}

internal static class ChangePlanDiff
{
    internal static string Render(IReadOnlyList<ChangePlanEffect> effects)
    {
        ArgumentNullException.ThrowIfNull(effects);
        var result = new StringBuilder();
        foreach (var effect in effects)
        {
            var afterState = effect.Kind == ChangePlanEffectKind.Move
                ? effect.DestinationAfter!
                : effect.SourceAfter;
            var afterContent = effect.HasReplacementContent
                ? effect.ReplacementContent
                : effect.Kind == ChangePlanEffectKind.Move
                    ? effect.OriginalContent
                    : default;

            result.Append("effect ")
                .Append(effect.Sequence.ToString(CultureInfo.InvariantCulture))
                .Append(' ')
                .Append(EffectWireName(effect.Kind))
                .Append(' ')
                .Append(ArtifactWireName(effect.ArtifactKind))
                .Append('\n');
            result.Append("source ").Append(effect.SourcePath).Append('\n');
            result.Append("destination ").Append(effect.DestinationPath ?? "null").Append('\n');
            result.Append("beforeSha256 ").Append(effect.SourceBefore.ContentHash ?? "absent").Append('\n');
            result.Append("afterSha256 ").Append(afterState.ContentHash ?? "absent").Append('\n');
            result.Append("beforeContentBase64 ")
                .Append(effect.HasOriginalContent
                    ? Convert.ToBase64String(effect.OriginalContent.AsSpan())
                    : "null")
                .Append('\n');
            result.Append("afterContentBase64 ")
                .Append(afterContent.IsDefault
                    ? "null"
                    : Convert.ToBase64String(afterContent.AsSpan()))
                .Append('\n');
        }
        return result.ToString();
    }

    private static string EffectWireName(ChangePlanEffectKind value) => value switch
    {
        ChangePlanEffectKind.Create => "create",
        ChangePlanEffectKind.Replace => "replace",
        ChangePlanEffectKind.Delete => "delete",
        ChangePlanEffectKind.Move => "move",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };

    private static string ArtifactWireName(ChangePlanArtifactKind value) => value switch
    {
        ChangePlanArtifactKind.File => "file",
        ChangePlanArtifactKind.ProjectFile => "projectFile",
        ChangePlanArtifactKind.RoslynDocument => "roslynDocument",
        ChangePlanArtifactKind.AdditionalDocument => "additionalDocument",
        ChangePlanArtifactKind.AnalyzerConfigDocument => "analyzerConfigDocument",
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}

internal sealed class ChangePlanDiagnostic
{
    internal ChangePlanDiagnostic(
        string code,
        MutationDiagnosticSeverity severity,
        string message,
        IEnumerable<ChangePlanValue> context)
    {
        Code = ChangePlanValidation.Required(code, nameof(code));
        Severity = severity;
        Message = ChangePlanValidation.Required(message, nameof(message));
        Context = ChangePlanValidation.FreezeValues(context, nameof(context));
    }

    internal string Code { get; }
    internal MutationDiagnosticSeverity Severity { get; }
    internal string Message { get; }
    internal ImmutableArray<ChangePlanValue> Context { get; }
}

internal sealed class ChangePlanDiagnosticComparer : IComparer<ChangePlanDiagnostic>
{
    private ChangePlanDiagnosticComparer()
    {
    }

    internal static ChangePlanDiagnosticComparer Instance { get; } = new();

    int IComparer<ChangePlanDiagnostic>.Compare(ChangePlanDiagnostic? left, ChangePlanDiagnostic? right) =>
        CompareCanonical(left, right);

    internal static int CompareCanonical(ChangePlanDiagnostic? left, ChangePlanDiagnostic? right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left is null)
            return -1;
        if (right is null)
            return 1;

        var result = string.Compare(left.Code, right.Code, StringComparison.Ordinal);
        if (result != 0)
            return result;
        result = left.Severity.CompareTo(right.Severity);
        if (result != 0)
            return result;
        result = string.Compare(left.Message, right.Message, StringComparison.Ordinal);
        if (result != 0)
            return result;

        var sharedCount = Math.Min(left.Context.Length, right.Context.Length);
        for (var index = 0; index < sharedCount; index++)
        {
            result = string.Compare(
                left.Context[index].Name,
                right.Context[index].Name,
                StringComparison.Ordinal);
            if (result != 0)
                return result;
            result = CompareNullable(left.Context[index].Value, right.Context[index].Value);
            if (result != 0)
                return result;
        }
        return left.Context.Length.CompareTo(right.Context.Length);
    }

    private static int CompareNullable(string? left, string? right)
    {
        if (left is null)
            return right is null ? 0 : -1;
        return right is null ? 1 : string.Compare(left, right, StringComparison.Ordinal);
    }
}

internal sealed class ChangePlanConfidence
{
    internal ChangePlanConfidence(string level, string basis, IEnumerable<string> evidence)
    {
        Level = ChangePlanValidation.Required(level, nameof(level));
        Basis = ChangePlanValidation.Required(basis, nameof(basis));
        Evidence = ChangePlanValidation.FreezeStrings(evidence, nameof(evidence));
        if (Evidence.IsEmpty)
            throw new ArgumentException("Plan confidence requires affirmative evidence.", nameof(evidence));
    }

    internal string Level { get; }
    internal string Basis { get; }
    internal ImmutableArray<string> Evidence { get; }
}

internal sealed class ChangePlanWorkspace
{
    internal ChangePlanWorkspace(
        WorkspaceCompletenessState state,
        string solutionPath,
        string configuration,
        IEnumerable<string> targetFrameworks,
        string sdkIdentity,
        string compilerIdentity,
        string evaluatorMode,
        string trustState)
    {
        State = state;
        SolutionPath = ChangePlanValidation.NormalizeAbsolutePath(solutionPath, nameof(solutionPath));
        Configuration = ChangePlanValidation.Required(configuration, nameof(configuration));
        TargetFrameworks = ChangePlanValidation.FreezeStrings(targetFrameworks, nameof(targetFrameworks));
        if (TargetFrameworks.IsEmpty)
            throw new ArgumentException("Workspace provenance requires at least one target framework.", nameof(targetFrameworks));
        SdkIdentity = ChangePlanValidation.Required(sdkIdentity, nameof(sdkIdentity));
        CompilerIdentity = ChangePlanValidation.Required(compilerIdentity, nameof(compilerIdentity));
        EvaluatorMode = ChangePlanValidation.Required(evaluatorMode, nameof(evaluatorMode));
        TrustState = ChangePlanValidation.Required(trustState, nameof(trustState));
    }

    internal WorkspaceCompletenessState State { get; }
    internal string SolutionPath { get; }
    internal string Configuration { get; }
    internal ImmutableArray<string> TargetFrameworks { get; }
    internal string SdkIdentity { get; }
    internal string CompilerIdentity { get; }
    internal string EvaluatorMode { get; }
    internal string TrustState { get; }
}

internal sealed class ChangePlanVerificationAction
{
    internal ChangePlanVerificationAction(
        int sequence,
        string name,
        string capability,
        string expectedOutcome,
        bool required)
    {
        if (sequence <= 0)
            throw new ArgumentOutOfRangeException(nameof(sequence), "Verification sequence must be positive.");

        Sequence = sequence;
        Name = ChangePlanValidation.Required(name, nameof(name));
        Capability = ChangePlanValidation.Required(capability, nameof(capability));
        ExpectedOutcome = ChangePlanValidation.Required(expectedOutcome, nameof(expectedOutcome));
        Required = required;
    }

    internal int Sequence { get; }
    internal string Name { get; }
    internal string Capability { get; }
    internal string ExpectedOutcome { get; }
    internal bool Required { get; }
}

internal sealed class ChangePlanCoordination
{
    internal ChangePlanCoordination(
        ChangePlanCoordinationDomain domain,
        ChangePlanCoordinationMode mode,
        string writerSetProof,
        string controlRoot,
        string controlRootResolvedPath,
        string controlRootPlatformIdentity,
        string filesystemVolumeIdentity,
        string stagingRoot,
        string stagingRootResolvedPath,
        string stagingRootPlatformIdentity,
        string backupRoot,
        string backupRootResolvedPath,
        string backupRootPlatformIdentity)
    {
        Domain = domain;
        Mode = mode;
        WriterSetProof = ChangePlanValidation.Required(writerSetProof, nameof(writerSetProof));
        ControlRoot = ChangePlanValidation.NormalizeAbsolutePath(controlRoot, nameof(controlRoot));
        ControlRootResolvedPath = ChangePlanValidation.NormalizeAbsolutePath(
            controlRootResolvedPath,
            nameof(controlRootResolvedPath));
        ControlRootPlatformIdentity = ChangePlanValidation.Required(
            controlRootPlatformIdentity,
            nameof(controlRootPlatformIdentity));
        FilesystemVolumeIdentity = ChangePlanValidation.Required(
            filesystemVolumeIdentity,
            nameof(filesystemVolumeIdentity));
        StagingRoot = ChangePlanValidation.NormalizeAbsolutePath(stagingRoot, nameof(stagingRoot));
        StagingRootResolvedPath = ChangePlanValidation.NormalizeAbsolutePath(
            stagingRootResolvedPath,
            nameof(stagingRootResolvedPath));
        StagingRootPlatformIdentity = ChangePlanValidation.Required(
            stagingRootPlatformIdentity,
            nameof(stagingRootPlatformIdentity));
        BackupRoot = ChangePlanValidation.NormalizeAbsolutePath(backupRoot, nameof(backupRoot));
        BackupRootResolvedPath = ChangePlanValidation.NormalizeAbsolutePath(
            backupRootResolvedPath,
            nameof(backupRootResolvedPath));
        BackupRootPlatformIdentity = ChangePlanValidation.Required(
            backupRootPlatformIdentity,
            nameof(backupRootPlatformIdentity));
    }

    internal ChangePlanCoordinationDomain Domain { get; }
    internal ChangePlanCoordinationMode Mode { get; }
    internal string WriterSetProof { get; }
    internal string ControlRoot { get; }
    internal string ControlRootResolvedPath { get; }
    internal string ControlRootPlatformIdentity { get; }
    internal string FilesystemVolumeIdentity { get; }
    internal string StagingRoot { get; }
    internal string StagingRootResolvedPath { get; }
    internal string StagingRootPlatformIdentity { get; }
    internal string BackupRoot { get; }
    internal string BackupRootResolvedPath { get; }
    internal string BackupRootPlatformIdentity { get; }
}

internal sealed record ChangePlanPathPolicy(
    ChangePlanCaseSensitivity CaseSensitivity,
    ChangePlanLongPathPolicy LongPath,
    ChangePlanReadOnlyPolicy ReadOnly,
    ChangePlanSymlinkPolicy Symlink,
    ChangePlanHardLinkPolicy HardLink);

internal sealed class ChangePlanPayload
{
    internal ChangePlanPayload(
        Guid planId,
        ChangePlanRepository repository,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        IEnumerable<ChangePlanOperation> operations,
        IEnumerable<ChangePlanDependency> dependencies,
        IEnumerable<ChangePlanEffect> effects,
        IEnumerable<ChangePlanDiagnostic> diagnostics,
        IEnumerable<string> assumptions,
        ChangePlanConfidence confidence,
        IEnumerable<string> limitations,
        ChangePlanWorkspace workspace,
        IEnumerable<ChangePlanVerificationAction> verificationActions,
        ChangePlanCoordination coordination,
        ChangePlanPathPolicy pathPolicy,
        ChangePlanCleanupPolicy cleanupPolicy,
        ChangePlanRecoveryPolicy recoveryPolicy)
    {
        if (planId == Guid.Empty)
            throw new ArgumentException("A plan ID cannot be empty.", nameof(planId));
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(diagnostics);
        ArgumentNullException.ThrowIfNull(confidence);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(verificationActions);
        ArgumentNullException.ThrowIfNull(coordination);
        ArgumentNullException.ThrowIfNull(pathPolicy);

        PlanId = planId;
        SchemaVersion = ChangePlanSchema.Version;
        Repository = repository;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        if (ExpiresAtUtc <= CreatedAtUtc)
            throw new ArgumentException("Plan expiry must be after creation.", nameof(expiresAtUtc));

        Operations = FreezeSequenced(
            operations,
            operation => operation.Sequence,
            "operations");
        Dependencies = FreezeDependencies(dependencies);
        Effects = FreezeSequenced(
            effects,
            effect => effect.Sequence,
            "effects");
        if (Operations.IsEmpty)
            throw new ArgumentException("A change plan requires at least one canonical operation.", nameof(operations));
        if (Dependencies.IsEmpty)
            throw new ArgumentException("A change plan requires at least one guarded read dependency.", nameof(dependencies));
        if (Effects.IsEmpty)
            throw new ArgumentException("A change plan requires at least one effect.", nameof(effects));

        Diff = ChangePlanDiff.Render(Effects);
        var diagnosticArray = diagnostics.ToArray();
        if (diagnosticArray.Any(diagnostic => diagnostic is null))
            throw new ArgumentException("Change-plan diagnostics cannot contain null values.", nameof(diagnostics));
        Diagnostics = diagnosticArray
            .Order(ChangePlanDiagnosticComparer.Instance)
            .ToImmutableArray();
        if (Diagnostics.Zip(Diagnostics.Skip(1), ChangePlanDiagnosticComparer.CompareCanonical)
            .Any(comparison => comparison == 0))
        {
            throw new ArgumentException(
                "Change-plan diagnostics must be unique.",
                nameof(diagnostics));
        }
        Assumptions = ChangePlanValidation.FreezeStrings(assumptions, nameof(assumptions));
        Confidence = confidence;
        Limitations = ChangePlanValidation.FreezeStrings(limitations, nameof(limitations));
        Workspace = workspace;
        VerificationActions = FreezeSequenced(
            verificationActions,
            action => action.Sequence,
            "verification actions");
        if (VerificationActions.IsEmpty || VerificationActions.All(action => !action.Required))
        {
            throw new ArgumentException(
                "A change plan requires at least one required verification action.",
                nameof(verificationActions));
        }
        Coordination = coordination;
        PathPolicy = pathPolicy;
        if (cleanupPolicy != ChangePlanCleanupPolicy.RetainUntilVerifiedTerminal)
            throw new ArgumentOutOfRangeException(nameof(cleanupPolicy));
        if (recoveryPolicy != ChangePlanRecoveryPolicy.RequireDurableJournal)
            throw new ArgumentOutOfRangeException(nameof(recoveryPolicy));
        CleanupPolicy = cleanupPolicy;
        RecoveryPolicy = recoveryPolicy;

        var affectedPaths = Effects.SelectMany(effect => effect.AffectedPaths).ToArray();
        var comparison = pathPolicy.CaseSensitivity == ChangePlanCaseSensitivity.Sensitive
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;
        var duplicatePath = affectedPaths
            .GroupBy(path => path, comparison)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicatePath is not null)
            throw new ArgumentException(
                $"Affected path '{duplicatePath.Key}' participates in more than one effect.",
                nameof(effects));
        AffectedPaths = affectedPaths.Order(comparison).ToImmutableArray();

        ValidateRoots(comparison);
        ValidateResolvedPaths(comparison);
        ValidateDependencies(comparison);
        ValidateGuards();
        ValidatePathConflicts(comparison);
        ValidatePathPolicies();
    }

    internal string SchemaVersion { get; }
    internal Guid PlanId { get; }
    internal ChangePlanRepository Repository { get; }
    internal DateTimeOffset CreatedAtUtc { get; }
    internal DateTimeOffset ExpiresAtUtc { get; }
    internal ImmutableArray<ChangePlanOperation> Operations { get; }
    internal ImmutableArray<ChangePlanDependency> Dependencies { get; }
    internal ImmutableArray<ChangePlanEffect> Effects { get; }
    internal ImmutableArray<string> AffectedPaths { get; }
    internal string Diff { get; }
    internal ImmutableArray<ChangePlanDiagnostic> Diagnostics { get; }
    internal ImmutableArray<string> Assumptions { get; }
    internal ChangePlanConfidence Confidence { get; }
    internal ImmutableArray<string> Limitations { get; }
    internal ChangePlanWorkspace Workspace { get; }
    internal ImmutableArray<ChangePlanVerificationAction> VerificationActions { get; }
    internal ChangePlanCoordination Coordination { get; }
    internal ChangePlanPathPolicy PathPolicy { get; }
    internal ChangePlanCleanupPolicy CleanupPolicy { get; }
    internal ChangePlanRecoveryPolicy RecoveryPolicy { get; }

    internal bool IsExpired(DateTimeOffset nowUtc) => nowUtc.ToUniversalTime() >= ExpiresAtUtc;

    private void ValidateRoots(StringComparer comparer)
    {
        var comparison = ToComparison(comparer);
        RequireUnsymlinkedRoot(
            Repository.RepositoryRoot,
            Repository.ResolvedRepositoryRoot,
            "repository",
            comparison);
        RequireUnsymlinkedRoot(
            Coordination.ControlRoot,
            Coordination.ControlRootResolvedPath,
            "control",
            comparison);
        RequireUnsymlinkedRoot(
            Coordination.StagingRoot,
            Coordination.StagingRootResolvedPath,
            "staging",
            comparison);
        RequireUnsymlinkedRoot(
            Coordination.BackupRoot,
            Coordination.BackupRootResolvedPath,
            "backup",
            comparison);

        if (!ChangePlanValidation.IsContained(Coordination.StagingRoot, Coordination.ControlRoot, comparison)
            || !ChangePlanValidation.IsContained(Coordination.BackupRoot, Coordination.ControlRoot, comparison)
            || string.Equals(Coordination.ControlRoot, Coordination.StagingRoot, comparison)
            || string.Equals(Coordination.ControlRoot, Coordination.BackupRoot, comparison)
            || string.Equals(Coordination.StagingRoot, Coordination.BackupRoot, comparison))
        {
            throw new ArgumentException(
                "Staging and backup roots must be distinct children of the control root under the plan case policy.");
        }

        var protectedRoots = new[]
        {
            (Coordination.ControlRootResolvedPath, Coordination.ControlRootPlatformIdentity),
            (Coordination.StagingRootResolvedPath, Coordination.StagingRootPlatformIdentity),
            (Coordination.BackupRootResolvedPath, Coordination.BackupRootPlatformIdentity)
        };
        if (protectedRoots.Any(root =>
                ChangePlanValidation.IsContained(root.Item1, Repository.ResolvedRepositoryRoot, comparison)
                || ChangePlanValidation.IsContained(Repository.ResolvedRepositoryRoot, root.Item1, comparison))
            || protectedRoots.Any(root => string.Equals(
                Repository.RootPlatformIdentity,
                root.Item2,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "Protected roots and the repository root must not overlap or share one platform identity.");
        }
        if (protectedRoots.Select(root => root.Item2).Distinct(StringComparer.Ordinal).Count()
            != protectedRoots.Length)
        {
            throw new ArgumentException("Control, staging, and backup roots require distinct platform identities.");
        }
    }

    private static void RequireUnsymlinkedRoot(
        string path,
        string resolvedPath,
        string description,
        StringComparison comparison)
    {
        if (!string.Equals(path, resolvedPath, comparison))
        {
            throw new ArgumentException(
                $"The {description} root resolved path must equal its lexical path under the plan case policy.");
        }
    }

    private void ValidateResolvedPaths(StringComparer comparer)
    {
        var comparison = ToComparison(comparer);
        foreach (var dependency in Dependencies)
            ValidateResolvedPath(dependency.Path, dependency.ResolvedPath, comparison);
        foreach (var effect in Effects)
        {
            ValidateResolvedPath(effect.SourcePath, effect.SourceResolvedPath, comparison);
            if (effect.DestinationPath is { } destinationPath)
                ValidateResolvedPath(destinationPath, effect.DestinationResolvedPath!, comparison);
        }
    }

    private void ValidateResolvedPath(
        string repositoryPath,
        string resolvedPath,
        StringComparison comparison)
    {
        var lexicalPath = ChangePlanValidation.NormalizeAbsolutePath(
            Path.Combine(
                Repository.RepositoryRoot,
                repositoryPath.Replace('/', Path.DirectorySeparatorChar)),
            nameof(repositoryPath));
        if (!ChangePlanValidation.IsContained(resolvedPath, Repository.RepositoryRoot, comparison)
            || !string.Equals(lexicalPath, resolvedPath, comparison))
        {
            throw new ArgumentException(
                $"Resolved path '{resolvedPath}' does not prove an unsymlinked repository path for '{repositoryPath}'.");
        }
    }

    private void ValidateDependencies(StringComparer comparer)
    {
        var duplicate = Dependencies
            .GroupBy(dependency => dependency.Path, comparer)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Read dependency '{duplicate.Key}' is declared more than once.");
        if (Dependencies.Any(dependency => AffectedPaths.Contains(dependency.Path, comparer)))
            throw new ArgumentException("Read dependencies must be distinct from affected paths.");

        foreach (var dependency in Dependencies)
            ValidateExistingPrecondition(dependency.ExpectedState);
    }

    private void ValidateGuards()
    {
        var requiredKind = Coordination.Mode switch
        {
            ChangePlanCoordinationMode.ExternalWriteGuards => ChangePlanGuardKind.ExternalWriteGuard,
            ChangePlanCoordinationMode.ConditionalEffects => ChangePlanGuardKind.ConditionalEffect,
            _ => throw new ArgumentOutOfRangeException(nameof(Coordination.Mode))
        };
        var guards = Dependencies.Select(dependency => dependency.Guard)
            .Concat(Effects.SelectMany(effect => effect.Guards));
        if (guards.Any(guard => guard.Kind != requiredKind))
            throw new ArgumentException("Every path guard must match the coordination mode.");
    }

    private void ValidatePathConflicts(StringComparer comparer)
    {
        var comparison = ToComparison(comparer);
        for (var leftIndex = 0; leftIndex < AffectedPaths.Length; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < AffectedPaths.Length; rightIndex++)
            {
                if (IsAncestor(AffectedPaths[leftIndex], AffectedPaths[rightIndex], comparison)
                    || IsAncestor(AffectedPaths[rightIndex], AffectedPaths[leftIndex], comparison))
                {
                    throw new ArgumentException("Affected paths cannot have an ancestor/descendant conflict.");
                }
            }
        }

        var identities = Effects
            .Where(effect => effect.SourceBefore.Exists)
            .Select(effect => (effect.SourcePath, effect.SourceBefore.PlatformFileIdentity))
            .Concat(Dependencies.Select(dependency =>
                (dependency.Path, dependency.ExpectedState.PlatformFileIdentity)))
            .Where(item => item.PlatformFileIdentity is not null)
            .ToArray();
        if (identities.Any(item => string.Equals(
                item.PlatformFileIdentity,
                Repository.RootPlatformIdentity,
                StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                "A repository-relative plan path cannot share the repository root platform identity.");
        }
        var sharedIdentity = identities
            .GroupBy(item => item.PlatformFileIdentity!, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Select(item => item.Item1).Distinct(comparer).Count() > 1);
        if (sharedIdentity is not null)
            throw new ArgumentException("Distinct plan paths cannot share one platform file identity.");
    }

    private void ValidatePathPolicies()
    {
        if (PathPolicy.LongPath != ChangePlanLongPathPolicy.Reject
            || PathPolicy.ReadOnly != ChangePlanReadOnlyPolicy.Reject
            || PathPolicy.Symlink != ChangePlanSymlinkPolicy.Reject
            || PathPolicy.HardLink != ChangePlanHardLinkPolicy.Reject)
        {
            throw new ArgumentException(
                "rt.change-plan.v1 currently requires reject policies for long, read-only, symbolic-link, and hard-link paths.");
        }

        foreach (var dependency in Dependencies)
        {
            ValidateSafeLinkState(dependency.ExpectedState);
            ValidateMetadata(dependency.ExpectedState, exactContent: null);
        }

        foreach (var effect in Effects)
        {
            ValidateExistingPrecondition(effect.SourceBefore);
            if (effect.SourceBefore.Exists)
                ValidateMetadata(effect.SourceBefore, effect.OriginalContent.ToArray());
            if (effect.SourceAfter.Exists)
                ValidateMetadata(effect.SourceAfter, effect.ReplacementContent.ToArray());
            if (effect.DestinationAfter is { Exists: true } destinationAfter)
            {
                var afterContent = effect.HasReplacementContent
                    ? effect.ReplacementContent.ToArray()
                    : effect.OriginalContent.ToArray();
                ValidateMetadata(destinationAfter, afterContent);
            }

            foreach (var state in new[]
                     {
                         effect.SourceBefore,
                         effect.SourceAfter,
                         effect.DestinationBefore,
                         effect.DestinationAfter
                     }.OfType<ChangePlanPathState>().Where(state => state.Exists))
            {
                ValidateSafeLinkState(state);
            }

            if (effect.Kind == ChangePlanEffectKind.Replace)
                ValidateMetadataPreserved(effect.SourceBefore, effect.SourceAfter);
            if (effect.Kind == ChangePlanEffectKind.Move)
                ValidateMetadataPreserved(effect.SourceBefore, effect.DestinationAfter!);
        }

        foreach (var path in AffectedPaths.Concat(Dependencies.Select(dependency => dependency.Path)))
        {
            var fullPath = Path.Combine(Repository.RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));
            if (fullPath.Length > 259)
                throw new ArgumentException($"Path '{path}' exceeds the conservative v1 path limit.");
        }
    }

    private static void ValidateSafeLinkState(ChangePlanPathState state)
    {
        if (!state.KnownHardLinkAliases.IsEmpty)
            throw new ArgumentException("rt.change-plan.v1 rejects known hard-link aliases.");
        if (state.Kind == ChangePlanPathKind.RegularFile && state.LinkCount != 1)
            throw new ArgumentException("rt.change-plan.v1 requires one proven link and no hard-link aliases.");
        if (state.ReadOnly != false)
            throw new ArgumentException("rt.change-plan.v1 requires explicit writable path metadata.");
    }

    private static void ValidateMetadata(ChangePlanPathState state, byte[]? exactContent)
    {
        if (state.FileMode is null
            || state.Executable is null
            || state.ReadOnly is null
            || state.FileMode.Length != 4
            || state.FileMode.Any(character => character is < '0' or > '7'))
            throw new ArgumentException("Existing and resulting paths require canonical mode, executable, and read-only metadata.");

        var mode = Convert.ToInt32(state.FileMode, 8);
        var modeIsExecutable = (mode & 0b001_001_001) != 0;
        if (modeIsExecutable != state.Executable)
            throw new ArgumentException("Executable metadata must agree with the canonical file mode.");

        if (state.Kind == ChangePlanPathKind.Directory)
        {
            if (state.EncodingName is not null
                || state.HasByteOrderMark is not null
                || state.Newline is not null
                || exactContent is not null)
                throw new ArgumentException("Directory metadata cannot contain text or content fields.");
            return;
        }

        if (state.Kind != ChangePlanPathKind.RegularFile
            || !string.Equals(state.EncodingName, "utf-8", StringComparison.Ordinal)
            || state.HasByteOrderMark is null
            || state.Newline is not ("none" or "lf" or "crlf" or "cr" or "mixed"))
            throw new ArgumentException("Regular files require canonical UTF-8, BOM, and newline metadata.");

        if (exactContent is not null)
            ValidateUtf8ContentMetadata(state, exactContent);
    }

    private static void ValidateUtf8ContentMetadata(ChangePlanPathState state, byte[] exactContent)
    {
        ReadOnlySpan<byte> utf8Bom = [0xEF, 0xBB, 0xBF];
        var hasBom = exactContent.AsSpan().StartsWith(utf8Bom);
        if (hasBom != state.HasByteOrderMark)
            throw new ArgumentException("UTF-8 BOM metadata does not match the exact content bytes.");

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(
                exactContent.AsSpan(hasBom ? 3 : 0));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("Exact content is not valid UTF-8.", exception);
        }

        if (!string.Equals(DetectNewline(text), state.Newline, StringComparison.Ordinal))
            throw new ArgumentException("Newline metadata does not match the exact content bytes.");
    }

    private static string DetectNewline(string text)
    {
        var crlf = text.Contains("\r\n", StringComparison.Ordinal);
        var textWithoutCrlf = text.Replace("\r\n", string.Empty, StringComparison.Ordinal);
        var lf = textWithoutCrlf.Contains('\n');
        var cr = textWithoutCrlf.Contains('\r');

        var kinds = (crlf ? 1 : 0) + (lf ? 1 : 0) + (cr ? 1 : 0);
        if (kinds == 0)
            return "none";
        if (kinds > 1)
            return "mixed";
        return crlf ? "crlf" : lf ? "lf" : "cr";
    }

    private static void ValidateMetadataPreserved(
        ChangePlanPathState before,
        ChangePlanPathState after)
    {
        if (before.HasByteOrderMark != after.HasByteOrderMark
            || !string.Equals(before.Newline, after.Newline, StringComparison.Ordinal)
            || !string.Equals(before.FileMode, after.FileMode, StringComparison.Ordinal))
        {
            throw new ArgumentException("Replace and move effects must preserve exact file metadata in v1.");
        }
    }

    private static void ValidateExistingPrecondition(ChangePlanPathState state)
    {
        if (!state.Exists)
            return;
        if (string.IsNullOrWhiteSpace(state.PlatformFileIdentity) || state.LinkCount is null)
        {
            throw new ArgumentException(
                "Every existing affected path requires platform identity and link-count preconditions.");
        }
    }

    private static ImmutableArray<T> FreezeSequenced<T>(
        IEnumerable<T> values,
        Func<T, int> getSequence,
        string description)
    {
        var valueArray = values.ToArray();
        if (valueArray.Any(value => value is null))
            throw new ArgumentException($"Change-plan {description} cannot contain null values.", description);
        var frozen = valueArray.OrderBy(getSequence).ToImmutableArray();
        if (frozen.GroupBy(getSequence).Any(group => group.Count() > 1))
            throw new ArgumentException($"Change-plan {description} must have unique sequence values.", description);
        return frozen;
    }

    private static ImmutableArray<ChangePlanDependency> FreezeDependencies(
        IEnumerable<ChangePlanDependency> dependencies)
    {
        var dependencyArray = dependencies.ToArray();
        if (dependencyArray.Any(dependency => dependency is null))
            throw new ArgumentException("Change-plan dependencies cannot contain null values.", nameof(dependencies));
        return dependencyArray
            .OrderBy(dependency => dependency.Path, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static bool IsAncestor(string candidate, string descendant, StringComparison comparison) =>
        descendant.StartsWith(candidate.TrimEnd('/') + '/', comparison);

    private static StringComparison ToComparison(StringComparer comparer) =>
        ReferenceEquals(comparer, StringComparer.Ordinal)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;
}

internal static class ChangePlanValidation
{
    internal static string Required(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
        return value;
    }

    internal static string NormalizeSha256(string value, string parameterName)
    {
        Required(value, parameterName);
        if (value.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException("Value must be a 64-character SHA-256 hexadecimal digest.", parameterName);
        return value.ToLowerInvariant();
    }

    internal static string NormalizeRepositoryPath(string value, string parameterName)
    {
        Required(value, parameterName);
        var candidate = value.Replace('\\', '/');
        if (candidate[0] == '/'
            || (candidate.Length >= 2 && char.IsLetter(candidate[0]) && candidate[1] == ':'))
        {
            throw new ArgumentException("Path must be repository-relative.", parameterName);
        }

        var segments = new List<string>();
        foreach (var segment in candidate.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
                continue;
            if (segment == "..")
                throw new ArgumentException("Path must be repository-relative and cannot escape its root.", parameterName);
            if (segment.Length > 255
                || segment.EndsWith('.')
                || segment.EndsWith(' ')
                || segment.Any(character => char.IsControl(character) || character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
                || IsReservedWindowsName(segment))
            {
                throw new ArgumentException(
                    "Path contains a special, alternate-stream, reserved, or non-portable segment.",
                    parameterName);
            }
            segments.Add(segment);
        }

        if (segments.Count == 0)
            throw new ArgumentException("Path must identify a repository-relative entry.", parameterName);
        return string.Join('/', segments);
    }

    private static bool IsReservedWindowsName(string segment)
    {
        var stem = segment.Split('.', 2)[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL"
            or "COM1" or "COM2" or "COM3" or "COM4" or "COM5" or "COM6" or "COM7" or "COM8" or "COM9"
            or "LPT1" or "LPT2" or "LPT3" or "LPT4" or "LPT5" or "LPT6" or "LPT7" or "LPT8" or "LPT9"
            or "COM¹" or "COM²" or "COM³" or "LPT¹" or "LPT²" or "LPT³";
    }

    internal static string NormalizeAbsolutePath(string value, string parameterName)
    {
        Required(value, parameterName);
        if (!Path.IsPathFullyQualified(value))
            throw new ArgumentException("Path must be absolute.", parameterName);
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value)).Replace('\\', '/');
    }

    internal static ImmutableArray<ChangePlanValue> FreezeValues(
        IEnumerable<ChangePlanValue> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var valueArray = values.ToArray();
        if (valueArray.Any(value => value is null))
            throw new ArgumentException("Change-plan values cannot contain null entries.", parameterName);
        var frozen = valueArray.OrderBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();
        if (frozen.Select(value => value.Name).Distinct(StringComparer.Ordinal).Count() != frozen.Length)
        {
            throw new ArgumentException("Change-plan value names must be nonempty and unique.", parameterName);
        }
        return frozen;
    }

    internal static ImmutableArray<string> FreezeStrings(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var frozen = values
            .Select(value => Required(value, parameterName))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (frozen.Distinct(StringComparer.Ordinal).Count() != frozen.Length)
            throw new ArgumentException("Change-plan values must be unique.", parameterName);
        return frozen;
    }

    internal static ImmutableArray<string> FreezeKnownPaths(
        IEnumerable<string> values,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var frozen = values
            .Select(value => Required(value, parameterName))
            .Select(value => Path.IsPathFullyQualified(value)
                ? NormalizeAbsolutePath(value, parameterName)
                : NormalizeRepositoryPath(value, parameterName))
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (frozen.Distinct(StringComparer.Ordinal).Count() != frozen.Length)
            throw new ArgumentException("Change-plan paths must be unique.", parameterName);
        return frozen;
    }

    internal static bool IsContained(string candidate, string root, StringComparison comparison)
    {
        if (string.Equals(candidate, root, comparison))
            return true;
        return candidate.StartsWith(root.TrimEnd('/') + '/', comparison);
    }
}
