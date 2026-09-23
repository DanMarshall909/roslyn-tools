using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RoslynTools.Core;

internal enum ChangePlanStoreErrorKind
{
    Missing,
    Expired,
    Unreadable,
    SchemaIncompatible,
    HashMismatch,
    CapacityExceeded,
    Conflict,
    UnsafeProtection
}

internal sealed record ChangePlanStoreError(
    ChangePlanStoreErrorKind Kind,
    string Message,
    IReadOnlyDictionary<string, string>? Evidence = null);

internal sealed record ChangePlanStoreOptions(
    int MaximumPlanCount,
    long MaximumPlanBytes,
    long MaximumStoreBytes,
    int MaximumPinsPerPlan,
    int MaximumPinReferenceBytes,
    TimeSpan MaximumDispatchLeaseLifetime,
    TimeSpan MaximumPlanLifetime,
    TimeSpan UnreferencedRetention)
{
    internal static ChangePlanStoreOptions Default { get; } = new(
        MaximumPlanCount: 256,
        MaximumPlanBytes: 16 * 1024 * 1024,
        MaximumStoreBytes: 256 * 1024 * 1024,
        MaximumPinsPerPlan: 128,
        MaximumPinReferenceBytes: 256,
        MaximumDispatchLeaseLifetime: TimeSpan.FromMinutes(15),
        MaximumPlanLifetime: TimeSpan.FromHours(24),
        UnreferencedRetention: TimeSpan.FromHours(24));

    internal void Validate()
    {
        if (MaximumPlanCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumPlanCount));
        if (MaximumPlanBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumPlanBytes));
        if (MaximumStoreBytes < MaximumPlanBytes)
            throw new ArgumentOutOfRangeException(
                nameof(MaximumStoreBytes),
                "Store capacity must admit at least one maximum-sized plan.");
        if (MaximumPinsPerPlan <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumPinsPerPlan));
        if (MaximumPinReferenceBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(MaximumPinReferenceBytes));
        if (MaximumDispatchLeaseLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaximumDispatchLeaseLifetime));
        if (MaximumPlanLifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(MaximumPlanLifetime));
        if (UnreferencedRetention < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(UnreferencedRetention));
    }
}

internal enum ChangePlanPinKind
{
    DispatchLease,
    OperationLedger
}

internal sealed record ChangePlanStoreKey(
    string RepositoryId,
    Guid PlanId,
    string PlanHash);

internal sealed record StoredChangePlan(
    ChangePlanStoreKey Key,
    ChangePlanEnvelope Envelope,
    ImmutableArray<byte> EnvelopeBytes,
    DateTimeOffset PersistedAtUtc,
    DateTimeOffset RetainUntilUtc,
    ImmutableArray<ChangePlanStorePin> Pins);

internal sealed record ChangePlanStorePin(
    ChangePlanPinKind Kind,
    string ReferenceId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    string? AdoptedFromDispatchLeaseId);

/// <summary>
///     Trusted host boundary that proves a store path is owned by the authenticated
///     principal and is physically outside the repository, including mount aliases.
///     The returned lease must hold that physical binding stable until disposal.
///     Repository-controlled data must never select or implement this authority.
/// </summary>
internal interface IChangePlanStoreProtectionAuthority
{
    ChangePlanStoreError? AcquireForPersistence(
        string storeRootPath,
        ChangePlanRepository repository,
        out IChangePlanStoreProtectionLease? lease);

    ChangePlanStoreError? AcquireExisting(
        string storeRootPath,
        out IChangePlanStoreProtectionLease? lease);
}

internal interface IChangePlanStoreProtectionLease : IDisposable
{
}

internal interface IChangePlanStoreDurability
{
    void FlushFileMetadata(string path);

    void ReplaceFile(string temporaryPath, string recordPath);

    void FlushDirectoryMetadata(string directory);
}

internal sealed class UnavailableChangePlanStoreProtectionAuthority : IChangePlanStoreProtectionAuthority
{
    internal static UnavailableChangePlanStoreProtectionAuthority Instance { get; } = new();

    private UnavailableChangePlanStoreProtectionAuthority()
    {
    }

    ChangePlanStoreError IChangePlanStoreProtectionAuthority.AcquireForPersistence(
        string storeRootPath,
        ChangePlanRepository repository,
        out IChangePlanStoreProtectionLease? lease)
    {
        lease = null;
        return Unavailable();
    }

    ChangePlanStoreError IChangePlanStoreProtectionAuthority.AcquireExisting(
        string storeRootPath,
        out IChangePlanStoreProtectionLease? lease)
    {
        lease = null;
        return Unavailable();
    }

    private static ChangePlanStoreError Unavailable() => new(
        ChangePlanStoreErrorKind.UnsafeProtection,
        "No trusted authority proved the protected plan store's physical identity and ownership.");
}

internal sealed record ChangePlanGarbageCollectionResult(
    int DeletedPlans,
    int ReleasedPins,
    int RetainedPlans,
    int FailedPlans);

/// <summary>
///     Principal-private, process-independent storage for exact canonical change-plan
///     envelopes in an <see cref="ChangePlanCoordinationDomain.ExclusivePrincipal" />
///     domain. Each update replaces one complete record under a cross-process store lock.
///     Record paths contain only digests and canonical IDs, never repository-controlled
///     text. Every operation requires a trusted physical-protection lease; no proof is
///     available by default. Shared-authority plans require an authority-owned provider.
/// </summary>
internal sealed class DurableChangePlanStore
{
    private const int StoreSchemaVersion = 1;
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
    private readonly string _plansPath;
    private readonly string _lockPath;
    private readonly ChangePlanStoreOptions _options;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly IChangePlanStoreProtectionAuthority _protectionAuthority;
    private readonly IChangePlanStoreDurability _durability;

