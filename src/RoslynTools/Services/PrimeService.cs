using System.Diagnostics.CodeAnalysis;

namespace RoslynTools.Services;

/// <summary>
///     Produces a compact session-start summary for <c>rt prime</c>.
///     No MSBuild, no solution load — returns a hardcoded hot-command list.
///     Fast enough to run from a <c>UserPromptSubmit</c> hook.
///     Trade-off: the list is static and will need manual updates as the catalog grows.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class registered with DI; IRtService contract requires instance.")]
internal sealed class PrimeService : IRtService
{
    public PrimeResult Build() => new(
        Gate: "rt inspect outline/describe <file> before reading or editing any .cs file",
        Entry: "rt suggest \"<task in English>\" to rank commands by intent",
        Note: "Every inspect JSON result includes _suggestions pre-filled with FQN + solution path",
        HotCommands:
        [
            "rt inspect describe \"<FQN>\" --solution <slnx>",
            "rt inspect outline <file.cs> --solution <slnx>",
            "rt inspect find-refs --symbol <Name> --solution <slnx>",
            "rt refactor rename \"<FQN>\" NewName --solution <slnx> --dry-run",
            "rt refactor move --type <FQN> --to-namespace <NS> --solution <slnx> --dry-run",
            "rt suggest \"<task in English>\""
        ]);
}
