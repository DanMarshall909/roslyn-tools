using System.Collections.Immutable;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RoslynTools.Core;

internal enum OperationLifecycleState
{
    Received,
    Prepared,
    Applying,
    OwnerQuiescencePending,
    Reconciling,
    Applied,
    RolledBack,
    RecoveryRequired,
    Recovering,
    RecoveredApplied,
    RecoveredRolledBack,
    RecoveryAcknowledged,
    CancelledNoEffect,
    RejectedStale,
    RejectedPlanUnavailable,
    RejectedUnsafeCoordination,
    Expired
}

internal enum OperationIntakeDisposition
{
    Accepted,
    Replay,
    Conflict,
    AccessDenied
}

internal enum OperationLedgerErrorKind
{
    Missing,
    Corrupt,
    Unreadable,
    UnsafeProtection,
    InvalidTransition,
    Conflict
}

internal sealed record OperationLedgerError(
    OperationLedgerErrorKind Kind,
    string Message,
    IReadOnlyDictionary<string, string>? Evidence = null);

internal sealed record OperationTransition(
    long Sequence,
    OperationLifecycleState State,
    DateTimeOffset CommittedAtUtc,
    string? Evidence);

internal enum OperationEffectJournalKind
{
    Intent,
    Admitted,
    Observed
}

internal sealed record OperationGuardToken(
    string Path,
    ChangePlanGuardKind Kind,
    string VersionToken);

internal sealed record OperationOwnerLease(
    Guid OwnerInstanceId,
    long FenceEpoch,
    Guid ReservationToken,
    DateTimeOffset HeartbeatAtUtc,
    DateTimeOffset ExpiresAtUtc);

internal sealed record PreEffectActivityAdmission(
    Guid Token,
    long ControlGeneration,
    long FenceEpoch,
    Guid ReservationToken);

internal sealed record PreEffectActivityState(
    long ControlGeneration,
    bool IsClosed,
    ImmutableArray<PreEffectActivityAdmission> ActiveAdmissions,
    bool HasAdmissionHistory = false)
{
    internal static PreEffectActivityState Open { get; } = new(1, false, []);
}

internal sealed record OperationEffectJournalEvent(
    long Sequence,
    int EffectSequence,
    OperationEffectJournalKind Kind,
    string EvidenceHash,
    DateTimeOffset CommittedAtUtc)
{
    internal long? FenceEpoch { get; init; }
    internal Guid? FenceToken { get; init; }
    internal string? EffectHash { get; init; }
    internal ImmutableArray<OperationGuardToken> GuardTokens { get; init; } = [];
}

internal sealed record DurableOperationRecord(
    string RepositoryId,
    OperationId OperationId,
    OperationRequestIdentity RequestIdentity,
    ImmutableArray<byte> CanonicalRequestBytes,
    string SubmittingPrincipalId,
    OperationLifecycleState CurrentState,
    ImmutableArray<OperationTransition> Transitions)
{
    internal ImmutableArray<OperationEffectJournalEvent> EffectJournal { get; init; } = [];
    internal OperationOwnerLease? OwnerLease { get; init; }
    internal PreEffectActivityState PreEffectActivity { get; init; } = PreEffectActivityState.Open;
}

internal sealed record OperationIntakeResult(
    OperationIntakeDisposition Disposition,
    DurableOperationRecord? Operation);

internal enum DispatchIntakePhase
{
    Registered,
    DispatchPinned,
    IntakePinned,
    Received,
    OperationPinned,
    Prepared,
    RejectedPlanUnavailable
}

internal enum DispatchIntakeRegistrationDisposition
{
    Registered,
    Replay,
    Conflict,
    AccessDenied
}

internal sealed record DispatchIntakeTransition(
    long Sequence,
    DispatchIntakePhase Phase,
    DateTimeOffset CommittedAtUtc,
    string? Evidence);

internal sealed record DurableDispatchIntakeRecord(
    string RepositoryId,
    OperationId OperationId,
    OperationRequestIdentity RequestIdentity,
    ImmutableArray<byte> CanonicalRequestBytes,
    string SubmittingPrincipalId,
    string DispatchLeaseId,
    DateTimeOffset DispatchLeaseExpiresAtUtc,
    string IntakeAttemptId,
    DateTimeOffset IntakeAttemptExpiresAtUtc,
    DispatchIntakePhase CurrentPhase,
    ImmutableArray<DispatchIntakeTransition> Transitions);

internal sealed record DispatchIntakeRegistrationResult(
    DispatchIntakeRegistrationDisposition Disposition,
    DurableDispatchIntakeRecord? Dispatch);

/// <summary>
///     Principal identity established by a protected host or transport boundary. Request
///     payloads cannot be converted implicitly into this authority-bearing value.
/// </summary>
internal sealed record AuthenticatedPrincipalId
{
    internal AuthenticatedPrincipalId(string value)
    {
        Value = OperationRequestText.Required(value, nameof(value));
    }

    internal string Value { get; }
}

/// <summary>
///     Resolves the operation principal from protected host or transport context. The
///     application request deliberately carries no principal field.
/// </summary>
internal interface IAuthenticatedPrincipalProvider
{
    AuthenticatedPrincipalId GetPrincipal();
}

internal sealed record AuthenticatedOperationContext(
    AuthenticatedPrincipalId Principal,
    OperationEpochClaims EpochClaims);

/// <summary>
///     Authenticates public operation-epoch claims against protected host policy before
///     they contribute to canonical request identity. Request fields cannot self-assert
///     either the principal or the claims authority.
/// </summary>
internal interface IAuthenticatedOperationContextProvider : IAuthenticatedPrincipalProvider
{
    Result<AuthenticatedOperationContext, RtError> Authenticate(
        string repositoryId,
        OperationId operationId,
        int maximumOperationCount,
        long maximumRequestBytes,
        DateTimeOffset notAfterUtc,
        long actualRequestBytes);
}

/// <summary>
///     Trusted host boundary that holds the operation ledger's physical location and
///     access policy stable for the duration of a read or durable transition.
/// </summary>
internal interface IOperationLedgerProtectionAuthority
{
    /// <summary>
    ///     Acquires an already-provisioned ledger without creating its root or protection
    ///     lease. A missing root or lease is reported as <see cref="OperationLedgerErrorKind.Missing" />.
    /// </summary>
    OperationLedgerError? AcquireExisting(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease);

    /// <summary>Acquires the ledger for a durable mutation, provisioning it when necessary.</summary>
    OperationLedgerError? Acquire(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease);
}

internal interface IOperationLedgerProtectionLease : IDisposable
{
}

internal interface IOperationLedgerDurability
{
    void FlushFileMetadata(string path);

    void ReplaceFile(string temporaryPath, string recordPath);

    void FlushDirectoryMetadata(string directory);
}

internal sealed class UnavailableOperationLedgerProtectionAuthority
    : IOperationLedgerProtectionAuthority
{
    internal static UnavailableOperationLedgerProtectionAuthority Instance { get; } = new();

    private UnavailableOperationLedgerProtectionAuthority()
    {
    }

    // Stryker disable all : Removing this unavailable provider still reaches the same fail-closed missing-lease result.
    OperationLedgerError? IOperationLedgerProtectionAuthority.AcquireExisting(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease) =>
        Reject(out lease);

    OperationLedgerError? IOperationLedgerProtectionAuthority.Acquire(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease) =>
        Reject(out lease);

    private static OperationLedgerError Reject(out IOperationLedgerProtectionLease? lease)
    {
        lease = null;
        return new OperationLedgerError(
            OperationLedgerErrorKind.UnsafeProtection,
            // Stryker disable once String : Failure wording is diagnostic; callers branch on the typed error kind.
            "No trusted authority proved the protected operation ledger's physical identity and ownership.");
    }
    // Stryker restore all
}

/// <summary>
///     Process-independent intake receipt and logically append-only lifecycle ledger.
///     Each operation is one atomic snapshot whose transition sequence is never rewritten.
/// </summary>
internal sealed class DurableOperationLedger
{
    private const int StoreSchemaVersion = 1;
    private const int MaximumActivePreEffectActivities = 32;
    private static readonly TimeSpan ApplyOwnerLeaseLifetime = TimeSpan.FromMinutes(1);
    private const int MaximumCoordinationReferenceBytes = 256;
    private const uint MoveFileReplaceExisting = 0x1;
    private const uint MoveFileWriteThrough = 0x8;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint FileShareDelete = 0x4;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _rootPath;
    private readonly string _operationsPath;
    private readonly string _dispatchPath;
    private readonly string _lockPath;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IOperationLedgerProtectionAuthority _protectionAuthority;
    private readonly IOperationLedgerDurability _durability;

    internal DurableOperationLedger(
        string rootPath,
        Func<DateTimeOffset>? utcNow = null,
        IOperationLedgerProtectionAuthority? protectionAuthority = null,
        IOperationLedgerDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _operationsPath = Path.Combine(_rootPath, "operations");
        _dispatchPath = Path.Combine(_rootPath, "dispatch");
        _lockPath = Path.Combine(_rootPath, ".ledger.lock");
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _protectionAuthority = protectionAuthority
                               ?? UnavailableOperationLedgerProtectionAuthority.Instance;
        _durability = durability ?? PlatformOperationLedgerDurability.Instance;
    }

    internal Result<OperationIntakeResult, OperationLedgerError> Receive(
        CanonicalApplyRequest request,
        AuthenticatedPrincipalId submittingPrincipal)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(submittingPrincipal);