    internal DurableChangePlanStore(
        string rootPath,
        ChangePlanStoreOptions? options = null,
        Func<DateTimeOffset>? utcNow = null,
        IChangePlanStoreProtectionAuthority? protectionAuthority = null,
        IChangePlanStoreDurability? durability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        _rootPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        _plansPath = Path.Combine(_rootPath, "plans");
        _lockPath = Path.Combine(_rootPath, ".store.lock");
        _options = options ?? ChangePlanStoreOptions.Default;
        _options.Validate();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _protectionAuthority = protectionAuthority
                               ?? UnavailableChangePlanStoreProtectionAuthority.Instance;
        _durability = durability ?? PlatformChangePlanStoreDurability.Instance;
    }

    internal Result<ChangePlanStoreKey, ChangePlanStoreError> Persist(ChangePlanEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (!envelope.Verify())
            return Error<ChangePlanStoreKey>(ChangePlanStoreErrorKind.HashMismatch, "The plan envelope failed canonical verification.");

        var payload = envelope.Payload;
        var observedAtEntry = _utcNow().ToUniversalTime();
        if (payload.CreatedAtUtc > observedAtEntry)
            return Error<ChangePlanStoreKey>(ChangePlanStoreErrorKind.Conflict, "The plan creation time is in the future.");
        if (payload.IsExpired(observedAtEntry))
            return Error<ChangePlanStoreKey>(ChangePlanStoreErrorKind.Expired, "The plan expired before it could be persisted.");
        if (payload.ExpiresAtUtc - payload.CreatedAtUtc > _options.MaximumPlanLifetime)
        {
            return Error<ChangePlanStoreKey>(
                ChangePlanStoreErrorKind.CapacityExceeded,
                "The plan lifetime exceeds the protected store limit.");
        }
        if (payload.Coordination.Domain != ChangePlanCoordinationDomain.ExclusivePrincipal)
        {
            return Error<ChangePlanStoreKey>(
                ChangePlanStoreErrorKind.UnsafeProtection,
                "A shared-authority plan requires an authority-owned plan-store provider.");
        }

        var envelopeBytes = envelope.SerializeEnvelope();
        if (envelopeBytes.LongLength > _options.MaximumPlanBytes)
        {
            return Error<ChangePlanStoreKey>(
                ChangePlanStoreErrorKind.CapacityExceeded,
                "The canonical plan envelope exceeds the per-plan byte limit.");
        }

        var key = new ChangePlanStoreKey(payload.Repository.RepositoryId, payload.PlanId, envelope.PlanHash);
        try
        {
            var protection = AcquirePersistenceProtection(payload.Repository, out var protectionLease);
            if (protection is not null)
                return Result<ChangePlanStoreKey, ChangePlanStoreError>.Err(protection);
            using var heldProtectionLease = protectionLease!;
            var rootError = EnsureProtectedRoot();
            if (rootError is not null)
                return Result<ChangePlanStoreKey, ChangePlanStoreError>.Err(rootError);

            using var storeLock = AcquireMutationLock();
            var now = _utcNow().ToUniversalTime();
            if (payload.CreatedAtUtc > now)
            {
                return Error<ChangePlanStoreKey>(
                    ChangePlanStoreErrorKind.Conflict,
                    "The plan creation time is in the future at the persistence boundary.");
            }
            if (payload.IsExpired(now))
            {
                return Error<ChangePlanStoreKey>(
                    ChangePlanStoreErrorKind.Expired,
                    "The plan expired before the persistence boundary.");
            }
            CleanupTombstones();
            var recordPath = GetRecordPath(key);
            if (File.Exists(recordPath))
            {
                // Stryker disable once Boolean : Persist already proved the same immutable envelope is unexpired.
                var existing = ReadRecord(recordPath, key, now, rejectExpired: false);
                return existing.Match(
                    // Stryker disable all : A different verified envelope under the same complete key requires a SHA-256 collision; keep the defensive byte check and diagnostic.
                    plan => plan.EnvelopeBytes.AsSpan().SequenceEqual(envelopeBytes)
                        ? Result<ChangePlanStoreKey, ChangePlanStoreError>.Ok(key)
                        : Error<ChangePlanStoreKey>(
                            ChangePlanStoreErrorKind.Conflict,
                            "The plan key is already bound to different persisted bytes."),
                    // Stryker restore all
                    error => Result<ChangePlanStoreKey, ChangePlanStoreError>.Err(error));
            }

            var retainUntil = Max(payload.ExpiresAtUtc, now + _options.UnreferencedRetention);
            var record = new StoreRecord(
                StoreSchemaVersion,
                key.RepositoryId,
                key.PlanId.ToString(),
                key.PlanHash,
                now,
                retainUntil,
                envelopeBytes,
                []);
            var recordBytes = SerializeRecord(record);
            var inventory = Inventory();
            if (inventory.Count >= _options.MaximumPlanCount
                || inventory.Bytes + recordBytes.LongLength > _options.MaximumStoreBytes)
            {
                return Error<ChangePlanStoreKey>(
                    ChangePlanStoreErrorKind.CapacityExceeded,
                    "The protected plan store has no remaining reserved capacity.");
            }
            WriteRecord(recordPath, record);
            return Result<ChangePlanStoreKey, ChangePlanStoreError>.Ok(key);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<ChangePlanStoreKey, ChangePlanStoreError>.Err(StorageError(exception));
        }
    }

    internal Result<StoredChangePlan, ChangePlanStoreError> Load(ChangePlanStoreKey key)
    {
        var keyError = ValidateKey(key);
        if (keyError is not null)
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(keyError);
        try
        {
            var rootError = ValidateExistingProtectedRoot(out var protectionLease);
            if (rootError is not null)
                return Result<StoredChangePlan, ChangePlanStoreError>.Err(rootError);
            using var heldProtectionLease = protectionLease!;
            var path = GetRecordPath(key);
            if (!File.Exists(path))
                return Error<StoredChangePlan>(ChangePlanStoreErrorKind.Missing, "The requested change plan is not present.");
            var fileProtectionError = ValidatePrivateFile(path);
            if (fileProtectionError is not null)
                return Result<StoredChangePlan, ChangePlanStoreError>.Err(fileProtectionError);
            return ReadRecord(path, key, _utcNow().ToUniversalTime(), rejectExpired: true);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(StorageError(exception));
        }
    }

