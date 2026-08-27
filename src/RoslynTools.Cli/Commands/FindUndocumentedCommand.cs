using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Identifies undocumented symbols in the current file
/// </summary>
internal sealed class FindUndocumentedCommand(
    DaemonRpcRecoveryCoordinator daemon,
    FindUndocumentedOperation operation) : IRtCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindUndocumentedOperation _operation = operation;
    public static RtCommandInfo Info => FindUndocumentedCommandDefinition.Info;
    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithScope("Namespace prefix filter (e.g. RoslynTools.Core)");
        var membersOpt = new Option<bool>("--members", "Also report undocumented public methods and properties");
        var levelOpt = new Option<string>("--level",
            "Comma-separated accessibility levels to check: public, internal, protected, private, all (default: public)")
        {
            ArgumentHelpName = "levels"
        };
        levelOpt.SetDefaultValue("public");

        var cmd = new Command("find-undocumented", "Report types and members with no XML doc comment");
        cmd.AddAlias("undoc");
        cmd.Add(membersOpt);
        cmd.Add(levelOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var members = ctx.ParseResult.GetValueForOption(membersOpt);
            var level = ctx.ParseResult.GetValueForOption(levelOpt);
            var req = binder.Bind(ctx.ParseResult, f => new FindUndocumentedCliRequest(f.SolutionPath, f.Scope));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new FindUndocumentedRequest(req.SolutionPath, members, req.Scope, level!),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindUndocumentedResult>.Ok(
                    FindUndocumentedResult.FromAnalysis(value))),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record FindUndocumentedCliRequest(
        string? SolutionPath,
        string? Scope) : ISupportsSolutionOption, ISupportsScopeOption;
}
