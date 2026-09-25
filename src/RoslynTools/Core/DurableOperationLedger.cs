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

internal sealed record DurableOperationRecord(
    string RepositoryId,
    OperationId OperationId,
    OperationRequestIdentity RequestIdentity,
    ImmutableArray<byte> CanonicalRequestBytes,
    string SubmittingPrincipalId,
    OperationLifecycleState CurrentState,
    ImmutableArray<OperationTransition> Transitions);

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
///     Trusted host boundary that holds the operation ledger's physical location and
///     access policy stable for the duration of a read or durable transition.
/// </summary>
internal interface IOperationLedgerProtectionAuthority
{
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
    OperationLedgerError? IOperationLedgerProtectionAuthority.Acquire(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease)
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
            var protectionError = AcquireProtection(out var protectionLease);
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
            var protectionError = AcquireProtection(out var protectionLease);
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

            var committedAtUtc = _utcNow().ToUniversalTime();
            var previousCommittedAtUtc = operation.Transitions[^1].CommittedAtUtc;
            // Stryker disable once Equality : Equal operands select the same timestamp through either branch.
            committedAtUtc = committedAtUtc < previousCommittedAtUtc
                ? previousCommittedAtUtc
                : committedAtUtc;
            var transitions = operation.Transitions.Add(new OperationTransition(
                operation.Transitions[^1].Sequence + 1,
                nextState,
                committedAtUtc,
                evidence));
            var updated = operation with
            {
                CurrentState = nextState,
                Transitions = transitions
            };
            WriteRecord(recordPath, updated);
            return Result<DurableOperationRecord, OperationLedgerError>.Ok(updated);
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
    {
        protectionLease = null;
        IOperationLedgerProtectionLease? acquiredLease = null;
        try
        {
            var error = _protectionAuthority.Acquire(_rootPath, out acquiredLease);
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

            return Result<DurableOperationRecord, OperationLedgerError>.Ok(new DurableOperationRecord(
                stored.RepositoryId,
                operationId,
                identity,
                ImmutableArray.CreateRange(canonicalBytes),
                OperationRequestText.Required(stored.SubmittingPrincipalId, nameof(stored.SubmittingPrincipalId)),
                stored.CurrentState,
                transitions));
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
                transition.Evidence)).ToArray());
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
        IReadOnlyList<TransitionRecord> Transitions);

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

    private sealed class PlatformOperationLedgerDurability : IOperationLedgerDurability
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
