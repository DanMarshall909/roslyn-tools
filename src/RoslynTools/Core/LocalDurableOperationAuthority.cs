using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RoslynTools.Core;

/// <summary>
///     Principal-private experimental authority for the local durable-operation roots.
///     The host fixes these roots outside repository control and supplies the current
///     OS principal; request payloads can select neither value. This authority is limited
///     to local <see cref="ChangePlanCoordinationDomain.ExclusivePrincipal" /> operation
///     preparation and does not establish shared-authority or repository-effect safety.
/// </summary>
internal sealed class LocalDurableOperationAuthority
    : IChangePlanStoreProtectionAuthority,
      IOperationLedgerProtectionAuthority,
      IAuthenticatedPrincipalProvider,
      IAuthenticatedOperationContextProvider
{
    private const string LeaseFileName = ".physical-protection.lease";
    private const int AuthenticatedMaximumOperationCount = 8;
    private const long AuthenticatedMaximumRequestBytes = 16 * 1024 * 1024;
    private readonly AuthenticatedPrincipalId _principal;
    private readonly Func<DateTimeOffset> _utcNow;

    internal LocalDurableOperationAuthority(
        string? controlRoot = null,
        Func<DateTimeOffset>? utcNow = null)
    {
        ControlRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(
            controlRoot ?? DefaultControlRoot));
        PlanStoreRoot = Path.Combine(ControlRoot, "plan-store");
        LedgerRoot = Path.Combine(ControlRoot, "operation-ledger");
        _principal = ResolvePrincipal();
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    }

    internal static string DefaultControlRoot => Path.Combine(
        RepositoryTrustRegistry.ResolveLocalApplicationData(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
        "RoslynTools",
        "durable-operations");

    internal string ControlRoot { get; }

    internal string PlanStoreRoot { get; }

    internal string LedgerRoot { get; }

    /// <summary>Returns the stable OS principal captured by this host authority.</summary>
    public AuthenticatedPrincipalId GetPrincipal() => _principal;

    /// <summary>
    ///     Admits a new local operation only when its public epoch fields exactly match the
    ///     host's bounded current-hour policy and its canonical request fits the byte limit.
    /// </summary>
    /// <returns>
    ///     The host-derived principal and canonical claims, or a typed access/input error.
    ///     This call does not write the repository or durable control state.
    /// </returns>
    public Result<AuthenticatedOperationContext, RtError> Authenticate(
        string repositoryId,
        OperationId operationId,
        int maximumOperationCount,
        long maximumRequestBytes,
        DateTimeOffset notAfterUtc,
        long actualRequestBytes)
    {
        ArgumentNullException.ThrowIfNull(operationId);
        var now = _utcNow().ToUniversalTime();
        var expectedEpoch = CurrentEpoch(now);
        var expectedNotAfter = EpochNotAfter(expectedEpoch);
        if (operationId.Epoch != expectedEpoch
            || maximumOperationCount != AuthenticatedMaximumOperationCount
            || maximumRequestBytes != AuthenticatedMaximumRequestBytes
            || notAfterUtc.ToUniversalTime() != expectedNotAfter)
        {
            return Result<AuthenticatedOperationContext, RtError>.Err(new RtError(
                RtErrorKind.OperationAccessDenied,
                "The operation-epoch claims were not issued by the protected host authority."));
        }
        if (actualRequestBytes < 0 || actualRequestBytes > maximumRequestBytes)
        {
            return Result<AuthenticatedOperationContext, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "The canonical operation request exceeds the authenticated operation-epoch byte limit."));
        }

        var claims = new OperationEpochClaims(
            repositoryId,
            _principal.Value,
            expectedEpoch,
            AuthenticatedMaximumOperationCount,
            AuthenticatedMaximumRequestBytes,
            expectedNotAfter);
        return Result<AuthenticatedOperationContext, RtError>.Ok(new(
            _principal,
            claims));
    }

    internal static long CurrentEpoch(DateTimeOffset nowUtc) =>
        checked(nowUtc.ToUniversalTime().ToUnixTimeSeconds() / 3600 + 1);

    internal static DateTimeOffset EpochNotAfter(long epoch) =>
        DateTimeOffset.FromUnixTimeSeconds(checked(epoch * 3600));

    ChangePlanStoreError? IChangePlanStoreProtectionAuthority.AcquireForPersistence(
        string storeRootPath,
        ChangePlanRepository repository,
        out IChangePlanStoreProtectionLease? lease)
    {
        ArgumentNullException.ThrowIfNull(repository);
        lease = null;
        if (!SamePath(storeRootPath, PlanStoreRoot))
            return UnsafePlan("The host did not select the configured protected plan-store root.");
        if (PathsOverlap(ControlRoot, repository.RepositoryRoot)
            || PathsOverlap(ControlRoot, repository.ResolvedRepositoryRoot))
            return UnsafePlan("The durable operation control root overlaps the target repository.");

        var acquired = AcquireRoot(PlanStoreRoot, createIfMissing: true);
        if (acquired.Error is not null)
            return UnsafePlan(acquired.Error);
        lease = new PlanProtectionLease(acquired.Stream!);
        return null;
    }

    ChangePlanStoreError? IChangePlanStoreProtectionAuthority.AcquireExisting(
        string storeRootPath,
        out IChangePlanStoreProtectionLease? lease)
    {
        lease = null;
        if (!SamePath(storeRootPath, PlanStoreRoot))
            return UnsafePlan("The host did not select the configured protected plan-store root.");
        if (!Directory.Exists(PlanStoreRoot))
        {
            return new ChangePlanStoreError(
                ChangePlanStoreErrorKind.Missing,
                "The protected plan store does not exist.");
        }

        var acquired = AcquireRoot(PlanStoreRoot, createIfMissing: false);
        if (acquired.Error is not null)
            return UnsafePlan(acquired.Error);
        lease = new PlanProtectionLease(acquired.Stream!);
        return null;
    }

    OperationLedgerError? IOperationLedgerProtectionAuthority.Acquire(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease)
    {
        lease = null;
        if (!SamePath(ledgerRootPath, LedgerRoot))
            return UnsafeLedger("The host did not select the configured protected operation-ledger root.");

        var acquired = AcquireRoot(LedgerRoot, createIfMissing: true);
        if (acquired.Error is not null)
            return UnsafeLedger(acquired.Error);
        lease = new LedgerProtectionLease(acquired.Stream!);
        return null;
    }

    OperationLedgerError? IOperationLedgerProtectionAuthority.AcquireExisting(
        string ledgerRootPath,
        out IOperationLedgerProtectionLease? lease)
    {
        lease = null;
        if (!SamePath(ledgerRootPath, LedgerRoot))
            return UnsafeLedger("The host did not select the configured protected operation-ledger root.");
        if (!Directory.Exists(LedgerRoot)
            || !File.Exists(Path.Combine(LedgerRoot, LeaseFileName)))
        {
            return new OperationLedgerError(
                OperationLedgerErrorKind.Missing,
                // Stryker disable once String : Missing is the public contract; wording is diagnostic.
                "The protected operation ledger does not exist.");
        }

        // Stryker disable once Boolean : The precondition above proves the lease exists; changing
        // this only affects a filesystem deletion race that needs an injected filesystem seam.
        var acquired = AcquireRoot(LedgerRoot, createIfMissing: false);
        if (acquired.Error is not null)
            return UnsafeLedger(acquired.Error);
        lease = new LedgerProtectionLease(acquired.Stream!);
        return null;
    }

    private static (FileStream? Stream, string? Error) AcquireRoot(
        string rootPath,
        bool createIfMissing)
    {
        try
        {
            var createdRoot = !Directory.Exists(rootPath);
            if (createdRoot)
            {
                if (!createIfMissing)
                    return (null, "The protected control root does not exist.");
                CreatePrivateDirectoryTree(rootPath);
            }

            if (HasReparsePointInExistingPath(rootPath))
                return (null, "The protected control root contains a symbolic-link or reparse boundary.");

            var directoryError = ValidatePrivateDirectory(rootPath);
            if (directoryError is not null)
                return (null, directoryError);

            var leasePath = Path.Combine(rootPath, LeaseFileName);
            if (!File.Exists(leasePath))
            {
                if (!createIfMissing)
                    return (null, "The protected control-root lease does not exist.");
                try
                {
                    using var created = new FileStream(
                        leasePath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        1,
                        FileOptions.WriteThrough);
                    created.WriteByte(1);
                    // Stryker disable all : A power-loss flush cannot be distinguished in-process;
                    // WriteThrough plus the initialized lease byte are asserted separately.
                    created.Flush(flushToDisk: true);
                    // Stryker restore all
                    ApplyPrivateFilePermissions(leasePath);
                }
                catch (IOException) when (File.Exists(leasePath))
                {
                    // Another process completed the same private-root provisioning race.
                    // The common validation path below must still prove the resulting file.
                }
            }
            if ((File.GetAttributes(leasePath) & FileAttributes.ReparsePoint) != 0)
                return (null, "The protected control-root lease is a symbolic link or reparse point.");
            var fileError = ValidatePrivateFile(leasePath);
            if (fileError is not null)
                return (null, fileError);

            return (new FileStream(
                leasePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1,
                FileOptions.RandomAccess), null);
        }
        catch (Exception exception) when (exception is IOException
                                          or UnauthorizedAccessException
                                          or PlatformNotSupportedException)
        {
            return (null, $"The host could not prove the protected control root: {exception.Message}");
        }
    }

    private static void CreatePrivateDirectoryTree(string directory)
    {
        var missing = new Stack<string>();
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
        }
    }

    private static bool HasReparsePointInExistingPath(string path)
    {
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                return true;
        }
        return false;
    }

    private static void ApplyPrivateDirectoryPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyPrivateDirectoryPermissionsOnWindows(path);
            return;
        }
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static void ApplyPrivateFilePermissions(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyPrivateFilePermissionsOnWindows(path);
            return;
        }
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static string? ValidatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
            return ValidateWindowsAccessControl(new DirectoryInfo(path).GetAccessControl(), path);
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        return File.GetUnixFileMode(path) == expected
            ? null
            : $"Protected control directory '{path}' is not private to its owning principal.";
    }

    private static string? ValidatePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows())
            return ValidateWindowsAccessControl(new FileInfo(path).GetAccessControl(), path);
        var expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return File.GetUnixFileMode(path) == expected
            ? null
            : $"Protected control file '{path}' is not private to its owning principal.";
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyPrivateDirectoryPermissionsOnWindows(string path)
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

    [SupportedOSPlatform("windows")]
    private static void ApplyPrivateFilePermissionsOnWindows(string path)
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

    [SupportedOSPlatform("windows")]
    private static string? ValidateWindowsAccessControl(FileSystemSecurity security, string path)
    {
        var owner = CurrentWindowsPrincipal();
        if (!security.AreAccessRulesProtected
            || !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            return $"Protected control path '{path}' does not have an owner-private ACL.";
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
                return $"Protected control path '{path}' grants access outside its owning principal.";
            }
        }
        return null;
    }

    private static AuthenticatedPrincipalId ResolvePrincipal()
    {
        if (OperatingSystem.IsWindows())
            return new AuthenticatedPrincipalId($"windows-sid:{CurrentWindowsPrincipal().Value}");
        return new AuthenticatedPrincipalId($"unix-uid:{GetEffectiveUserId()}");
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentWindowsPrincipal() =>
        WindowsIdentity.GetCurrent().User
        ?? throw new UnauthorizedAccessException(
            "The current Windows principal has no security identifier.");

    private static bool SamePath(string left, string right) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
        OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal);

    private static bool PathsOverlap(string left, string right)
    {
        var normalizedLeft = Path.TrimEndingDirectorySeparator(Path.GetFullPath(left));
        var normalizedRight = Path.TrimEndingDirectorySeparator(Path.GetFullPath(right));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return IsContained(normalizedLeft, normalizedRight, comparison)
               || IsContained(normalizedRight, normalizedLeft, comparison);
    }

    private static bool IsContained(string path, string root, StringComparison comparison)
    {
        if (string.Equals(path, root, comparison))
            return true;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : $"{root}{Path.DirectorySeparatorChar}";
        return path.StartsWith(prefix, comparison);
    }

    private static ChangePlanStoreError UnsafePlan(string message) => new(
        ChangePlanStoreErrorKind.UnsafeProtection,
        message);

    private static OperationLedgerError UnsafeLedger(string message) => new(
        OperationLedgerErrorKind.UnsafeProtection,
        message);

    private sealed class PlanProtectionLease(FileStream stream) : IChangePlanStoreProtectionLease
    {
        /// <summary>Releases the held local protection marker.</summary>
        public void Dispose() => stream.Dispose();
    }

    private sealed class LedgerProtectionLease(FileStream stream) : IOperationLedgerProtectionLease
    {
        /// <summary>Releases the held local protection marker.</summary>
        public void Dispose() => stream.Dispose();
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint GetEffectiveUserId();
}
