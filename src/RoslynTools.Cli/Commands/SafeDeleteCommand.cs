using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for safe deletion with asynchronous execution.
/// </summary>
internal sealed class SafeDeleteCommand(IWorkspaceSessionFactory sessionFactory, SafeDeleteService safeDelete) : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly SafeDeleteService _safeDelete = safeDelete;
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
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx")
            .WithDryRun("Preview without writing")
            .WithSymbol("Type or member FQN or simple name to delete")
            .WithKind("Kind of symbol: type, method, or property");

        var cmd = new Command("safe-delete", "Delete a type or member only if it has no references");
        cmd.AddAlias("sd");
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var request = flags.Bind(ctx.ParseResult, SafeDeleteCliRequest.FromFlags);
            try
            {
                await ExecuteAsync(request.Symbol!, request.Kind, request.SolutionPath, request.DryRun);
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                Environment.ExitCode = 1;
            }
        });

        return cmd;
    }

    private sealed record SafeDeleteCliRequest(
        string? Symbol,
        string? Kind,
        string? SolutionPath,
        bool DryRun) : ISupportsSymbolOption, ISupportsKindOption, ISupportsSolutionOption, ISupportsDryRunOption
    {
        public static SafeDeleteCliRequest FromFlags(RtFlagValues flags)
        {
            return new SafeDeleteCliRequest(flags.Symbol, flags.Kind, flags.SolutionPath, flags.DryRun);
        }
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
