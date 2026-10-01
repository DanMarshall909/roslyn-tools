using System.Runtime.InteropServices;

namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>The platform admission shared by semantic-diff execution and discovery.</summary>
internal static class WorkingTreeReadCapability
{
    internal static bool IsAvailable => OperatingSystem.IsLinux()
        && RuntimeInformation.ProcessArchitecture == Architecture.X64;
}
