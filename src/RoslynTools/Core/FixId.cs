using System.Security.Cryptography;
using System.Text;

namespace RoslynTools.Core;

/// <summary>
/// Deterministic 8-hex identifier for a single proposed mutation, derived from the
/// command name, target key (symbol FQN or repo-relative path), and mutation kind.
/// Stable across runs as long as the inputs haven't changed.
/// </summary>
internal static class FixId
{
    public static string Compute(string command, string target, string mutationKind)
    {
        var input = $"{command}|{target}|{mutationKind}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }
}
