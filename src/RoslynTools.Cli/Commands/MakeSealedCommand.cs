using System.CommandLine;
using System.CommandLine.Invocation;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a class to be sealed
/// </summary>
internal sealed class MakeSealedCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcClient _daemon;

    public MakeSealedCommand(DaemonRpcClient daemon)
    {
        _daemon = daemon;
    }

    // Stryker disable all
    public static RtCommandInfo Info => MakeSealedCommandDefinition.Info;
    // Stryker restore all

    // Stryker disable all
    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class to seal");
        var binder = RtCommandFlagBinder.Create()
            .WithSolution();
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview without writing (default)");
        var applyOpt = new Option<bool>("--apply", "Apply the sealed modifier to disk");

        var cmd = new Command("make-sealed", "Add the sealed modifier to a class that has no derived types");
        cmd.AddAlias("ms");
        cmd.AddAll(typeOpt, dryRunOpt, applyOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var req = binder.Bind(ctx.ParseResult, f =>
                new MakeSealedCliRequest(
                    ctx.ParseResult.GetValueForOption(typeOpt)!,
                    f.SolutionPath,
                    !ctx.ParseResult.GetValueForOption(applyOpt)));
            await ExecuteThroughDaemonAsync(new MakeSealedRequest(req.Type, req.SolutionPath, req.DryRun), ctx);
        });

        return cmd;
    }
    // Stryker restore all

    private async Task ExecuteThroughDaemonAsync(MakeSealedRequest request, InvocationContext context)
    {
        await DaemonCommandExecutor.ExecuteAsync(
            _daemon,
            MakeSealedCommandDefinition.Name,
            request,
            context);
    }

    private sealed record MakeSealedCliRequest(
        string Type,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption;
}
