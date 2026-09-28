using Microsoft.Build.Locator;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;

namespace RoslynTools.Daemon;

internal sealed class DaemonMsBuildCompatibility(
    MsBuildInstanceIdentity registeredIdentity,
    Func<string, MsBuildInstanceIdentity?>? resolveIdentity = null)
    : IDaemonSdkContextGuard
{
    private readonly MsBuildInstanceIdentity _registeredIdentity = registeredIdentity;
    private readonly Func<string, MsBuildInstanceIdentity?> _resolveIdentity =
        resolveIdentity ?? ResolveIdentity;

    public Result<bool, RtError> Check(string directory)
    {
        MsBuildInstanceIdentity? requestedIdentity;
        try
        {
            requestedIdentity = _resolveIdentity(directory);
        }
        catch (Exception exception) when (exception is ArgumentException
                                          or DllNotFoundException
                                          or InvalidOperationException
                                          or IOException
                                          or UnauthorizedAccessException)
        {
            return Result<bool, RtError>.Err(MsBuildInstanceCompatibility.SelectionError(
                directory,
                $"Could not select the .NET SDK: {exception.Message}",
                "daemon",
                "daemonRestartRequired"));
        }

        if (requestedIdentity is null)
        {
            return Result<bool, RtError>.Err(MsBuildInstanceCompatibility.SelectionError(
                directory,
                "No compatible .NET SDK could be selected.",
                "daemon",
                "daemonRestartRequired"));
        }

        return MsBuildInstanceCompatibility.Validate(
            _registeredIdentity,
            requestedIdentity,
            directory,
            "daemon",
            "daemonRestartRequired");
    }

    private static MsBuildInstanceIdentity? ResolveIdentity(string directory)
    {
        var instance = MsBuildBootstrap.FindPreferredInstance(directory);
        return CreateIdentity(instance);
    }

    internal static MsBuildInstanceIdentity? CreateIdentity(VisualStudioInstance? instance) =>
        instance is null ? null : MsBuildInstanceIdentity.From(instance);

}