    internal Result<StoredChangePlan, ChangePlanStoreError> PinDispatchLease(
        ChangePlanStoreKey key,
        string dispatchLeaseId,
        DateTimeOffset expiresAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchLeaseId);
        ValidatePinReference(dispatchLeaseId, nameof(dispatchLeaseId));
        var expiry = expiresAtUtc.ToUniversalTime();
        var observedAtEntry = _utcNow().ToUniversalTime();
        // Stryker disable all : These entry checks avoid needless filesystem work; the same boundaries are authoritatively rechecked under the mutation lock below.
        if (expiry <= observedAtEntry)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "A dispatch-plan lease must expire in the future.");
        if (expiry - observedAtEntry > _options.MaximumDispatchLeaseLifetime)
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "The dispatch-plan lease exceeds its lifetime limit.");
        // Stryker restore all
        return UpdateRecord(key, rejectExpired: true, (record, now) =>
        {
            if (expiry <= now)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expiresAtUtc),
                    "The dispatch-plan lease must expire in the future at the persistence boundary.");
            }
            if (expiry - now > _options.MaximumDispatchLeaseLifetime)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(expiresAtUtc),
                    "The dispatch-plan lease exceeds its lifetime limit.");
            }
            var pins = record.Pins.ToList();
            var existing = pins.SingleOrDefault(pin =>
                pin.Kind == ChangePlanPinKind.DispatchLease
                && string.Equals(pin.ReferenceId, dispatchLeaseId, StringComparison.Ordinal));
            if (existing is null)
                pins.Add(new PinRecord(ChangePlanPinKind.DispatchLease, dispatchLeaseId, now, expiry, null));
            else if (existing.ExpiresAtUtc != expiry)
                throw new ChangePlanStoreConflictException("The dispatch-plan lease already has a different expiry.");
            return record with { Pins = SortPins(pins) };
        });
    }

    internal Result<StoredChangePlan, ChangePlanStoreError> AdoptDispatchLease(
        ChangePlanStoreKey key,
        string dispatchLeaseId,
        string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchLeaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ValidatePinReference(dispatchLeaseId, nameof(dispatchLeaseId));
        ValidatePinReference(operationId, nameof(operationId));
        return UpdateRecord(key, rejectExpired: false, (record, now) =>
        {
            var pins = record.Pins.ToList();
            var dispatch = pins.SingleOrDefault(pin =>
                pin.Kind == ChangePlanPinKind.DispatchLease
                && string.Equals(pin.ReferenceId, dispatchLeaseId, StringComparison.Ordinal));
            var operation = pins.SingleOrDefault(pin =>
                pin.Kind == ChangePlanPinKind.OperationLedger
                && string.Equals(pin.ReferenceId, operationId, StringComparison.Ordinal));
            if (dispatch is null && operation is null)
                throw new ChangePlanStoreConflictException("The dispatch-plan lease is not available for adoption.");
            if (operation is not null
                && !string.Equals(
                    operation.AdoptedFromDispatchLeaseId,
                    dispatchLeaseId,
                    StringComparison.Ordinal))
            {
                throw new ChangePlanStoreConflictException(
                    "The operation pin was adopted from a different dispatch-plan lease.");
            }
            if (dispatch is not null && operation is not null)
                throw new ChangePlanStoreConflictException("The adopted dispatch-plan lease was registered again.");
            if (dispatch is not null && dispatch.ExpiresAtUtc <= now)
                throw new ChangePlanStoreConflictException("The dispatch-plan lease expired before adoption.");
            if (dispatch is not null)
                pins.Remove(dispatch);
            if (operation is null)
            {
                pins.Add(new PinRecord(
                    ChangePlanPinKind.OperationLedger,
                    operationId,
                    now,
                    null,
                    dispatchLeaseId));
            }
            return record with { Pins = SortPins(pins) };
        });
    }

    internal Result<StoredChangePlan, ChangePlanStoreError> ReleasePin(
        ChangePlanStoreKey key,
        ChangePlanPinKind kind,
        string referenceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceId);
        ValidatePinReference(referenceId, nameof(referenceId));
        return UpdateRecord(key, rejectExpired: false, (record, _) => record with
        {
            Pins = record.Pins
                .Where(pin => pin.Kind != kind
                              || !string.Equals(pin.ReferenceId, referenceId, StringComparison.Ordinal))
                .ToArray()
        });
    }

    internal ChangePlanGarbageCollectionResult CollectGarbage()
    {
        try
        {
            var rootError = ValidateExistingProtectedRoot(out var protectionLease);
            if (rootError is not null)
                return new ChangePlanGarbageCollectionResult(0, 0, 0, 1);
            using var heldProtectionLease = protectionLease!;
            using var storeLock = AcquireMutationLock();
            CleanupTombstones();
            var now = _utcNow().ToUniversalTime();
            var deleted = 0;
            var released = 0;
            var retained = 0;
            var failed = 0;
            foreach (var recordPath in EnumerateRecordPaths())
            {
                StoreRecord record;
                ChangePlanStoreKey key;
                try
                {
                    record = DeserializeRecord(File.ReadAllBytes(recordPath));
                    key = KeyFromRecord(record);
                    var validated = ReadRecord(recordPath, key, now, rejectExpired: false);
                    if (validated.IsErr)
                    {
                        failed++;
                        continue;
                    }
                }
                catch (Exception exception) when (IsRecordException(exception))
                {
                    failed++;
                    continue;
                }

                var livePins = record.Pins
                    .Where(pin => pin.Kind != ChangePlanPinKind.DispatchLease
                                  || pin.ExpiresAtUtc > now)
                    .ToArray();
                released += record.Pins.Length - livePins.Length;
                if (livePins.Length == 0 && record.RetainUntilUtc <= now)
                {
                    TombstoneAndDelete(recordPath);
                    deleted++;
                    continue;
                }

                if (livePins.Length != record.Pins.Length)
                    WriteRecord(recordPath, record with { Pins = livePins });
                retained++;
            }
            return new ChangePlanGarbageCollectionResult(deleted, released, retained, failed);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return new ChangePlanGarbageCollectionResult(0, 0, 0, 1);
        }
    }

    private Result<StoredChangePlan, ChangePlanStoreError> UpdateRecord(
        ChangePlanStoreKey key,
        bool rejectExpired,
        Func<StoreRecord, DateTimeOffset, StoreRecord> update)
    {
        var keyError = ValidateKey(key);
        if (keyError is not null)
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(keyError);
        try
        {
            var rootError = ValidateExistingProtectedRoot(out var protectionLease);
            if (rootError is not null)
                return Result<StoredChangePlan, ChangePlanStoreError>.Err(rootError);
            using var heldProtectionLease = protectionLease!;
            using var storeLock = AcquireMutationLock();
            var now = _utcNow().ToUniversalTime();
            var recordPath = GetRecordPath(key);
            if (!File.Exists(recordPath))
                return Error<StoredChangePlan>(ChangePlanStoreErrorKind.Missing, "The requested change plan is not present.");
            var fileProtectionError = ValidatePrivateFile(recordPath);
            if (fileProtectionError is not null)
                return Result<StoredChangePlan, ChangePlanStoreError>.Err(fileProtectionError);
            var loaded = ReadRecord(recordPath, key, now, rejectExpired);
            if (loaded.IsErr)
                return loaded;

            StoreRecord updated;
            try
            {
                updated = update(DeserializeRecord(File.ReadAllBytes(recordPath)), now);
            }
            catch (ChangePlanStoreConflictException exception)
            {
                return Error<StoredChangePlan>(ChangePlanStoreErrorKind.Conflict, exception.Message);
            }
            if (updated.Pins.Length > _options.MaximumPinsPerPlan)
            {
                return Error<StoredChangePlan>(
                    ChangePlanStoreErrorKind.CapacityExceeded,
                    "The protected plan has no remaining pin capacity.");
            }
            var validationError = ValidateRecord(updated);
            if (validationError is not null)
                return Result<StoredChangePlan, ChangePlanStoreError>.Err(validationError);
            var currentLength = new FileInfo(recordPath).Length;
            var updatedLength = SerializeRecord(updated).LongLength;
            var inventory = Inventory();
            if (inventory.Bytes - currentLength + updatedLength > _options.MaximumStoreBytes)
            {
                return Error<StoredChangePlan>(
                    ChangePlanStoreErrorKind.CapacityExceeded,
                    "The protected plan store has no remaining capacity for this pin.");
            }
            WriteRecord(recordPath, updated);
            return ReadRecord(recordPath, key, now, rejectExpired: false);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(StorageError(exception));
        }
    }

    private Result<StoredChangePlan, ChangePlanStoreError> ReadRecord(
        string recordPath,
        ChangePlanStoreKey expectedKey,
        DateTimeOffset now,
        bool rejectExpired)
    {
        var fileProtectionError = ValidatePrivateFile(recordPath);
        if (fileProtectionError is not null)
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(fileProtectionError);

        StoreRecord record;
        try
        {
            record = DeserializeRecord(File.ReadAllBytes(recordPath));
        }
        catch (Exception exception) when (IsRecordException(exception))
        {
            return Error<StoredChangePlan>(
                ChangePlanStoreErrorKind.Unreadable,
                $"The persisted change-plan record is unreadable: {exception.Message}");
        }

        var recordError = ValidateRecord(record);
        if (recordError is not null)
            return Result<StoredChangePlan, ChangePlanStoreError>.Err(recordError);

        if (record.SchemaVersion != StoreSchemaVersion)
        {
            return Error<StoredChangePlan>(
                ChangePlanStoreErrorKind.SchemaIncompatible,
                $"Unsupported plan-store schema '{record.SchemaVersion}'.");
        }

        ChangePlanStoreKey actualKey;
        try
        {
            actualKey = KeyFromRecord(record);
        }
        catch (Exception exception) when (IsRecordException(exception))
        {
            return Error<StoredChangePlan>(ChangePlanStoreErrorKind.Unreadable, exception.Message);
        }
        if (actualKey != expectedKey)
        {
            return Error<StoredChangePlan>(
                ChangePlanStoreErrorKind.HashMismatch,
                "The persisted record identity does not match its requested plan key.");
        }

        var parsed = CanonicalChangePlanReader.ReadEnvelope(record.EnvelopeBytes);
        if (parsed.IsErr)
        {
            return parsed.Match(
                _ => throw new UnreachableException(),
                error => Error<StoredChangePlan>(
                    error.Kind switch
                    {
                        ChangePlanEnvelopeReadErrorKind.SchemaIncompatible => ChangePlanStoreErrorKind.SchemaIncompatible,
                        ChangePlanEnvelopeReadErrorKind.IntegrityFailure => ChangePlanStoreErrorKind.HashMismatch,
                        _ => ChangePlanStoreErrorKind.Unreadable
                    },
                    error.Message));
        }

        return parsed.Match(
            envelope =>
            {
                // Stryker disable all : The outer key and verified canonical hash already bind these payload identity fields; this remains a collision-defense assertion.
                if (envelope.Payload.Repository.RepositoryId != expectedKey.RepositoryId
                    || envelope.Payload.PlanId != expectedKey.PlanId
                    || !string.Equals(envelope.PlanHash, expectedKey.PlanHash, StringComparison.Ordinal))
                {
                    return Error<StoredChangePlan>(
                        ChangePlanStoreErrorKind.HashMismatch,
                        "The persisted envelope identity does not match its protected store key.");
                }
                // Stryker restore all
                if (envelope.Payload.Coordination.Domain != ChangePlanCoordinationDomain.ExclusivePrincipal)
                {
                    return Error<StoredChangePlan>(
                        ChangePlanStoreErrorKind.UnsafeProtection,
                        // Stryker disable once String : The typed boundary is observable; message wording is diagnostic only.
                        "A shared-authority plan cannot be loaded from a user-private plan store.");
                }
                if (rejectExpired && envelope.Payload.IsExpired(now))
                    return Error<StoredChangePlan>(ChangePlanStoreErrorKind.Expired, "The requested change plan has expired.");
                if (record.RetainUntilUtc < envelope.Payload.ExpiresAtUtc)
                {
                    return Error<StoredChangePlan>(
                        ChangePlanStoreErrorKind.Unreadable,
                        "The persisted plan retention boundary precedes plan expiry.");
                }
                return Result<StoredChangePlan, ChangePlanStoreError>.Ok(new StoredChangePlan(
                    expectedKey,
                    envelope,
                    ImmutableArray.CreateRange(record.EnvelopeBytes),
                    record.PersistedAtUtc,
                    record.RetainUntilUtc,
                    record.Pins.Select(ToPublicPin).ToImmutableArray()));
            },
            _ => throw new UnreachableException());
    }

    private ChangePlanStoreError? AcquirePersistenceProtection(
        ChangePlanRepository repository,
        out IChangePlanStoreProtectionLease? protectionLease)
    {
        protectionLease = null;
        // A validated plan requires RepositoryRoot and ResolvedRepositoryRoot to be
        // identical under its path policy, so one containment check covers both names.
        if (PathsOverlap(_rootPath, repository.RepositoryRoot))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                "The protected plan store must be outside and non-overlapping with the target repository.");
        }

        IChangePlanStoreProtectionLease? acquiredLease = null;
        try
        {
            var authorityError = _protectionAuthority.AcquireForPersistence(
                _rootPath,
                repository,
                out acquiredLease);
            if (authorityError is not null)
            {
                acquiredLease?.Dispose();
                return authorityError;
            }
            if (acquiredLease is null)
            {
                return new ChangePlanStoreError(
                    ChangePlanStoreErrorKind.UnsafeProtection,
                    "The trusted protection authority did not provide a physical store lease.");
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

    private ChangePlanStoreError? EnsureProtectedRoot()
    {
        if (Directory.Exists(_rootPath) && HasReparsePointInExistingPath(_rootPath))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                // Stryker disable once String : The typed protection failure is observable; message wording is diagnostic only.
                "The protected plan-store path contains a symbolic-link or reparse boundary.");
        }
        CreatePrivateDirectoryTree(_rootPath);
        if (HasReparsePointInExistingPath(_rootPath))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                // Stryker disable once String : This race-specific diagnostic does not alter the fail-closed result.
                "The protected plan-store path contains a symbolic-link or reparse boundary.");
        }
        ApplyDirectoryPermissions(_rootPath);
        _durability.FlushDirectoryMetadata(_rootPath);
        var rootError = ValidatePrivateDirectory(_rootPath);
        if (rootError is not null)
            return rootError;
        CreatePrivateDirectoryTree(_plansPath);
        var plansError = ValidatePrivateDirectory(_plansPath);
        if (plansError is not null)
            return plansError;
        return null;
    }

    private ChangePlanStoreError? ValidateExistingProtectedRoot(
        out IChangePlanStoreProtectionLease? protectionLease)
    {
        protectionLease = null;
        if (!Directory.Exists(_rootPath))
            return new ChangePlanStoreError(ChangePlanStoreErrorKind.Missing, "The protected plan store does not exist.");
        if (HasReparsePointInExistingPath(_rootPath))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                // Stryker disable once String : The typed protection failure is observable; message wording is diagnostic only.
                "The protected plan-store path contains a symbolic-link or reparse boundary.");
        }
        var directoryError = ValidatePrivateDirectory(_rootPath);
        return directoryError ?? _protectionAuthority.AcquireExisting(_rootPath, out protectionLease);
    }

    private static ChangePlanStoreError? ValidatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            return ValidatePrivateDirectoryOnWindows(path);
        try
        {
            var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            return File.GetUnixFileMode(path) == expected
                ? null
                : new ChangePlanStoreError(
                    ChangePlanStoreErrorKind.UnsafeProtection,
                    $"Protected plan-store directory '{path}' is not private to its owning principal.");
        }
        catch (PlatformNotSupportedException)
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                // Stryker disable once String : The platform branch is exercised by native jobs; wording is diagnostic only.
                "This platform cannot prove private plan-store directory permissions.");
        }
    }

    private static ChangePlanStoreError? ValidatePrivateFile(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                // Stryker disable once String : The typed protection failure is observable; message wording is diagnostic only.
                $"Protected plan-store file '{path}' is a symbolic link or reparse point.");
        }
        if (OperatingSystem.IsWindows())
            return ValidatePrivateFileOnWindows(path);
        try
        {
            var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            return File.GetUnixFileMode(path) == expected
                ? null
                : new ChangePlanStoreError(
                    ChangePlanStoreErrorKind.UnsafeProtection,
                    $"Protected plan-store file '{path}' is not private to its owning principal.");
        }
        catch (PlatformNotSupportedException)
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                // Stryker disable once String : The platform branch is exercised by native jobs; wording is diagnostic only.
                "This platform cannot prove private plan-store file permissions.");
        }
    }

    private FileStream AcquireMutationLock()
    {
        var stopwatch = Stopwatch.StartNew();
        // Stryker disable all : Timeout equality and retry sleep alter contention pacing only; FileShare.None remains the admission boundary.
        while (true)
        {
            try
            {
                var stream = new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                ApplyFilePermissions(_lockPath);
                return stream;
            }
            catch (IOException) when (stopwatch.Elapsed < LockTimeout)
            {
                Thread.Sleep(25);
            }
        }
        // Stryker restore all
    }

    private void WriteRecord(string recordPath, StoreRecord record)
    {
        var directory = Path.GetDirectoryName(recordPath)!;
        CreatePrivateDirectoryTree(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(recordPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var bytes = SerializeRecord(record);
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 4096,
                       FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                // Stryker disable all : Power-loss durability is not observable in an in-process test.
                stream.Flush(flushToDisk: true);
                // Stryker restore all
            }
            ApplyFilePermissions(temporaryPath);
            _durability.FlushFileMetadata(temporaryPath);
            _durability.ReplaceFile(temporaryPath, recordPath);
            _durability.FlushDirectoryMetadata(directory);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private void CreatePrivateDirectoryTree(string directory)
    {
        var missing = new Stack<string>();
        // Stryker disable once Logical : Inverting this loop's termination predicate creates a nonterminating mutant.
        for (var current = directory;
             !string.IsNullOrEmpty(current) && !Directory.Exists(current);
             current = Path.GetDirectoryName(current))
            missing.Push(current);
        while (missing.TryPop(out var path))
        {
            Directory.CreateDirectory(path);
            ApplyDirectoryPermissions(path);
            _durability.FlushDirectoryMetadata(path);
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                _durability.FlushDirectoryMetadata(parent);
        }
    }

    private static void FlushFileMetadataOnPlatform(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 1,
            FileOptions.WriteThrough);
        // Stryker disable once Statement : A power-loss flush cannot be distinguished by an in-process test; ordering is asserted through the injected durability seam.
        stream.Flush(flushToDisk: true);
    }

    private static void ReplaceFileOnPlatform(string temporaryPath, string recordPath)
    {
        // Stryker disable all : Native Windows replacement is exercised by the Windows filesystem job; Linux tests assert the same contract through the durability seam.
        if (OperatingSystem.IsWindows())
        {
            var moved = MoveFileEx(
                temporaryPath,
                recordPath,
                MoveFileReplaceExisting | MoveFileWriteThrough);
            var moveError = Marshal.GetLastPInvokeError();
            if (!moved)
            {
                throw NativeIOException(
                    "durably replace the protected plan record",
                    recordPath,
                    moveError);
            }
            return;
        }
        // Stryker restore all

        File.Move(temporaryPath, recordPath, overwrite: true);
    }

    private static void FlushDirectoryMetadataOnPlatform(string directory)
    {
        // Stryker disable all : Native Windows directory flushing is exercised by the Windows filesystem job; Linux tests assert the same contract through the durability seam.
        if (OperatingSystem.IsWindows())
        {
            using var handle = CreateFile(
                directory,
                GenericWrite,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            var openError = Marshal.GetLastPInvokeError();
            if (handle.IsInvalid)
            {
                throw NativeIOException(
                    "open the protected plan-store directory for a durable flush",
                    directory,
                    openError);
            }
            var flushed = FlushFileBuffers(handle);
            var flushError = Marshal.GetLastPInvokeError();
            if (!flushed)
                throw NativeIOException("flush the protected plan-store directory", directory, flushError);
            return;
        }
        // Stryker restore all

        var nativePath = Marshal.StringToCoTaskMemUTF8(directory);
        int descriptor;
        int unixOpenError;
        try
        {
            descriptor = Open(nativePath, 0);
            unixOpenError = Marshal.GetLastPInvokeError();
        }
        finally
        {
            // Stryker disable once Statement : Native allocator release is not observable without process-level leak instrumentation.
            Marshal.FreeCoTaskMem(nativePath);
        }
        if (descriptor < 0)
        {
            throw NativeIOException(
                // Stryker disable once String : Native error wording is diagnostic; failure mapping is asserted through the durability seam.
                "open the protected plan-store directory for a durable flush",
                directory,
                unixOpenError);
        }
        // Stryker disable once Boolean : Native descriptor ownership is not observable without process-level leak instrumentation.
        using var directoryHandle = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
        var result = Fsync(directoryHandle);
        var fsyncError = Marshal.GetLastPInvokeError();
        if (result != 0)
            // Stryker disable all : Native fsync failure projection is covered through the injected durability-fault seam and native filesystem jobs.
            throw NativeIOException("flush the protected plan-store directory", directory, fsyncError);
        // Stryker restore all
    }

    private string GetRecordPath(ChangePlanStoreKey key)
    {
        var repositoryDigest = LowerSha256(Encoding.UTF8.GetBytes(key.RepositoryId));
        return Path.Combine(
            _plansPath,
            repositoryDigest[..2],
            repositoryDigest,
            key.PlanId.ToString("N"),
            $"{key.PlanHash}.plan");
    }

    private StoreInventory Inventory()
    {
        var records = EnumerateRecordPaths().ToArray();
        return new StoreInventory(records.Length, records.Sum(path => new FileInfo(path).Length));
    }

    private IEnumerable<string> EnumerateRecordPaths() =>
        Directory.EnumerateFiles(_plansPath, "*.plan", SearchOption.AllDirectories);

    private void CleanupTombstones()
    {
        // Stryker disable once Statement : Continuing with a missing plans directory produces the same caught conservative GC failure.
        if (!Directory.Exists(_plansPath))
            return;
        foreach (var tombstone in Directory.EnumerateFiles(_plansPath, "*.gc-*", SearchOption.AllDirectories))
            File.Delete(tombstone);
        foreach (var temporaryFile in Directory.EnumerateFiles(_plansPath, "*.tmp", SearchOption.AllDirectories))
            File.Delete(temporaryFile);
    }

    private static void TombstoneAndDelete(string recordPath)
    {
        var tombstone = $"{recordPath}.gc-{Guid.NewGuid():N}";
        File.Move(recordPath, tombstone);
        File.Delete(tombstone);
    }

    private static StoreRecord DeserializeRecord(byte[] bytes) =>
        JsonSerializer.Deserialize<StoreRecord>(bytes, JsonOptions)
        // Stryker disable once String : Empty-record wording is diagnostic; the typed unreadable result is asserted.
        ?? throw new JsonException("The plan-store record was empty.");

    private static byte[] SerializeRecord(StoreRecord record) =>
        JsonSerializer.SerializeToUtf8Bytes(record, JsonOptions);

    private static ChangePlanStoreKey KeyFromRecord(StoreRecord record) => new(
        record.RepositoryId,
        Guid.Parse(record.PlanId),
        ChangePlanValidation.NormalizeSha256(record.PlanHash, nameof(record.PlanHash)));

    private static ChangePlanStorePin ToPublicPin(PinRecord pin) => new(
        pin.Kind,
        pin.ReferenceId,
        pin.CreatedAtUtc,
        pin.ExpiresAtUtc,
        pin.AdoptedFromDispatchLeaseId);

    private ChangePlanStoreError? ValidateRecord(StoreRecord record)
    {
        if (record.SchemaVersion != StoreSchemaVersion)
            return null;
        // Stryker disable once Equality : Equality is accepted here but necessarily rejected by the later plan-expiry invariant.
        var retentionPrecedesPersistence = record.RetainUntilUtc < record.PersistedAtUtc;
        if (string.IsNullOrWhiteSpace(record.RepositoryId)
            || string.IsNullOrWhiteSpace(record.PlanId)
            || string.IsNullOrWhiteSpace(record.PlanHash)
            || record.EnvelopeBytes is null
            || record.EnvelopeBytes.LongLength == 0
            || record.EnvelopeBytes.LongLength > _options.MaximumPlanBytes
            || record.Pins is null
            || record.Pins.Length > _options.MaximumPinsPerPlan
            || record.PersistedAtUtc.Offset != TimeSpan.Zero
            || record.RetainUntilUtc.Offset != TimeSpan.Zero
            || retentionPrecedesPersistence)
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Unreadable,
                "The persisted change-plan record metadata is invalid.");
        }

        if (record.Pins.Any(pin => !IsValidPin(pin)))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Unreadable,
                "The persisted change-plan pin metadata is invalid.");
        }

        var duplicatePins = record.Pins
            .GroupBy(pin => (pin.Kind, pin.ReferenceId))
            .Any(group => group.Count() > 1);
        if (duplicatePins)
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Unreadable,
                "The persisted change-plan pin metadata contains duplicates.");
        }
        return null;
    }

    private bool IsValidPin(PinRecord? pin)
    {
        if (pin is null
            || !Enum.IsDefined(pin.Kind)
            || string.IsNullOrWhiteSpace(pin.ReferenceId)
            || Encoding.UTF8.GetByteCount(pin.ReferenceId) > _options.MaximumPinReferenceBytes
            || pin.CreatedAtUtc.Offset != TimeSpan.Zero)
            return false;
        return pin.Kind switch
        {
            ChangePlanPinKind.DispatchLease => pin.ExpiresAtUtc is { Offset: var offset } expiry
                                               && offset == TimeSpan.Zero
                                               && expiry > pin.CreatedAtUtc
                                               && pin.AdoptedFromDispatchLeaseId is null,
            ChangePlanPinKind.OperationLedger => pin.ExpiresAtUtc is null
                                                 && !string.IsNullOrWhiteSpace(pin.AdoptedFromDispatchLeaseId)
                                                 && Encoding.UTF8.GetByteCount(pin.AdoptedFromDispatchLeaseId)
                                                 <= _options.MaximumPinReferenceBytes,
            // Stryker disable once Boolean : The preceding Enum.IsDefined guard makes this default unreachable.
            _ => false
        };
    }

    private void ValidatePinReference(string referenceId, string parameterName)
    {
        if (Encoding.UTF8.GetByteCount(referenceId) > _options.MaximumPinReferenceBytes)
            throw new ArgumentOutOfRangeException(parameterName, "The pin reference exceeds the configured byte limit.");
    }

    private static PinRecord[] SortPins(IEnumerable<PinRecord> pins) => pins
        .OrderBy(pin => pin.Kind)
        .ThenBy(pin => pin.ReferenceId, StringComparer.Ordinal)
        .ToArray();

    private static ChangePlanStoreError? ValidateKey(ChangePlanStoreKey? key)
    {
        if (key is null
            || string.IsNullOrWhiteSpace(key.RepositoryId)
            || key.PlanId == Guid.Empty)
            return new ChangePlanStoreError(ChangePlanStoreErrorKind.Missing, "A complete plan-store key is required.");
        try
        {
            if (!string.Equals(
                    ChangePlanValidation.NormalizeSha256(key.PlanHash, nameof(key.PlanHash)),
                    key.PlanHash,
                    StringComparison.Ordinal))
            {
                return new ChangePlanStoreError(ChangePlanStoreErrorKind.HashMismatch, "The plan hash is not canonical.");
            }
        }
        catch (ArgumentException)
        {
            return new ChangePlanStoreError(ChangePlanStoreErrorKind.HashMismatch, "The plan hash is invalid.");
        }
        return null;
    }

    private static bool PathsOverlap(string left, string right)
    {
        // Stryker disable once Conditional : Windows and POSIX case policy is exercised by native platform jobs.
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return IsContained(left, right, comparison) || IsContained(right, left, comparison);
    }

    private static bool IsContained(string root, string path, StringComparison comparison)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (string.Equals(normalizedRoot, normalizedPath, comparison))
            return true;
        // Stryker disable once Conditional : A filesystem-root store also fails the independent owner-private root check.
        var rootPrefix = Path.EndsInDirectorySeparator(normalizedRoot)
            ? normalizedRoot
            : $"{normalizedRoot}{Path.DirectorySeparatorChar}";
        return normalizedPath.StartsWith(rootPrefix, comparison);
    }

    private static bool HasReparsePointInExistingPath(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
        return false;
    }

    private static void ApplyDirectoryPermissions(string path)
    {
        // Stryker disable all : Windows ACL application is exercised by the native Windows filesystem job.
        if (OperatingSystem.IsWindows())
        {
            ApplyDirectoryPermissionsOnWindows(path);
            return;
        }
        // Stryker restore all
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void ApplyFilePermissions(string path)
    {
        // Stryker disable all : Windows ACL application is exercised by the native Windows filesystem job.
        if (OperatingSystem.IsWindows())
        {
            ApplyFilePermissionsOnWindows(path);
            return;
        }
        // Stryker restore all
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // Stryker disable all : Windows ACL behavior is exercised by the native Windows filesystem job.
    [SupportedOSPlatform("windows")]
    private static void ApplyDirectoryPermissionsOnWindows(string path)
    {
        var owner = CurrentWindowsPrincipal();
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(security);
    }
    // Stryker restore all

    // Stryker disable all : Windows ACL behavior is exercised by the native Windows filesystem job.
    [SupportedOSPlatform("windows")]
    private static void ApplyFilePermissionsOnWindows(string path)
    {
        var owner = CurrentWindowsPrincipal();
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(security);
    }
    // Stryker restore all

    // Stryker disable all : Windows ACL behavior is exercised by the native Windows filesystem job.
    [SupportedOSPlatform("windows")]
    private static ChangePlanStoreError? ValidatePrivateDirectoryOnWindows(string path)
    {
        return ValidateWindowsAccessControl(new DirectoryInfo(path).GetAccessControl(), path);
    }
    // Stryker restore all

    // Stryker disable all : Windows ACL behavior is exercised by the native Windows filesystem job.
    [SupportedOSPlatform("windows")]
    private static ChangePlanStoreError? ValidatePrivateFileOnWindows(string path)
    {
        return ValidateWindowsAccessControl(new FileInfo(path).GetAccessControl(), path);
    }
    // Stryker restore all

    // Stryker disable all : Windows ACL behavior is exercised by the native Windows filesystem job.
    [SupportedOSPlatform("windows")]
    private static ChangePlanStoreError? ValidateWindowsAccessControl(
        FileSystemSecurity security,
        string path)
    {
        var owner = CurrentWindowsPrincipal();
        if (!security.AreAccessRulesProtected
            || !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.UnsafeProtection,
                $"Protected plan-store path '{path}' does not have an owner-private ACL.");
        }

        var rules = security.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType == AccessControlType.Allow
                && !owner.Equals(rule.IdentityReference))
            {
                return new ChangePlanStoreError(
                    ChangePlanStoreErrorKind.UnsafeProtection,
                    $"Protected plan-store path '{path}' grants access outside its owning principal.");
            }
        }
        return null;
    }
    // Stryker restore all

    // Stryker disable all : Windows identity resolution is exercised by the native Windows filesystem job.
    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentWindowsPrincipal()
    {
        return WindowsIdentity.GetCurrent().User
               ?? throw new UnauthorizedAccessException("The current Windows principal has no security identifier.");
    }
    // Stryker restore all

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

    private static IOException NativeIOException(string operation, string path, int error)
    {
        return new IOException(
            // Stryker disable once String : Native error wording is diagnostic; failure mapping is asserted through the durability seam.
            $"Could not {operation} '{path}': {new Win32Exception(error).Message}",
            new Win32Exception(error));
    }

    private static string LowerSha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    // Stryker disable once Equality : Equal timestamp operands produce the same result through either branch.
    private static DateTimeOffset Max(DateTimeOffset left, DateTimeOffset right) => left >= right ? left : right;

    private static bool IsRecordException(Exception exception) => exception is JsonException
        or ArgumentException
        or InvalidOperationException
        or FormatException
        or OverflowException;

    private static bool IsStorageException(Exception exception) => exception is IOException
        or UnauthorizedAccessException
        or NotSupportedException
        or System.Security.SecurityException;

    private ChangePlanStoreError StorageError(Exception exception) => new(
        ChangePlanStoreErrorKind.Unreadable,
        $"The protected change-plan store could not be read or written: {exception.Message}",
        new Dictionary<string, string> { ["store"] = _rootPath });

    private static Result<T, ChangePlanStoreError> Error<T>(ChangePlanStoreErrorKind kind, string message) =>
        Result<T, ChangePlanStoreError>.Err(new ChangePlanStoreError(kind, message));

    private sealed class PlatformChangePlanStoreDurability : IChangePlanStoreDurability
    {
        internal static PlatformChangePlanStoreDurability Instance { get; } = new();

        void IChangePlanStoreDurability.FlushFileMetadata(string path) => FlushFileMetadataOnPlatform(path);

        void IChangePlanStoreDurability.ReplaceFile(string temporaryPath, string recordPath) =>
            ReplaceFileOnPlatform(temporaryPath, recordPath);

        void IChangePlanStoreDurability.FlushDirectoryMetadata(string directory) =>
            FlushDirectoryMetadataOnPlatform(directory);
    }

    private sealed record StoreRecord(
        int SchemaVersion,
        string RepositoryId,
        string PlanId,
        string PlanHash,
        DateTimeOffset PersistedAtUtc,
        DateTimeOffset RetainUntilUtc,
        byte[] EnvelopeBytes,
        PinRecord[] Pins);

    private sealed record PinRecord(
        ChangePlanPinKind Kind,
        string ReferenceId,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? ExpiresAtUtc,
        string? AdoptedFromDispatchLeaseId);

    private sealed record StoreInventory(int Count, long Bytes);

    private sealed class ChangePlanStoreConflictException : Exception
    {
        internal ChangePlanStoreConflictException()
        {
        }

        internal ChangePlanStoreConflictException(string message)
            : base(message)
        {
        }

        internal ChangePlanStoreConflictException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
