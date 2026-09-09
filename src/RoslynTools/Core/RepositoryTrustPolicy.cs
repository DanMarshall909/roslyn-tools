namespace RoslynTools.Core;

/// <summary>Opt-in consumption of saved local approvals, independent of repository and CI configuration.</summary>
internal static class RepositoryTrustPolicy
{
    internal static IReadOnlyList<string> ReadUsableRoots(RepositoryTrustStore store)
    {
        // Re-read at each invocation: removal affects the next request, not an already
        // running evaluation. This is not the physical-identity lease protocol.
        return store.Read().Select(root => RepositoryTrustStore.NormalizeRoot(root, requireExists: true)).ToArray();
    }

    internal static IDisposable? EnterCliScope(
        bool explicitTrust,
        string? explicitRoot,
        bool useTrustedRoots,
        RepositoryTrustStore store)
    {
        // Existing one-shot CLI/CI approval takes precedence and never reads the registry.
        if (explicitTrust)
            return RepositoryTrustContext.UseExplicitTrust(explicitRoot!);

        // The compatibility bridge may already have established a host-owned scope.
        return useTrustedRoots ? RepositoryTrustContext.UseExplicitTrust(ReadUsableRoots(store)) : null;
    }
}
