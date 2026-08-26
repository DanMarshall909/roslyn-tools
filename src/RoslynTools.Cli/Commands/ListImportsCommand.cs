using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Lists all imported namespaces in the current file
/// </summary>
internal sealed class ListImportsCommand : IRtCommand, IAnalyzeCommand
{
    private readonly DaemonRpcClient _daemon;

    public ListImportsCommand(DaemonRpcClient daemon)
    {
        _daemon = daemon;
    }

    public static RtCommandInfo Info => ListImportsCommandDefinition.Info;

    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx");
        var kindOpt = new Option<string>("--kind", () => "all", "Filter: all, global, static, or alias");

        var cmd = new Command("list-imports", "List using directives, including global and static imports");
        cmd.AddAlias("imports");
        cmd.Add(kindOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var kind = ctx.ParseResult.GetValueForOption(kindOpt);
            var req = binder.Bind(ctx.ParseResult, f => new ListImportsCliRequest(f.SolutionPath, kind!));
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                ListImportsCommandDefinition.Name,
                new ListImportsRequest(req.SolutionPath, req.ImportKind),
                ctx);
        });

        return cmd;
    }

    private sealed record ListImportsCliRequest(string? SolutionPath, string ImportKind) : ISupportsSolutionOption;
}
