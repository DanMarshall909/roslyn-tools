namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>Creates a git source client for a caller-selected working directory.</summary>
internal interface IGitClientFactory
{
    IGitClient Create(string workingDirectory);
}
