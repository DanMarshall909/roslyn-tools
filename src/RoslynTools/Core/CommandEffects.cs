namespace RoslynTools.Core;

/// <summary>
///     Observable effects and privileged behaviours of a command. This is deliberately
///     separate from mutation defaults: an operation may write local maintainer state or
///     call an external service without modifying repository source.
/// </summary>
[Flags]
internal enum CommandEffects
{
    None = 0,
    RepositoryRead = 1 << 0,
    RepositoryWrite = 1 << 1,
    LocalStateRead = 1 << 2,
    LocalStateWrite = 1 << 3,
    ExternalRead = 1 << 4,
    ExternalWrite = 1 << 5,
    RepositoryCodeExecution = 1 << 6
}

/// <summary>Audience allowed to discover a command through an ordinary host adapter.</summary>
internal enum CommandAudience
{
    Public,
    TrustedExperimental,
    Maintainer,
    Removed
}
