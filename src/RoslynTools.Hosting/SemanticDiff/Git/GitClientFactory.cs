namespace RoslynTools.Services.SemanticDiff.Git;

/// <summary>Creates git process clients for shared host composition.</summary>
internal sealed class GitClientFactory : IGitClientFactory
{
    public IGitClient Create(string workingDirectory) => new GitClient(workingDirectory);
}
