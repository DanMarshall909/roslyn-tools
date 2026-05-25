using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for safe deletion with asynchronous execution.
/// </summary>
internal sealed class SafeDeleteCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly SafeDeleteService _safeDelete;
    public SafeDeleteCommand(IWorkspaceSessionFactory sessionFactory, SafeDeleteService safeDelete)
    {
        _safeDelete = safeDelete;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "safe-delete",
        true,
        false,
        "refactor",
        Aliases: ["sd"],
        WhenToUse:
        "Deleting a type or member only after verifying it has zero references in the solution. Safer than a manual delete because the command checks references (excluding nameof() and the declaration itself) before touching any file, and exits non-zero if references exist.",
        Examples:
        [
            "rt refactor safe-delete --symbol OrphanedHelper --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor sd --symbol BookingService.LegacyMethod --kind method --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor safe-delete --symbol OldDtoType --kind type --solution RoslynTools.slnx"
        ],
        SeeAlso: ["find-unused", "find-refs"]
    );

    public Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Type or member FQN or simple name to delete")
        { IsRequired = true };
        var kindOpt = new Option<string?>("--kind", "Kind of symbol: type, method, or property");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("safe-delete", "Delete a type or member only if it has no references");
        cmd.AddAlias("sd");
        cmd.Add(symbolOpt);
        cmd.Add(kindOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (symbol, kind, solution, dryRun) =>
        {
            try
            {
                await ExecuteAsync(symbol, kind, solution, dryRun);
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                Environment.ExitCode = 1;
            }
        }, symbolOpt, kindOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal async Task ExecuteAsync(string symbol, string? kind, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _safeDelete.RunAsync(sln, symbol, kind, dryRun);
        }
    }
}
