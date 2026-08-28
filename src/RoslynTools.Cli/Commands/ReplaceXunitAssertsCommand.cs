using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Replaces XUnit assertions with alternative testing methods.
/// </summary>
internal sealed class ReplaceXunitAssertsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ReplaceXunitAssertsOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ReplaceXunitAssertsOperation _operation = operation;

    public static RtCommandInfo Info => ReplaceXunitAssertsCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var applyOpt = new Option<bool>("--apply",
            "Write changes to disk. Without this, the command prints a review summary.");
        var reviewOpt = new Option<bool>("--review",
            "Summarize matching assert call sites without writing files or printing a full diff. This is the default mode.");

        var cmd = new Command("replace-xunit-asserts", "Replace common Xunit Assert calls with Shouldly assertions");
        cmd.AddAlias("rxa");
        cmd.AddAll(solutionOpt, dryRunOpt, applyOpt, reviewOpt);

        cmd.SetHandler(async context =>
        {
            var request = new ReplaceXunitAssertsRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt),
                context.ParseResult.GetValueForOption(applyOpt),
                context.ParseResult.GetValueForOption(reviewOpt));
            await DaemonCommandExecutor.ExecuteAsync(_daemon, _operation, request, context);
        });

        return cmd;
    }
}