        try
        {
            var protectionError = AcquireProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<OperationIntakeResult, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            EnsureProtectedRoot();
            using var ledgerLock = AcquireMutationLock();

            var recordPath = GetRecordPath(_rootPath, request.RepositoryId, request.OperationId);
            if (File.Exists(recordPath))
            {
                var existing = ReadRecord(recordPath, request.RepositoryId, request.OperationId);
                if (existing.IsErr)
                    return existing.Map(record => new OperationIntakeResult(
                        OperationIntakeDisposition.Replay,
                        record));

                var operation = existing.Match(record => record, _ => null!);
                if (!string.Equals(
                        operation.SubmittingPrincipalId,
                        submittingPrincipal.Value,
                        StringComparison.Ordinal))
                {
                    return Result<OperationIntakeResult, OperationLedgerError>.Ok(
                        new OperationIntakeResult(OperationIntakeDisposition.AccessDenied, null));
                }

                if (!SameRequest(operation, request))
                {
                    return Result<OperationIntakeResult, OperationLedgerError>.Ok(
                        new OperationIntakeResult(OperationIntakeDisposition.Conflict, null));
                }

                return Result<OperationIntakeResult, OperationLedgerError>.Ok(
                    new OperationIntakeResult(OperationIntakeDisposition.Replay, operation));
            }

            var now = _utcNow().ToUniversalTime();
            var receipt = new DurableOperationRecord(
                request.RepositoryId,
                request.OperationId,
                request.Identity,
                request.CanonicalBytes,
                submittingPrincipal.Value,
                OperationLifecycleState.Received,
                [new OperationTransition(1, OperationLifecycleState.Received, now, null)]);
            WriteRecord(recordPath, receipt);
            return Result<OperationIntakeResult, OperationLedgerError>.Ok(
                new OperationIntakeResult(OperationIntakeDisposition.Accepted, receipt));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<OperationIntakeResult, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<DispatchIntakeRegistrationResult, OperationLedgerError> RegisterDispatch(
        CanonicalApplyRequest request,
        AuthenticatedPrincipalId submittingPrincipal,
        string dispatchLeaseId,
        DateTimeOffset dispatchLeaseExpiresAtUtc,
        string intakeAttemptId,
        DateTimeOffset intakeAttemptExpiresAtUtc)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(submittingPrincipal);

        try
        {
            var protectionError = AcquireProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DispatchIntakeRegistrationResult, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            EnsureProtectedDispatchRoot();
            using var ledgerLock = AcquireMutationLock();

            var recordPath = GetDispatchRecordPath(_rootPath, request.RepositoryId, request.OperationId);
            if (File.Exists(recordPath))
            {
                var existing = ReadDispatchRecord(recordPath, request.RepositoryId, request.OperationId);
                if (existing.IsErr)
                {
                    return existing.Map(record => new DispatchIntakeRegistrationResult(
                        DispatchIntakeRegistrationDisposition.Replay,
                        record));
                }

                var dispatch = existing.Match(record => record, _ => null!);
                if (!string.Equals(
                        dispatch.SubmittingPrincipalId,
                        submittingPrincipal.Value,
                        StringComparison.Ordinal))
                {
                    return Result<DispatchIntakeRegistrationResult, OperationLedgerError>.Ok(
                        new DispatchIntakeRegistrationResult(
                            DispatchIntakeRegistrationDisposition.AccessDenied,
                            null));
                }
                if (!SameRequest(dispatch, request))
                {
                    return Result<DispatchIntakeRegistrationResult, OperationLedgerError>.Ok(
                        new DispatchIntakeRegistrationResult(
                            DispatchIntakeRegistrationDisposition.Conflict,
                            null));
                }
                return Result<DispatchIntakeRegistrationResult, OperationLedgerError>.Ok(
                    new DispatchIntakeRegistrationResult(
                        DispatchIntakeRegistrationDisposition.Replay,
                        dispatch));
            }

            dispatchLeaseId = OperationRequestText.Required(dispatchLeaseId, nameof(dispatchLeaseId));
            intakeAttemptId = OperationRequestText.Required(intakeAttemptId, nameof(intakeAttemptId));
            ValidateCoordinationReference(dispatchLeaseId, nameof(dispatchLeaseId));
            ValidateCoordinationReference(intakeAttemptId, nameof(intakeAttemptId));
            var dispatchExpiry = dispatchLeaseExpiresAtUtc.ToUniversalTime();
            var attemptExpiry = intakeAttemptExpiresAtUtc.ToUniversalTime();
            var now = _utcNow().ToUniversalTime();
            if (dispatchExpiry <= now)
                throw new ArgumentOutOfRangeException(nameof(dispatchLeaseExpiresAtUtc));
            if (attemptExpiry <= now || attemptExpiry > dispatchExpiry)
                throw new ArgumentOutOfRangeException(nameof(intakeAttemptExpiresAtUtc));
            var registered = new DurableDispatchIntakeRecord(
                request.RepositoryId,
                request.OperationId,
                request.Identity,
                request.CanonicalBytes,
                submittingPrincipal.Value,
                dispatchLeaseId,
                dispatchExpiry,
                intakeAttemptId,
                attemptExpiry,
                DispatchIntakePhase.Registered,
                [new DispatchIntakeTransition(1, DispatchIntakePhase.Registered, now, null)]);
            WriteDispatchRecord(recordPath, registered);
            return Result<DispatchIntakeRegistrationResult, OperationLedgerError>.Ok(
                new DispatchIntakeRegistrationResult(
                    DispatchIntakeRegistrationDisposition.Registered,
                    registered));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DispatchIntakeRegistrationResult, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<DurableDispatchIntakeRecord, OperationLedgerError> LoadDispatch(
        string repositoryId,
        OperationId operationId)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);

        try
        {
            var protectionError = AcquireExistingProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            if (!Directory.Exists(_rootPath))
                return Missing<DurableDispatchIntakeRecord>();

            var recordPath = GetDispatchRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableDispatchIntakeRecord>();
            return ReadDispatchRecord(recordPath, repositoryId, operationId);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<DurableDispatchIntakeRecord, OperationLedgerError> AdvanceDispatch(
        string repositoryId,
        OperationId operationId,
        DispatchIntakePhase expectedCurrentPhase,
        DispatchIntakePhase nextPhase,
        string? evidence)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        if (evidence is not null)
            evidence = OperationRequestText.Required(evidence, nameof(evidence));

        try
        {
            var protectionError = AcquireProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            if (!Directory.Exists(_rootPath))
                return Missing<DurableDispatchIntakeRecord>();
            using var ledgerLock = AcquireMutationLock();

            var recordPath = GetDispatchRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableDispatchIntakeRecord>();
            var loaded = ReadDispatchRecord(recordPath, repositoryId, operationId);
            if (loaded.IsErr)
                return loaded;
            var dispatch = loaded.Match(record => record, _ => null!);
            if (dispatch.CurrentPhase != expectedCurrentPhase)
            {
                return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.Conflict,
                        // Stryker disable once String : The typed conflict and durable phase are the contract; wording is diagnostic.
                        $"The dispatch intake is '{dispatch.CurrentPhase}', not the expected '{expectedCurrentPhase}'."));
            }
            if (!CanAdvanceDispatch(expectedCurrentPhase, nextPhase))
            {
                return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        // Stryker disable once String : The typed invalid transition is the contract; wording is diagnostic.
                        $"Dispatch intake cannot advance from '{expectedCurrentPhase}' to '{nextPhase}'."));
            }

            var committedAtUtc = _utcNow().ToUniversalTime();
            var previousCommittedAtUtc = dispatch.Transitions[^1].CommittedAtUtc;
            // Stryker disable once Equality : Equal operands produce the same timestamp through either branch.
            committedAtUtc = committedAtUtc < previousCommittedAtUtc
                ? previousCommittedAtUtc
                : committedAtUtc;
            var updated = dispatch with
            {
                CurrentPhase = nextPhase,
                Transitions = dispatch.Transitions.Add(new DispatchIntakeTransition(
                    dispatch.Transitions[^1].Sequence + 1,
                    nextPhase,
                    committedAtUtc,
                    evidence))
            };
            WriteDispatchRecord(recordPath, updated);
            return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Ok(updated);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<DurableOperationRecord, OperationLedgerError> Load(
        string repositoryId,
        OperationId operationId)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);

        try
        {
            var protectionError = AcquireExistingProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableOperationRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            if (!Directory.Exists(_rootPath))
                return Missing<DurableOperationRecord>();

            var recordPath = GetRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableOperationRecord>();
            return ReadRecord(recordPath, repositoryId, operationId);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableOperationRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<bool, OperationLedgerError> HasUnresolvedRepositoryMutation(
        string repositoryId,
        OperationId excludedOperationId)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(excludedOperationId);

        try
        {
            var protectionError = AcquireExistingProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<bool, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            if (!Directory.Exists(_rootPath))
                return Missing<bool>();
            using var ledgerLock = AcquireMutationLock();

            var repositoryPath = Path.Combine(_operationsPath, Digest(repositoryId));
            if (!Directory.Exists(repositoryPath))
                return Result<bool, OperationLedgerError>.Ok(false);
            if ((File.GetAttributes(repositoryPath) & FileAttributes.ReparsePoint) != 0)
                return Corrupt<bool>("The repository operation directory is a link.");
            foreach (var epochPath in Directory.EnumerateDirectories(repositoryPath))
            {
                if ((File.GetAttributes(epochPath) & FileAttributes.ReparsePoint) != 0
                    || !long.TryParse(Path.GetFileName(epochPath), out var epoch)
                    || epoch <= 0)
                {
                    return Corrupt<bool>("The repository contains an invalid operation address.");
                }
                foreach (var recordPath in Directory.EnumerateFiles(
                             epochPath,
                             "*.operation",
                             SearchOption.TopDirectoryOnly))
                {
                    var nonceName = Path.GetFileNameWithoutExtension(recordPath);
                    if (!Guid.TryParseExact(nonceName, "D", out var nonce)
                        || nonce == Guid.Empty)
                    {
                        return Corrupt<bool>("The repository contains an invalid operation address.");
                    }
                    var operationId = new OperationId(epoch, nonce);
                    var loaded = ReadRecord(recordPath, repositoryId, operationId);
                    if (loaded.IsErr)
                        return loaded.Map(_ => false);
                    var operation = loaded.Match(value => value, _ => null!);
                    if (operation.CurrentState is OperationLifecycleState.Applied
                        or OperationLifecycleState.CancelledNoEffect
                        or OperationLifecycleState.RejectedStale
                        or OperationLifecycleState.RejectedPlanUnavailable
                        or OperationLifecycleState.RejectedUnsafeCoordination
                        or OperationLifecycleState.Expired
                        || operation.PreEffectActivity.HasAdmissionHistory)
                    {
                        // A terminal or drained-admission replacement may be
                        // visible after its directory flush failed. Prove its
                        // durability before admitting any later mutation.
                        _durability.FlushDirectoryMetadata(epochPath);
                    }
                    if (operationId == excludedOperationId)
                        continue;
                    if (!operation.PreEffectActivity.ActiveAdmissions.IsEmpty
                        || operation.CurrentState is OperationLifecycleState.Applying
                        or OperationLifecycleState.OwnerQuiescencePending
                        or OperationLifecycleState.Reconciling
                        or OperationLifecycleState.RecoveryRequired
                        or OperationLifecycleState.Recovering)
                    {
                        return Result<bool, OperationLedgerError>.Ok(true);
                    }
                }
            }
            return Result<bool, OperationLedgerError>.Ok(false);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<bool, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<DurableOperationRecord, OperationLedgerError> AppendTransition(
        string repositoryId,
        OperationId operationId,
        OperationLifecycleState expectedCurrentState,
        OperationLifecycleState nextState,
        string? evidence)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        if (evidence is not null)
            evidence = OperationRequestText.Required(evidence, nameof(evidence));

        try
        {
            var protectionError = AcquireProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableOperationRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            if (!Directory.Exists(_rootPath))
                return Missing<DurableOperationRecord>();
            using var ledgerLock = AcquireMutationLock();

            var recordPath = GetRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableOperationRecord>();
            var loaded = ReadRecord(recordPath, repositoryId, operationId);
            if (loaded.IsErr)
                return loaded;
            var operation = loaded.Match(record => record, _ => null!);
            if (operation.CurrentState != expectedCurrentState)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.Conflict,
                        // Stryker disable once String : Conflict wording is diagnostic; the typed kind is the contract.
                        $"The operation is '{operation.CurrentState}', not the expected '{expectedCurrentState}'."));
            }
            if (!CanTransition(expectedCurrentState, nextState))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        // Stryker disable once String : Transition wording is diagnostic; the typed kind is the contract.
                        $"Operation lifecycle cannot transition from '{expectedCurrentState}' to '{nextState}'."));
            }
            if (RequiresMutationProof(expectedCurrentState, nextState))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "A repository mutation outcome requires durable effect or no-effect verification."));
            }
            var noEffectTerminal = nextState is OperationLifecycleState.CancelledNoEffect
                or OperationLifecycleState.RejectedStale
                or OperationLifecycleState.RejectedPlanUnavailable
                or OperationLifecycleState.RejectedUnsafeCoordination
                or OperationLifecycleState.Expired;
            if (noEffectTerminal
                && (!operation.EffectJournal.IsEmpty
                    || !operation.PreEffectActivity.ActiveAdmissions.IsEmpty))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "A no-effect outcome with active control work requires fenced resolution."));
            }

            var committedAtUtc = _utcNow().ToUniversalTime();
            var previousCommittedAtUtc = operation.Transitions[^1].CommittedAtUtc;
            // Stryker disable once Equality : Equal operands select the same timestamp through either branch.
            committedAtUtc = committedAtUtc < previousCommittedAtUtc
                ? previousCommittedAtUtc
                : committedAtUtc;
            if (!operation.EffectJournal.IsEmpty
                && committedAtUtc < operation.EffectJournal[^1].CommittedAtUtc)
            {
                committedAtUtc = operation.EffectJournal[^1].CommittedAtUtc;
            }
            var transitions = operation.Transitions.Add(new OperationTransition(
                operation.Transitions[^1].Sequence + 1,
                nextState,
                committedAtUtc,
                evidence));
            var updated = operation with
            {
                CurrentState = nextState,
                Transitions = transitions,
                PreEffectActivity = nextState is not (OperationLifecycleState.Received
                        or OperationLifecycleState.Prepared
                        or OperationLifecycleState.Applying)
                    && !operation.PreEffectActivity.IsClosed
                    ? operation.PreEffectActivity with
                    {
                        IsClosed = true,
                        ControlGeneration = checked(
                            operation.PreEffectActivity.ControlGeneration + 1)
                    }
                    : operation.PreEffectActivity
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableOperationRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<PreEffectActivityAdmission, OperationLedgerError> BeginPreEffectActivity(
        string repositoryId,
        OperationId operationId,
        RepositoryMutationReservation reservation)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(reservation);
        reservation.AssertHeld(repositoryId);

        var token = Guid.NewGuid();
        var updated = UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            var activity = operation.PreEffectActivity;
            if (operation.CurrentState is not (OperationLifecycleState.Prepared or OperationLifecycleState.Applying)
                || activity.IsClosed
                || !operation.EffectJournal.IsEmpty
                || activity.ActiveAdmissions.Length >= MaximumActivePreEffectActivities
                || (operation.CurrentState == OperationLifecycleState.Applying
                    && (!OwnerMatches(operation.OwnerLease, reservation)
                        || operation.OwnerLease!.ExpiresAtUtc <= _utcNow().ToUniversalTime())))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Pre-effect activity is closed or the current owner cannot admit more work."));
            }

            var admission = new PreEffectActivityAdmission(
                token,
                activity.ControlGeneration,
                reservation.FenceEpoch,
                reservation.FenceToken);
            var next = operation with
            {
                PreEffectActivity = activity with
                {
                    HasAdmissionHistory = true,
                    ActiveAdmissions = activity.ActiveAdmissions.Add(admission)
                }
            };
            WriteRecord(recordPath, next);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(next);
        });
        return updated.Match(
            record => Result<PreEffectActivityAdmission, OperationLedgerError>.Ok(
                record.PreEffectActivity.ActiveAdmissions[^1]),
            Result<PreEffectActivityAdmission, OperationLedgerError>.Err);
    }

    internal Result<DurableOperationRecord, OperationLedgerError> CompletePreEffectActivity(
        string repositoryId,
        OperationId operationId,
        PreEffectActivityAdmission admission,
        RepositoryMutationReservation reservation)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(reservation);
        reservation.AssertHeld(repositoryId);

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            var activity = operation.PreEffectActivity;
            if (admission.FenceEpoch != reservation.FenceEpoch
                || admission.ReservationToken != reservation.FenceToken
                || !activity.ActiveAdmissions.Contains(admission))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Only the admitted reservation may complete pre-effect activity."));
            }

            var next = operation with
            {
                PreEffectActivity = activity with
                {
                    ActiveAdmissions = activity.ActiveAdmissions.Remove(admission)
                }
            };
            WriteRecord(recordPath, next);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(next);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> CancelBeforeFirstEffect(
        string repositoryId,
        OperationId operationId)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            if (operation.CurrentState is not (OperationLifecycleState.Prepared or OperationLifecycleState.Applying)
                || !operation.EffectJournal.IsEmpty
                || operation.PreEffectActivity.IsClosed)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Cancellation cannot prove no repository effect after first-effect admission."));
            }

            var now = _utcNow().ToUniversalTime();
            if (now < operation.Transitions[^1].CommittedAtUtc)
                now = operation.Transitions[^1].CommittedAtUtc;
            var next = operation with
            {
                CurrentState = OperationLifecycleState.CancelledNoEffect,
                PreEffectActivity = operation.PreEffectActivity with
                {
                    IsClosed = true,
                    ControlGeneration = checked(operation.PreEffectActivity.ControlGeneration + 1)
                },
                Transitions = operation.Transitions.Add(new OperationTransition(
                    operation.Transitions[^1].Sequence + 1,
                    OperationLifecycleState.CancelledNoEffect,
                    now,
                    "Cancellation won before the first repository effect intent."))
            };
            WriteRecord(recordPath, next);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(next);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> ClaimApplyOwner(
        string repositoryId,
        OperationId operationId,
        RepositoryMutationReservation reservation,
        string planHash)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(reservation);
        reservation.AssertHeld(repositoryId);
        planHash = ChangePlanValidation.NormalizeSha256(planHash, nameof(planHash));

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            if (operation.CurrentState != OperationLifecycleState.Prepared
                || operation.OwnerLease is not null
                || !FixedTimeHashEquals(operation.RequestIdentity.PlanHash, planHash))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "A prepared exact plan is required to claim apply ownership."));
            }

            var now = _utcNow().ToUniversalTime();
            if (now < operation.Transitions[^1].CommittedAtUtc)
                now = operation.Transitions[^1].CommittedAtUtc;
            var updated = operation with
            {
                CurrentState = OperationLifecycleState.Applying,
                OwnerLease = new OperationOwnerLease(
                    Guid.NewGuid(),
                    reservation.FenceEpoch,
                    reservation.FenceToken,
                    now,
                    now + ApplyOwnerLeaseLifetime),
                Transitions = operation.Transitions.Add(new OperationTransition(
                    operation.Transitions[^1].Sequence + 1,
                    OperationLifecycleState.Applying,
                    now,
                    planHash))
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> RenewApplyOwner(
        string repositoryId,
        OperationId operationId,
        RepositoryMutationReservation reservation)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(reservation);
        reservation.AssertHeld(repositoryId);

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            var observedNow = _utcNow().ToUniversalTime();
            if (operation.CurrentState != OperationLifecycleState.Applying
                || !OwnerMatches(operation.OwnerLease, reservation)
                || operation.OwnerLease!.ExpiresAtUtc <= observedNow)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Only the held apply owner may renew its lease."));
            }
            var now = observedNow;
            if (now < operation.OwnerLease!.HeartbeatAtUtc)
                now = operation.OwnerLease.HeartbeatAtUtc;
            var updated = operation with
            {
                OwnerLease = operation.OwnerLease with
                {
                    HeartbeatAtUtc = now,
                    ExpiresAtUtc = now + ApplyOwnerLeaseLifetime
                }
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> AppendEffectIntent(
        string repositoryId,
        OperationId operationId,
        int effectSequence,
        string planEffectHash,
        RepositoryMutationReservation? reservation = null,
        string? effectHash = null,
        ImmutableArray<OperationGuardToken> guardTokens = default)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effectSequence);
        planEffectHash = ChangePlanValidation.NormalizeSha256(planEffectHash, nameof(planEffectHash));
        if (effectHash is not null)
            effectHash = ChangePlanValidation.NormalizeSha256(effectHash, nameof(effectHash));
        if (reservation is not null && guardTokens.IsDefaultOrEmpty)
            throw new ArgumentException("A guarded effect intent requires guard tokens.", nameof(guardTokens));
        reservation?.AssertHeld(repositoryId);

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            if (operation.CurrentState != OperationLifecycleState.Applying)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "An effect intent requires an applying operation."));
            }
            if (reservation is not null
                && (!OwnerMatches(operation.OwnerLease, reservation)
                    || operation.OwnerLease!.ExpiresAtUtc <= _utcNow().ToUniversalTime()))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Effect intent requires the current unexpired apply owner."));
            }

            var journal = operation.EffectJournal;
            if (!journal.IsEmpty && journal[^1].Kind is
                    OperationEffectJournalKind.Intent or OperationEffectJournalKind.Admitted)
            {
                var pending = journal[^1];
                return pending.Kind == OperationEffectJournalKind.Intent
                       && pending.EffectSequence == effectSequence
                       && FixedTimeHashEquals(pending.EvidenceHash, planEffectHash)
                    ? Result<DurableOperationRecord, OperationLedgerError>.Ok(operation)
                    : Result<DurableOperationRecord, OperationLedgerError>.Err(
                        new OperationLedgerError(
                            OperationLedgerErrorKind.Conflict,
                            "A different effect intent is already pending."));
            }

            var nextEffectSequence = journal.IsEmpty ? 1 : journal[^1].EffectSequence + 1;
            if (effectSequence != nextEffectSequence)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Effect intents must follow the reviewed plan order."));
            }
            if ((journal.IsEmpty && operation.PreEffectActivity.IsClosed)
                || !operation.PreEffectActivity.ActiveAdmissions.IsEmpty
                || (!journal.IsEmpty && !operation.PreEffectActivity.IsClosed))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "The first effect requires a drained pre-effect activity fence."));
            }

            var committedAtUtc = _utcNow().ToUniversalTime();
            var lastCommit = journal.IsEmpty
                ? operation.Transitions[^1].CommittedAtUtc
                : journal[^1].CommittedAtUtc;
            if (committedAtUtc < lastCommit)
                committedAtUtc = lastCommit;
            var updated = operation with
            {
                PreEffectActivity = journal.IsEmpty
                    ? operation.PreEffectActivity with
                    {
                        IsClosed = true,
                        ControlGeneration = checked(
                            operation.PreEffectActivity.ControlGeneration + 1)
                    }
                    : operation.PreEffectActivity,
                EffectJournal = journal.Add(new OperationEffectJournalEvent(
                    journal.IsEmpty ? 1 : journal[^1].Sequence + 1,
                    effectSequence,
                    OperationEffectJournalKind.Intent,
                    planEffectHash,
                    committedAtUtc)
                {
                    FenceEpoch = reservation?.FenceEpoch,
                    FenceToken = reservation?.FenceToken,
                    EffectHash = effectHash,
                    GuardTokens = guardTokens.IsDefault ? [] : guardTokens
                })
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> AppendEffectAdmission(
        string repositoryId,
        OperationId operationId,
        int effectSequence,
        string intentHash,
        RepositoryMutationReservation reservation)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effectSequence);
        ArgumentNullException.ThrowIfNull(reservation);
        reservation.AssertHeld(repositoryId);
        intentHash = ChangePlanValidation.NormalizeSha256(intentHash, nameof(intentHash));

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            var journal = operation.EffectJournal;
            if (operation.CurrentState != OperationLifecycleState.Applying
                || journal.IsEmpty
                || !OwnerMatches(operation.OwnerLease, reservation)
                || operation.OwnerLease!.ExpiresAtUtc <= _utcNow().ToUniversalTime()
                || journal[^1].Kind != OperationEffectJournalKind.Intent
                || journal[^1].EffectSequence != effectSequence
                || journal[^1].FenceEpoch != reservation.FenceEpoch
                || journal[^1].FenceToken != reservation.FenceToken
                || !FixedTimeHashEquals(journal[^1].EvidenceHash, intentHash))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Effect admission requires the current fenced durable intent."));
            }

            var intent = journal[^1];
            var committedAtUtc = _utcNow().ToUniversalTime();
            if (committedAtUtc < intent.CommittedAtUtc)
                committedAtUtc = intent.CommittedAtUtc;
            var updated = operation with
            {
                EffectJournal = journal.Add(new OperationEffectJournalEvent(
                    intent.Sequence + 1,
                    effectSequence,
                    OperationEffectJournalKind.Admitted,
                    intentHash,
                    committedAtUtc)
                {
                    FenceEpoch = intent.FenceEpoch,
                    FenceToken = intent.FenceToken,
                    EffectHash = intent.EffectHash,
                    GuardTokens = intent.GuardTokens
                })
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> AppendEffectObservation(
        string repositoryId,
        OperationId operationId,
        int effectSequence,
        string observedStateHash,
        RepositoryMutationReservation? reservation = null)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(effectSequence);
        observedStateHash = ChangePlanValidation.NormalizeSha256(
            observedStateHash,
            nameof(observedStateHash));
        reservation?.AssertHeld(repositoryId);

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            if (operation.CurrentState != OperationLifecycleState.Applying)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "An effect observation requires an applying operation."));
            }

            var journal = operation.EffectJournal;
            if (journal.IsEmpty)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "An effect cannot be observed before its intent is durable."));
            }
            var last = journal[^1];
            if (last.FenceEpoch.HasValue
                && (reservation is null
                    || !OwnerMatches(operation.OwnerLease, reservation)))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "A guarded observation requires the held apply owner."));
            }
            if (last.Kind == OperationEffectJournalKind.Observed)
            {
                return last.EffectSequence == effectSequence
                       && FixedTimeHashEquals(last.EvidenceHash, observedStateHash)
                    ? Result<DurableOperationRecord, OperationLedgerError>.Ok(operation)
                    : Result<DurableOperationRecord, OperationLedgerError>.Err(
                        new OperationLedgerError(
                            OperationLedgerErrorKind.Conflict,
                            "A different effect observation is already durable."));
            }
            if (last.EffectSequence != effectSequence
                || (last.FenceEpoch.HasValue
                    && last.Kind != OperationEffectJournalKind.Admitted)
                || (!last.FenceEpoch.HasValue
                    && last.Kind != OperationEffectJournalKind.Intent))
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "An effect observation must match the pending intent."));
            }

            var committedAtUtc = _utcNow().ToUniversalTime();
            if (committedAtUtc < last.CommittedAtUtc)
                committedAtUtc = last.CommittedAtUtc;
            var updated = operation with
            {
                EffectJournal = journal.Add(new OperationEffectJournalEvent(
                    last.Sequence + 1,
                    effectSequence,
                    OperationEffectJournalKind.Observed,
                    observedStateHash,
                    committedAtUtc)
                {
                    FenceEpoch = last.FenceEpoch,
                    FenceToken = last.FenceToken,
                    EffectHash = last.EffectHash,
                    GuardTokens = last.GuardTokens
                })
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> AppendVerifiedApplied(
        StoredChangePlan plan,
        OperationId operationId,
        RepositoryMutationReservation reservation,
        string verificationHash)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(operationId);
        ArgumentNullException.ThrowIfNull(reservation);
        var repositoryId = plan.Key.RepositoryId;
        reservation.AssertHeld(repositoryId);
        verificationHash = ChangePlanValidation.NormalizeSha256(
            verificationHash,
            nameof(verificationHash));

        return UpdateEffectJournal(repositoryId, operationId, (operation, recordPath) =>
        {
            if (operation.CurrentState != OperationLifecycleState.Applying
                || !OwnerMatches(operation.OwnerLease, reservation)
                || operation.OwnerLease!.ExpiresAtUtc <= _utcNow().ToUniversalTime()
                || operation.RequestIdentity.PlanId != plan.Key.PlanId
                || !FixedTimeHashEquals(operation.RequestIdentity.PlanHash, plan.Key.PlanHash)
                || !plan.Pins.Any(pin =>
                    pin.Kind == ChangePlanPinKind.OperationLedger
                    && string.Equals(
                        pin.ReferenceId,
                        DurableDispatchIntakeCoordinator.FormatOperationReference(operationId),
                        StringComparison.Ordinal))
                || operation.EffectJournal.Length != plan.Envelope.Payload.Effects.Length * 3)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "Applied requires the exact pinned plan and complete observed effect journal."));
            }
            for (var index = 0; index < plan.Envelope.Payload.Effects.Length; index++)
            {
                var effect = plan.Envelope.Payload.Effects[index];
                var intent = operation.EffectJournal[index * 3];
                var admission = operation.EffectJournal[index * 3 + 1];
                var observed = operation.EffectJournal[index * 3 + 2];
                if (intent.Kind != OperationEffectJournalKind.Intent
                    || admission.Kind != OperationEffectJournalKind.Admitted
                    || observed.Kind != OperationEffectJournalKind.Observed
                    || intent.EffectSequence != effect.Sequence
                    || admission.EffectSequence != effect.Sequence
                    || observed.EffectSequence != effect.Sequence
                    || intent.FenceEpoch != reservation.FenceEpoch
                    || intent.FenceToken != reservation.FenceToken
                    || observed.FenceEpoch != reservation.FenceEpoch
                    || observed.FenceToken != reservation.FenceToken
                    || admission.FenceEpoch != reservation.FenceEpoch
                    || admission.FenceToken != reservation.FenceToken
                    || intent.EffectHash is null
                    || !FixedTimeHashEquals(
                        intent.EffectHash,
                        CanonicalChangePlan.ComputeEffectHash(effect))
                    || observed.EffectHash != intent.EffectHash
                    || admission.EffectHash != intent.EffectHash
                    || !intent.GuardTokens.SequenceEqual(
                        OperationGuardTokenSet.Expected(plan.Envelope.Payload))
                    || !admission.GuardTokens.SequenceEqual(intent.GuardTokens)
                    || !observed.GuardTokens.SequenceEqual(intent.GuardTokens)
                    || !FixedTimeHashEquals(
                        intent.EvidenceHash,
                        OperationEffectIntentIdentity.Compute(
                            plan.Key.PlanHash,
                            effect.Sequence,
                            reservation.FenceToken,
                            reservation.FenceEpoch)))
                {
                    return Result<DurableOperationRecord, OperationLedgerError>.Err(
                        new OperationLedgerError(
                            OperationLedgerErrorKind.InvalidTransition,
                            "The observed effect journal does not match the held plan and fence."));
                }
            }

            var committedAtUtc = _utcNow().ToUniversalTime();
            if (committedAtUtc < operation.EffectJournal[^1].CommittedAtUtc)
                committedAtUtc = operation.EffectJournal[^1].CommittedAtUtc;
            var updated = operation with
            {
                CurrentState = OperationLifecycleState.Applied,
                Transitions = operation.Transitions.Add(new OperationTransition(
                    operation.Transitions[^1].Sequence + 1,
                    OperationLifecycleState.Applied,
                    committedAtUtc,
                    verificationHash))
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
        });
    }

    internal Result<DurableOperationRecord, OperationLedgerError> ConfirmDurableApplied(
        string repositoryId,
        OperationId operationId,
        string verificationHash)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);
        verificationHash = ChangePlanValidation.NormalizeSha256(
            verificationHash,
            nameof(verificationHash));

        try
        {
            var protectionError = AcquireExistingProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableOperationRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            using var ledgerLock = AcquireMutationLock();
            var recordPath = GetRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableOperationRecord>();
            var loaded = ReadRecord(recordPath, repositoryId, operationId);
            if (loaded.IsErr)
                return loaded;
            var record = loaded.Match(value => value, _ => null!);
            if (record.CurrentState != OperationLifecycleState.Applied
                || record.Transitions[^1].Evidence != verificationHash)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "The verified applied outcome was not found during durability confirmation."));
            }

            _durability.FlushDirectoryMetadata(Path.GetDirectoryName(recordPath)!);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(record);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableOperationRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal Result<DurableOperationRecord, OperationLedgerError> ConfirmDurableCancelledNoEffect(
        string repositoryId,
        OperationId operationId)
    {
        repositoryId = OperationRequestText.Required(repositoryId, nameof(repositoryId));
        ArgumentNullException.ThrowIfNull(operationId);

        try
        {
            var protectionError = AcquireExistingProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableOperationRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            using var ledgerLock = AcquireMutationLock();
            var recordPath = GetRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableOperationRecord>();
            var loaded = ReadRecord(recordPath, repositoryId, operationId);
            if (loaded.IsErr)
                return loaded;
            var record = loaded.Match(value => value, _ => null!);
            if (record.CurrentState != OperationLifecycleState.CancelledNoEffect
                || !record.EffectJournal.IsEmpty
                || !record.PreEffectActivity.IsClosed
                || !record.PreEffectActivity.ActiveAdmissions.IsEmpty)
            {
                return Result<DurableOperationRecord, OperationLedgerError>.Err(
                    new OperationLedgerError(
                        OperationLedgerErrorKind.InvalidTransition,
                        "The drained no-effect cancellation was not found during durability confirmation."));
            }

            _durability.FlushDirectoryMetadata(Path.GetDirectoryName(recordPath)!);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(record);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableOperationRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    private Result<DurableOperationRecord, OperationLedgerError> UpdateEffectJournal(
        string repositoryId,
        OperationId operationId,
        Func<DurableOperationRecord, string, Result<DurableOperationRecord, OperationLedgerError>> update)
    {
        try
        {
            var protectionError = AcquireProtection(out var protectionLease);
            if (protectionError is not null)
                return Result<DurableOperationRecord, OperationLedgerError>.Err(protectionError);
            using var heldProtectionLease = protectionLease!;
            if (!Directory.Exists(_rootPath))
                return Missing<DurableOperationRecord>();
            using var ledgerLock = AcquireMutationLock();

            var recordPath = GetRecordPath(_rootPath, repositoryId, operationId);
            if (!File.Exists(recordPath))
                return Missing<DurableOperationRecord>();
            var loaded = ReadRecord(recordPath, repositoryId, operationId);
            if (loaded.IsErr)
                return loaded;
            return update(loaded.Match(record => record, _ => null!), recordPath);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<DurableOperationRecord, OperationLedgerError>.Err(StorageError(exception));
        }
    }

    internal static string GetRecordPath(
        string ledgerRootPath,
        string repositoryId,
        OperationId operationId)
    {
        var repositoryDigest = Digest(repositoryId);
        return Path.Combine(
            Path.GetFullPath(ledgerRootPath),
            "operations",
            repositoryDigest,
            operationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{operationId.Nonce:D}.operation");
    }

    internal static string GetDispatchRecordPath(
        string ledgerRootPath,
        string repositoryId,
        OperationId operationId)
    {
        var repositoryDigest = Digest(repositoryId);
        return Path.Combine(
            Path.GetFullPath(ledgerRootPath),
            "dispatch",
            repositoryDigest,
            operationId.Epoch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            $"{operationId.Nonce:D}.dispatch");
    }

    private OperationLedgerError? AcquireProtection(
        out IOperationLedgerProtectionLease? protectionLease)
        => AcquireProtection(
            _protectionAuthority.Acquire,
            out protectionLease);

    private OperationLedgerError? AcquireExistingProtection(
        out IOperationLedgerProtectionLease? protectionLease)
        => AcquireProtection(
            _protectionAuthority.AcquireExisting,
            out protectionLease);

    private OperationLedgerError? AcquireProtection(
        ProtectionAcquirer acquire,
        out IOperationLedgerProtectionLease? protectionLease)
    {
        protectionLease = null;
        IOperationLedgerProtectionLease? acquiredLease = null;
        try
        {
            var error = acquire(_rootPath, out acquiredLease);
            if (error is not null)
            {
                acquiredLease?.Dispose();
                return error;
            }
            if (acquiredLease is null)
            {
                return new OperationLedgerError(
                    OperationLedgerErrorKind.UnsafeProtection,
                    // Stryker disable once String : Failure wording is diagnostic; callers branch on the typed error kind.
                    "The operation-ledger protection authority returned no physical protection lease.");
            }

            protectionLease = acquiredLease;
            return null;
        }
        catch
        {
            acquiredLease?.Dispose();
            throw;
        }
    }

    private delegate OperationLedgerError? ProtectionAcquirer(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease);

    private void EnsureProtectedRoot()
    {
        CreatePrivateDirectoryTree(_operationsPath);
        ApplyPrivateDirectoryPermissions(_rootPath);
        ApplyPrivateDirectoryPermissions(_operationsPath);
    }

    private void EnsureProtectedDispatchRoot()
    {
        CreatePrivateDirectoryTree(_dispatchPath);
        ApplyPrivateDirectoryPermissions(_rootPath);
        ApplyPrivateDirectoryPermissions(_dispatchPath);
    }

    private FileStream AcquireMutationLock()
    {
        var deadline = DateTime.UtcNow + LockTimeout;
        while (true)
        {
            try
            {
                var lockStream = new FileStream(
                    _lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    1,
                    FileOptions.WriteThrough);
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(_lockPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return lockStream;
            }
            // Stryker disable once Equality : Timeout equality changes contention pacing only; FileShare.None remains authoritative.
            catch (IOException) when (DateTime.UtcNow < deadline)
            // Stryker disable all : Removing retry pacing leaves the same exclusive FileShare.None admission boundary.
            {
                Thread.Sleep(10);
            }
            // Stryker restore all
        }
    }

    private static Result<DurableOperationRecord, OperationLedgerError> ReadRecord(
        string recordPath,
        string expectedRepositoryId,
        OperationId expectedOperationId)
    {
        LedgerRecord? stored;
        try
        {
            stored = JsonSerializer.Deserialize<LedgerRecord>(
                File.ReadAllBytes(recordPath),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            // Stryker disable once String : Corruption wording is diagnostic; the typed kind and exception evidence are the contract.
            return Corrupt<DurableOperationRecord>("The operation ledger record is not valid JSON.", exception);
        }
        if (stored is null || stored.SchemaVersion != StoreSchemaVersion)
            // Stryker disable once String : Corruption wording is diagnostic; the typed kind is the contract.
            return Corrupt<DurableOperationRecord>("The operation ledger record has an unsupported schema.");

        try
        {
            var operationId = new OperationId(stored.OperationEpoch, stored.OperationNonce);
            if (!string.Equals(stored.RepositoryId, expectedRepositoryId, StringComparison.Ordinal)
                || operationId != expectedOperationId)
            {
                return Corrupt<DurableOperationRecord>(
                    // Stryker disable once String : Corruption wording is diagnostic; the typed kind is the contract.
                    "The operation ledger record does not match its durable address.");
            }

            var canonicalBytes = Convert.FromBase64String(stored.CanonicalRequestBase64);
            var identity = new OperationRequestIdentity(
                stored.RequestHash,
                stored.PlanId,
                stored.PlanHash,
                stored.ExpectedRevision,
                stored.OperationEpochClaimsHash);
            var canonicalHash = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
            if (!FixedTimeHashEquals(canonicalHash, identity.RequestHash))
            {
                return Corrupt<DurableOperationRecord>(
                    // Stryker disable once String : Corruption wording is diagnostic; the typed kind is the contract.
                    "The operation ledger canonical request does not match its request hash.");
            }
            var expectedCanonicalBytes = CanonicalOperationRequest.SerializeApplyRequest(
                expectedRepositoryId,
                operationId,
                identity.PlanId,
                identity.PlanHash,
                identity.ExpectedRevision,
                identity.OperationEpochClaimsHash);
            if (!CryptographicOperations.FixedTimeEquals(canonicalBytes, expectedCanonicalBytes))
            {
                return Corrupt<DurableOperationRecord>(
                    // Stryker disable once String : Corruption wording is diagnostic; the typed kind is the contract.
                    "The operation ledger request metadata does not match its canonical request.");
            }

            if (stored.Transitions is null || stored.Transitions.Any(static transition => transition is null))
                // Stryker disable once String : Corruption wording is diagnostic; the typed kind is the contract.
                return Corrupt<DurableOperationRecord>("The operation lifecycle history is missing.");
            var transitions = stored.Transitions.Select(transition => new OperationTransition(
                transition!.Sequence,
                transition.State,
                transition.CommittedAtUtc.ToUniversalTime(),
                transition.Evidence)).ToImmutableArray();
            if (!ValidHistory(transitions, stored.CurrentState))
                // Stryker disable once String : Corruption wording is diagnostic; the typed kind is the contract.
                return Corrupt<DurableOperationRecord>("The operation lifecycle history is inconsistent.");

            if (stored.EffectJournal is not null
                && stored.EffectJournal.Any(static journalEvent => journalEvent is null))
            {
                return Corrupt<DurableOperationRecord>("The effect journal contains a missing event.");
            }
            var effectJournal = stored.EffectJournal is null
                ? ImmutableArray<OperationEffectJournalEvent>.Empty
                : stored.EffectJournal.Select(journalEvent => new OperationEffectJournalEvent(
                    journalEvent!.Sequence,
                    journalEvent.EffectSequence,
                    journalEvent.Kind,
                    ChangePlanValidation.NormalizeSha256(
                        journalEvent.EvidenceHash,
                        nameof(journalEvent.EvidenceHash)),
                    journalEvent.CommittedAtUtc.ToUniversalTime())
                {
                    FenceEpoch = journalEvent.FenceEpoch,
                    FenceToken = journalEvent.FenceToken,
                    EffectHash = journalEvent.EffectHash is null
                        ? null
                        : ChangePlanValidation.NormalizeSha256(
                            journalEvent.EffectHash,
                            nameof(journalEvent.EffectHash)),
                    GuardTokens = journalEvent.GuardTokens is null
                        ? []
                        : journalEvent.GuardTokens.ToImmutableArray()
                }).ToImmutableArray();
            if (!ValidEffectJournal(effectJournal, transitions))
                return Corrupt<DurableOperationRecord>("The effect journal is inconsistent.");
            var ownerLease = stored.OwnerLease;
            if (ownerLease is not null
                && (ownerLease.OwnerInstanceId == Guid.Empty
                    || ownerLease.FenceEpoch <= 0
                    || ownerLease.ReservationToken == Guid.Empty
                    || ownerLease.ExpiresAtUtc <= ownerLease.HeartbeatAtUtc
                    || !transitions.Any(transition =>
                        transition.State == OperationLifecycleState.Applying)))
            {
                return Corrupt<DurableOperationRecord>("The apply owner lease is invalid.");
            }
            if (effectJournal.Any(journalEvent => journalEvent.FenceEpoch.HasValue
                && (ownerLease is null
                    || journalEvent.FenceEpoch != ownerLease.FenceEpoch
                    || journalEvent.FenceToken != ownerLease.ReservationToken)))
            {
                return Corrupt<DurableOperationRecord>("The effect journal does not match its apply owner.");
            }
            var preEffectActivity = stored.PreEffectActivity ?? new PreEffectActivityState(
                1,
                stored.CurrentState is not (OperationLifecycleState.Received or OperationLifecycleState.Prepared),
                []);
            if (preEffectActivity.ControlGeneration <= 0
                || preEffectActivity.ActiveAdmissions.IsDefault
                || preEffectActivity.ActiveAdmissions.Length > MaximumActivePreEffectActivities
                || (!preEffectActivity.HasAdmissionHistory
                    && !preEffectActivity.ActiveAdmissions.IsEmpty)
                || preEffectActivity.ActiveAdmissions.Any(admission =>
                    admission is null
                    || admission.Token == Guid.Empty
                    || admission.ControlGeneration <= 0
                    || admission.ControlGeneration > preEffectActivity.ControlGeneration
                    || admission.FenceEpoch <= 0
                    || admission.ReservationToken == Guid.Empty)
                || preEffectActivity.ActiveAdmissions.Select(admission => admission.Token).Distinct().Count()
                    != preEffectActivity.ActiveAdmissions.Length
                || (!preEffectActivity.IsClosed
                    && (effectJournal.Length > 0
                        || stored.CurrentState is not (OperationLifecycleState.Received
                            or OperationLifecycleState.Prepared
                            or OperationLifecycleState.Applying))))
            {
                return Corrupt<DurableOperationRecord>("The pre-effect activity fence is inconsistent.");
            }

            return Result<DurableOperationRecord, OperationLedgerError>.Ok(new DurableOperationRecord(
                stored.RepositoryId,
                operationId,
                identity,
                ImmutableArray.CreateRange(canonicalBytes),
                OperationRequestText.Required(stored.SubmittingPrincipalId, nameof(stored.SubmittingPrincipalId)),
                stored.CurrentState,
                transitions)
            {
                EffectJournal = effectJournal,
                OwnerLease = ownerLease,
                PreEffectActivity = preEffectActivity
            });
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // Stryker disable once String : Corruption wording is diagnostic; the typed kind and exception evidence are the contract.
            return Corrupt<DurableOperationRecord>("The operation ledger record contains invalid canonical data.", exception);
        }
    }

    private static Result<DurableDispatchIntakeRecord, OperationLedgerError> ReadDispatchRecord(
        string recordPath,
        string expectedRepositoryId,
        OperationId expectedOperationId)
    {
        DispatchRecord? stored;
        try
        {
            stored = JsonSerializer.Deserialize<DispatchRecord>(
                File.ReadAllBytes(recordPath),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            return Corrupt<DurableDispatchIntakeRecord>(
                // Stryker disable once String : The typed corruption and exception evidence are the contract; wording is diagnostic.
                "The dispatch-intake record is not valid JSON.",
                exception);
        }
        if (stored is null || stored.SchemaVersion != StoreSchemaVersion)
        {
            return Corrupt<DurableDispatchIntakeRecord>(
                // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                "The dispatch-intake record has an unsupported schema.");
        }

        try
        {
            var operationId = new OperationId(stored.OperationEpoch, stored.OperationNonce);
            if (!string.Equals(stored.RepositoryId, expectedRepositoryId, StringComparison.Ordinal)
                || operationId != expectedOperationId)
            {
                return Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                    "The dispatch-intake record does not match its durable address.");
            }

            var canonicalBytes = Convert.FromBase64String(stored.CanonicalRequestBase64);
            var identity = new OperationRequestIdentity(
                stored.RequestHash,
                stored.PlanId,
                stored.PlanHash,
                stored.ExpectedRevision,
                stored.OperationEpochClaimsHash);
            var canonicalHash = Convert.ToHexString(SHA256.HashData(canonicalBytes)).ToLowerInvariant();
            if (!FixedTimeHashEquals(canonicalHash, identity.RequestHash))
            {
                return Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                    "The dispatch-intake canonical request does not match its request hash.");
            }
            var expectedCanonicalBytes = CanonicalOperationRequest.SerializeApplyRequest(
                expectedRepositoryId,
                operationId,
                identity.PlanId,
                identity.PlanHash,
                identity.ExpectedRevision,
                identity.OperationEpochClaimsHash);
            if (!CryptographicOperations.FixedTimeEquals(canonicalBytes, expectedCanonicalBytes))
            {
                return Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                    "The dispatch-intake metadata does not match its canonical request.");
            }
            var dispatchLeaseId = OperationRequestText.Required(
                stored.DispatchLeaseId,
                nameof(stored.DispatchLeaseId));
            var intakeAttemptId = OperationRequestText.Required(
                stored.IntakeAttemptId,
                nameof(stored.IntakeAttemptId));
            ValidateCoordinationReference(dispatchLeaseId, nameof(stored.DispatchLeaseId));
            ValidateCoordinationReference(intakeAttemptId, nameof(stored.IntakeAttemptId));
            if (stored.Transitions is null || stored.Transitions.Any(static transition => transition is null))
            {
                return Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                    "The dispatch-intake history is missing.");
            }
            var transitions = stored.Transitions.Select(transition => new DispatchIntakeTransition(
                transition!.Sequence,
                transition.Phase,
                transition.CommittedAtUtc.ToUniversalTime(),
                transition.Evidence)).ToImmutableArray();
            if (!ValidDispatchHistory(transitions, stored.CurrentPhase))
            {
                return Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                    "The dispatch-intake history is inconsistent.");
            }
            var registeredAtUtc = transitions[0].CommittedAtUtc;
            if (stored.DispatchLeaseExpiresAtUtc.Offset != TimeSpan.Zero
                || stored.IntakeAttemptExpiresAtUtc.Offset != TimeSpan.Zero
                // At equality the following attempt clauses necessarily reject too; this explicit check documents the dispatch invariant.
                || stored.DispatchLeaseExpiresAtUtc <= registeredAtUtc
                || stored.IntakeAttemptExpiresAtUtc <= registeredAtUtc
                || stored.IntakeAttemptExpiresAtUtc > stored.DispatchLeaseExpiresAtUtc)
            {
                return Corrupt<DurableDispatchIntakeRecord>(
                    // Stryker disable once String : The typed corruption is the contract; wording is diagnostic.
                    "The dispatch-intake lease boundaries are invalid.");
            }

            return Result<DurableDispatchIntakeRecord, OperationLedgerError>.Ok(
                new DurableDispatchIntakeRecord(
                    stored.RepositoryId,
                    operationId,
                    identity,
                    ImmutableArray.CreateRange(canonicalBytes),
                    OperationRequestText.Required(
                        stored.SubmittingPrincipalId,
                        nameof(stored.SubmittingPrincipalId)),
                    dispatchLeaseId,
                    stored.DispatchLeaseExpiresAtUtc,
                    intakeAttemptId,
                    stored.IntakeAttemptExpiresAtUtc,
                    stored.CurrentPhase,
                    transitions));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            return Corrupt<DurableDispatchIntakeRecord>(
                // Stryker disable once String : The typed corruption and exception evidence are the contract; wording is diagnostic.
                "The dispatch-intake record contains invalid canonical data.",
                exception);
        }
    }

    private static void ValidateCoordinationReference(string reference, string parameterName)
    {
        if (Encoding.UTF8.GetByteCount(reference) > MaximumCoordinationReferenceBytes)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                // Stryker disable once String : The parameter and exception type are the validation contract; wording is diagnostic.
                "The coordination reference exceeds the durable byte limit.");
        }
    }

    private void WriteRecord(string recordPath, DurableOperationRecord record)
    {
        var stored = new LedgerRecord(
            StoreSchemaVersion,
            record.RepositoryId,
            record.OperationId.Epoch,
            record.OperationId.Nonce,
            record.RequestIdentity.RequestHash,
            record.RequestIdentity.PlanId,
            record.RequestIdentity.PlanHash,
            record.RequestIdentity.ExpectedRevision,
            record.RequestIdentity.OperationEpochClaimsHash,
            Convert.ToBase64String(record.CanonicalRequestBytes.AsSpan()),
            record.SubmittingPrincipalId,
            record.CurrentState,
            record.Transitions.Select(transition => new TransitionRecord(
                transition.Sequence,
                transition.State,
                transition.CommittedAtUtc,
                transition.Evidence)).ToArray(),
            record.EffectJournal.Select(journalEvent => new EffectJournalRecord(
                journalEvent.Sequence,
                journalEvent.EffectSequence,
                journalEvent.Kind,
                journalEvent.EvidenceHash,
                journalEvent.CommittedAtUtc,
                journalEvent.FenceEpoch,
                journalEvent.FenceToken,
                journalEvent.EffectHash,
                journalEvent.GuardTokens.ToArray())).ToArray(),
            record.OwnerLease,
            record.PreEffectActivity);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions);
        WriteRecordBytes(recordPath, bytes);
    }

    private void WriteDispatchRecord(string recordPath, DurableDispatchIntakeRecord record)
    {
        var stored = new DispatchRecord(
            StoreSchemaVersion,
            record.RepositoryId,
            record.OperationId.Epoch,
            record.OperationId.Nonce,
            record.RequestIdentity.RequestHash,
            record.RequestIdentity.PlanId,
            record.RequestIdentity.PlanHash,
            record.RequestIdentity.ExpectedRevision,
            record.RequestIdentity.OperationEpochClaimsHash,
            Convert.ToBase64String(record.CanonicalRequestBytes.AsSpan()),
            record.SubmittingPrincipalId,
            record.DispatchLeaseId,
            record.DispatchLeaseExpiresAtUtc,
            record.IntakeAttemptId,
            record.IntakeAttemptExpiresAtUtc,
            record.CurrentPhase,
            record.Transitions.Select(transition => new DispatchTransitionRecord(
                transition.Sequence,
                transition.Phase,
                transition.CommittedAtUtc,
                transition.Evidence)).ToArray());
        var bytes = JsonSerializer.SerializeToUtf8Bytes(stored, JsonOptions);
        WriteRecordBytes(recordPath, bytes);
    }

    private void WriteRecordBytes(string recordPath, byte[] bytes)
    {
        var directory = Path.GetDirectoryName(recordPath)!;
        CreatePrivateDirectoryTree(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(recordPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                // Stryker disable all : A power-loss flush cannot be distinguished in-process; the injected durability seam asserts ordering.
                stream.Flush(flushToDisk: true);
                // Stryker restore all
            }
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporaryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            _durability.FlushFileMetadata(temporaryPath);
            _durability.ReplaceFile(temporaryPath, recordPath);
            _durability.FlushDirectoryMetadata(directory);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private void CreatePrivateDirectoryTree(string directory)
    {
        var missing = new Stack<string>();
        // Stryker disable once Logical : Inverting this loop termination predicate creates a nonterminating mutant.
        for (var current = directory;
             !string.IsNullOrEmpty(current) && !Directory.Exists(current);
             current = Path.GetDirectoryName(current))
        {
            missing.Push(current);
        }

        while (missing.TryPop(out var path))
        {
            Directory.CreateDirectory(path);
            ApplyPrivateDirectoryPermissions(path);
            _durability.FlushDirectoryMetadata(path);
            var parent = Path.GetDirectoryName(path)!;
            _durability.FlushDirectoryMetadata(parent);
        }
    }

    private static void ApplyPrivateDirectoryPermissions(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // Stryker disable once Logical : Validated canonical bytes and their parsed identity covary; both checks retain collision defense.
    private static bool SameRequest(DurableOperationRecord operation, CanonicalApplyRequest request) =>
        operation.RequestIdentity == request.Identity
        && CryptographicOperations.FixedTimeEquals(
            operation.CanonicalRequestBytes.AsSpan(),
            request.CanonicalBytes.AsSpan());

    // Stryker disable once Logical : Validated canonical bytes and their parsed identity covary; both checks retain collision defense.
    private static bool SameRequest(DurableDispatchIntakeRecord dispatch, CanonicalApplyRequest request) =>
        dispatch.RequestIdentity == request.Identity
        && CryptographicOperations.FixedTimeEquals(
            dispatch.CanonicalRequestBytes.AsSpan(),
            request.CanonicalBytes.AsSpan());

    private static bool ValidDispatchHistory(
        ImmutableArray<DispatchIntakeTransition> transitions,
        DispatchIntakePhase currentPhase)
    {
        if (transitions.IsEmpty
            || transitions[0].Sequence != 1
            || transitions[0].Phase != DispatchIntakePhase.Registered
            || transitions[^1].Phase != currentPhase)
        {
            return false;
        }

        for (var index = 1; index < transitions.Length; index++)
        {
            if (transitions[index].Sequence != transitions[index - 1].Sequence + 1
                || transitions[index].CommittedAtUtc < transitions[index - 1].CommittedAtUtc
                || !CanAdvanceDispatch(transitions[index - 1].Phase, transitions[index].Phase))
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanAdvanceDispatch(
        DispatchIntakePhase current,
        DispatchIntakePhase next) => current switch
        {
            DispatchIntakePhase.Registered => next is
                DispatchIntakePhase.DispatchPinned
                or DispatchIntakePhase.RejectedPlanUnavailable,
            DispatchIntakePhase.DispatchPinned => next is
                DispatchIntakePhase.IntakePinned
                or DispatchIntakePhase.RejectedPlanUnavailable,
            DispatchIntakePhase.IntakePinned => next is
                DispatchIntakePhase.Received
                or DispatchIntakePhase.RejectedPlanUnavailable,
            DispatchIntakePhase.Received => next is
                DispatchIntakePhase.OperationPinned
                or DispatchIntakePhase.RejectedPlanUnavailable,
            DispatchIntakePhase.OperationPinned => next is DispatchIntakePhase.Prepared,
            // Stryker disable once Boolean : A loaded dispatch phase is validated and must match the caller's expected phase first.
            _ => false
        };

    private static bool ValidHistory(
        ImmutableArray<OperationTransition> transitions,
        OperationLifecycleState currentState)
    {
        if (transitions.IsEmpty
            || transitions[0].Sequence != 1
            || transitions[0].State != OperationLifecycleState.Received
            || transitions[^1].State != currentState)
        {
            return false;
        }

        for (var index = 1; index < transitions.Length; index++)
        {
            if (transitions[index].Sequence != transitions[index - 1].Sequence + 1
                || transitions[index].CommittedAtUtc < transitions[index - 1].CommittedAtUtc
                || !CanTransition(transitions[index - 1].State, transitions[index].State))
            {
                return false;
            }
        }
        return true;
    }

    private static bool ValidEffectJournal(
        ImmutableArray<OperationEffectJournalEvent> journal,
        ImmutableArray<OperationTransition> transitions)
    {
        if (journal.IsEmpty)
            return true;
        var applyingIndex = -1;
        for (var index = 0; index < transitions.Length; index++)
        {
            if (transitions[index].State != OperationLifecycleState.Applying)
                continue;
            applyingIndex = index;
            break;
        }
        if (applyingIndex < 0
            || journal[0].Sequence != 1
            || journal[0].EffectSequence != 1
            || journal[0].Kind != OperationEffectJournalKind.Intent
            || journal[0].CommittedAtUtc < transitions[applyingIndex].CommittedAtUtc
            || (applyingIndex + 1 < transitions.Length
                && journal[^1].CommittedAtUtc > transitions[applyingIndex + 1].CommittedAtUtc))
        {
            return false;
        }
        foreach (var journalEvent in journal)
        {
            if (journalEvent.FenceEpoch.HasValue != journalEvent.FenceToken.HasValue
                || journalEvent.FenceEpoch is <= 0
                || journalEvent.FenceToken == Guid.Empty
                || (journalEvent.FenceEpoch.HasValue
                    && (journalEvent.EffectHash is null || journalEvent.GuardTokens.IsEmpty))
                || journalEvent.GuardTokens.Any(guard => guard is null
                    || string.IsNullOrWhiteSpace(guard.Path)
                    || string.IsNullOrWhiteSpace(guard.VersionToken)
                    || !Enum.IsDefined(guard.Kind)))
            {
                return false;
            }
        }

        for (var index = 1; index < journal.Length; index++)
        {
            var previous = journal[index - 1];
            var current = journal[index];
            if (current.Sequence != previous.Sequence + 1
                || current.CommittedAtUtc < previous.CommittedAtUtc
                || current.FenceEpoch != previous.FenceEpoch
                || current.FenceToken != previous.FenceToken)
            {
                return false;
            }
            if (previous.Kind == OperationEffectJournalKind.Intent)
            {
                var expectedNext = previous.FenceEpoch.HasValue
                    ? OperationEffectJournalKind.Admitted
                    : OperationEffectJournalKind.Observed;
                if (current.Kind != expectedNext
                    || current.EffectSequence != previous.EffectSequence
                    || current.EffectHash != previous.EffectHash
                    || !current.GuardTokens.SequenceEqual(previous.GuardTokens)
                    || (current.Kind == OperationEffectJournalKind.Admitted
                        && !FixedTimeHashEquals(current.EvidenceHash, previous.EvidenceHash)))
                    return false;
            }
            else if (previous.Kind == OperationEffectJournalKind.Admitted)
            {
                if (current.Kind != OperationEffectJournalKind.Observed
                    || current.EffectSequence != previous.EffectSequence
                    || current.EffectHash != previous.EffectHash
                    || !current.GuardTokens.SequenceEqual(previous.GuardTokens))
                    return false;
            }
            else if (current.Kind != OperationEffectJournalKind.Intent
                     || current.EffectSequence != previous.EffectSequence + 1)
            {
                return false;
            }
        }
        return true;
    }

    private static bool CanTransition(
        OperationLifecycleState current,
        OperationLifecycleState next) => current switch
        {
            OperationLifecycleState.Received => next is
                OperationLifecycleState.Prepared
                or OperationLifecycleState.CancelledNoEffect
                or OperationLifecycleState.RejectedStale
                or OperationLifecycleState.RejectedPlanUnavailable
                or OperationLifecycleState.RejectedUnsafeCoordination
                or OperationLifecycleState.Expired,
            OperationLifecycleState.Prepared => next is
                OperationLifecycleState.Applying
                or OperationLifecycleState.CancelledNoEffect
                or OperationLifecycleState.RejectedStale
                or OperationLifecycleState.RejectedPlanUnavailable
                or OperationLifecycleState.RejectedUnsafeCoordination
                or OperationLifecycleState.Expired,
            OperationLifecycleState.Applying => next is
                OperationLifecycleState.Applied
                or OperationLifecycleState.RolledBack
                or OperationLifecycleState.CancelledNoEffect
                or OperationLifecycleState.RejectedStale
                or OperationLifecycleState.RejectedUnsafeCoordination
                or OperationLifecycleState.Expired
                or OperationLifecycleState.OwnerQuiescencePending
                or OperationLifecycleState.Reconciling
                or OperationLifecycleState.RecoveryRequired,
            OperationLifecycleState.OwnerQuiescencePending => next is OperationLifecycleState.Reconciling,
            OperationLifecycleState.Reconciling => next is
                OperationLifecycleState.Applied
                or OperationLifecycleState.RolledBack
                or OperationLifecycleState.CancelledNoEffect
                or OperationLifecycleState.RejectedStale
                or OperationLifecycleState.RejectedPlanUnavailable
                or OperationLifecycleState.RejectedUnsafeCoordination
                or OperationLifecycleState.Expired
                or OperationLifecycleState.RecoveryRequired,
            OperationLifecycleState.RecoveryRequired => next is
                OperationLifecycleState.Recovering
                or OperationLifecycleState.RecoveryAcknowledged,
            OperationLifecycleState.Recovering => next is
                OperationLifecycleState.RecoveredApplied
                or OperationLifecycleState.RecoveredRolledBack
                or OperationLifecycleState.RecoveryRequired
                or OperationLifecycleState.OwnerQuiescencePending
                or OperationLifecycleState.Reconciling,
            _ => false
        };

    private static bool RequiresMutationProof(
        OperationLifecycleState current,
        OperationLifecycleState next) =>
        current is OperationLifecycleState.Applying
            or OperationLifecycleState.Reconciling
            or OperationLifecycleState.Recovering
        && next is not OperationLifecycleState.OwnerQuiescencePending
            and not OperationLifecycleState.Reconciling
            and not OperationLifecycleState.RecoveryRequired;

    private static bool OwnerMatches(
        OperationOwnerLease? owner,
        RepositoryMutationReservation reservation) =>
        owner is not null
        && owner.FenceEpoch == reservation.FenceEpoch
        && owner.ReservationToken == reservation.FenceToken;

    private static bool FixedTimeHashEquals(string left, string right)
    {
        var leftBytes = Encoding.ASCII.GetBytes(left);
        var rightBytes = Encoding.ASCII.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
               && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Result<T, OperationLedgerError> Missing<T>() =>
        Result<T, OperationLedgerError>.Err(new OperationLedgerError(
            OperationLedgerErrorKind.Missing,
            // Stryker disable once String : Failure wording is diagnostic; callers branch on the typed error kind.
            "The requested operation is not present."));

    private static Result<T, OperationLedgerError> Corrupt<T>(
        string message,
        Exception? exception = null) => Result<T, OperationLedgerError>.Err(new OperationLedgerError(
        OperationLedgerErrorKind.Corrupt,
        message,
        exception is null
            ? null
            : new Dictionary<string, string> { ["exception"] = exception.GetType().Name }));

    private static OperationLedgerError StorageError(Exception exception) => new(
        OperationLedgerErrorKind.Unreadable,
        // Stryker disable once String : Storage wording is diagnostic; the typed kind and exception evidence are the contract.
        $"The protected operation ledger could not complete a durable filesystem operation: {exception.Message}",
        new Dictionary<string, string> { ["exception"] = exception.GetType().Name });

    private static bool IsStorageException(Exception exception) => exception is
        IOException
        or UnauthorizedAccessException
        or NotSupportedException;

    private sealed record LedgerRecord(
        int SchemaVersion,
        string RepositoryId,
        long OperationEpoch,
        Guid OperationNonce,
        string RequestHash,
        Guid PlanId,
        string PlanHash,
        string ExpectedRevision,
        string OperationEpochClaimsHash,
        string CanonicalRequestBase64,
        string SubmittingPrincipalId,
        OperationLifecycleState CurrentState,
        IReadOnlyList<TransitionRecord> Transitions,
        IReadOnlyList<EffectJournalRecord>? EffectJournal = null,
        OperationOwnerLease? OwnerLease = null,
        PreEffectActivityState? PreEffectActivity = null);

    private sealed record EffectJournalRecord(
        long Sequence,
        int EffectSequence,
        OperationEffectJournalKind Kind,
        string EvidenceHash,
        DateTimeOffset CommittedAtUtc,
        long? FenceEpoch = null,
        Guid? FenceToken = null,
        string? EffectHash = null,
        IReadOnlyList<OperationGuardToken>? GuardTokens = null);

    private sealed record DispatchRecord(
        int SchemaVersion,
        string RepositoryId,
        long OperationEpoch,
        Guid OperationNonce,
        string RequestHash,
        Guid PlanId,
        string PlanHash,
        string ExpectedRevision,
        string OperationEpochClaimsHash,
        string CanonicalRequestBase64,
        string SubmittingPrincipalId,
        string DispatchLeaseId,
        DateTimeOffset DispatchLeaseExpiresAtUtc,
        string IntakeAttemptId,
        DateTimeOffset IntakeAttemptExpiresAtUtc,
        DispatchIntakePhase CurrentPhase,
        IReadOnlyList<DispatchTransitionRecord> Transitions);

    private sealed record TransitionRecord(
        long Sequence,
        OperationLifecycleState State,
        DateTimeOffset CommittedAtUtc,
        string? Evidence);

    private sealed record DispatchTransitionRecord(
        long Sequence,
        DispatchIntakePhase Phase,
        DateTimeOffset CommittedAtUtc,
        string? Evidence);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFileEx(string existingPath, string newPath, uint flags);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string path,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);

    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int Open(IntPtr path, int flags);

    [DllImport("libc", EntryPoint = "fsync", SetLastError = true)]
    private static extern int Fsync(SafeFileHandle handle);

    internal sealed class PlatformOperationLedgerDurability : IOperationLedgerDurability
    {
        internal static PlatformOperationLedgerDurability Instance { get; } = new();

        private PlatformOperationLedgerDurability()
        {
        }

        // Stryker disable all : File durability under power loss is not observable in-process; ordering is asserted through the injected seam.
        void IOperationLedgerDurability.FlushFileMetadata(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            stream.Flush(flushToDisk: true);
        }
        // Stryker restore all

        void IOperationLedgerDurability.ReplaceFile(string temporaryPath, string recordPath)
        {
            // Stryker disable all : Native Windows replacement is exercised by Windows jobs; the durability seam asserts the cross-platform contract.
            if (OperatingSystem.IsWindows())
            {
                var moved = MoveFileEx(
                    temporaryPath,
                    recordPath,
                    MoveFileReplaceExisting | MoveFileWriteThrough);
                var error = Marshal.GetLastPInvokeError();
                if (!moved)
                    throw NativeIOException("durably replace the operation-ledger record", recordPath, error);
                return;
            }
            // Stryker restore all

            File.Move(temporaryPath, recordPath, overwrite: true);
        }

        void IOperationLedgerDurability.FlushDirectoryMetadata(string directory)
        {
            // Stryker disable all : Native Windows directory flushing is exercised by Windows jobs; the durability seam asserts the cross-platform contract.
            if (OperatingSystem.IsWindows())
            {
                using var windowsHandle = CreateFile(
                    directory,
                    GenericWrite,
                    FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero,
                    OpenExisting,
                    FileFlagBackupSemantics,
                    IntPtr.Zero);
                var windowsOpenError = Marshal.GetLastPInvokeError();
                if (windowsHandle.IsInvalid)
                    throw NativeIOException("open the operation-ledger directory", directory, windowsOpenError);
                var flushed = FlushFileBuffers(windowsHandle);
                var windowsFlushError = Marshal.GetLastPInvokeError();
                if (!flushed)
                    throw NativeIOException(
                        "durably flush the operation-ledger directory",
                        directory,
                        windowsFlushError);
                return;
            }
            // Stryker restore all

            var nativePath = Marshal.StringToCoTaskMemUTF8(directory);
            int descriptor;
            int openError;
            try
            {
                descriptor = Open(nativePath, 0);
                openError = Marshal.GetLastPInvokeError();
            }
            finally
            {
                // Stryker disable once Statement : Native allocator release is not observable without process-level leak instrumentation.
                Marshal.FreeCoTaskMem(nativePath);
            }
            if (descriptor < 0)
                // Stryker disable once String : Native error wording is diagnostic; failure mapping is asserted through injected faults.
                throw NativeIOException("open the operation-ledger directory", directory, openError);
            // Stryker disable once Boolean : Native descriptor ownership is not observable without process-level leak instrumentation.
            using var handle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
            var result = Fsync(handle);
            var flushError = Marshal.GetLastPInvokeError();
            if (result != 0)
                // Stryker disable all : Native fsync failure projection is covered through injected durability faults and native filesystem jobs.
                throw NativeIOException("durably flush the operation-ledger directory", directory, flushError);
            // Stryker restore all
        }
    }

    private static IOException NativeIOException(string operation, string path, int error) => new(
        // Stryker disable once String : Native error wording is diagnostic; failure mapping is asserted through the durability seam.
        $"Could not {operation} '{path}': {new Win32Exception(error).Message}",
        new Win32Exception(error));
}
