using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Replaces a call with a new implementation or method.
/// </summary>
internal sealed class ReplaceCallCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly ReplaceCallOperation _operation;

    public ReplaceCallCommand(
        DaemonRpcRecoveryCoordinator daemon,
        ReplaceCallOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => ReplaceCallCommandDefinition.Info;

    public Command Build()
    {
        var fromOpt = new Option<string>("--from", "Full method reference to replace, e.g. Logger.Log")
        { IsRequired = true };
        var toOpt = new Option<string>("--to", "New method name, e.g. LogInfo") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var ifArgIndexOpt = new Option<int?>("--if-arg",
            "Filter: only replace calls where argument at this index matches --if-arg-literal");
        var ifArgLiteralOpt = new Option<string?>("--if-arg-literal", "Filter: match this string literal value");

        var cmd = new Command("replace-call", "Replace all invocations of one static method with another")
        {
            fromOpt, toOpt, solutionOpt, dryRunOpt, ifArgIndexOpt, ifArgLiteralOpt
        };
        cmd.AddAlias("rc");

        cmd.SetHandler(async context =>
            {
                var from = context.ParseResult.GetValueForOption(fromOpt)!;
                var to = context.ParseResult.GetValueForOption(toOpt)!;
                var solution = context.ParseResult.GetValueForOption(solutionOpt);
                var dryRun = context.ParseResult.GetValueForOption(dryRunOpt);
                var ifArgIndex = context.ParseResult.GetValueForOption(ifArgIndexOpt);
                var ifArgLiteral = context.ParseResult.GetValueForOption(ifArgLiteralOpt);
                await DaemonCommandExecutor.ExecuteAsync(
                    _daemon,
                    _operation,
                    new ReplaceCallRequest(from, to, solution, dryRun, ifArgIndex, ifArgLiteral),
                    context);
            });

        return cmd;
    }

}
