using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all occurrences of a given pattern in the current file
/// </summary>
internal sealed class FindPatternCommand(
    DaemonRpcRecoveryCoordinator daemon,
    FindPatternOperation operation) : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindPatternOperation _operation = operation;

    public static RtCommandInfo Info => FindPatternCommandDefinition.Info;

    public Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file containing the pattern.")
        { IsRequired = true };
        var startLineOpt = new Option<int>("--start-line", "First line of the pattern range (1-based).")
        { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the pattern range (1-based, inclusive).")
        { IsRequired = true };
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx. Auto-detected if omitted.")
            .WithScope("Filter matches to documents in this namespace prefix or project name.");

        var cmd = new Command("find-pattern",
            "Find all solution locations structurally matching a given statement range.");
        cmd.AddAlias("fp");
        cmd.AddAll(fileOpt, startLineOpt, endLineOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var file = ctx.ParseResult.GetValueForOption(fileOpt)!;
            var startLine = ctx.ParseResult.GetValueForOption(startLineOpt);
            var endLine = ctx.ParseResult.GetValueForOption(endLineOpt);
            var req = binder.Bind(ctx.ParseResult, f => new FindPatternCliRequest(f.SolutionPath, f.Scope));
            RtLog.Info("Searching for pattern at {File}:{StartLine}-{EndLine}...", file, startLine, endLine);
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new FindPatternRequest(file, startLine, endLine, req.SolutionPath, req.Scope),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindPatternResult>.Ok(value)),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record FindPatternCliRequest(string? SolutionPath, string? Scope)
        : ISupportsSolutionOption, ISupportsScopeOption;
}
