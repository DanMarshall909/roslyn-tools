using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a type to a record format.
/// </summary>
internal sealed class ConvertToRecordCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ConvertToRecordOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ConvertToRecordOperation _operation = operation;

    public static RtCommandInfo Info => ConvertToRecordCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption();
        var flags = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithDryRun();

        var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
        cmd.AddAlias("ctr");
        cmd.Add(typeOpt);
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var type = ctx.ParseResult.GetValueForOption(typeOpt)!;
            var request = flags.Bind(ctx.ParseResult, f => ConvertToRecordCliRequest.FromFlags(f, type));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new ConvertToRecordRequest(
                    request.TypeName,
                    request.SolutionPath,
                    request.DryRun),
                ctx.GetCancellationToken());
            result.Match(
                value =>
                {
                    RtLog.Info("Converting '{TypeName}' to a positional record...", request.TypeName);
                    foreach (var diagnostic in value.Diagnostics.Where(diagnostic =>
                                 diagnostic.Severity == MutationDiagnosticSeverity.Warning))
                        RtLog.Warning("{Message}", diagnostic.Message);
                    RtResultRenderer.Write(
                        RtResult<MutationExecutionResult<ConvertToRecordResult>>.Ok(value));
                },
                error => RoslynTools.Cli.RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record ConvertToRecordCliRequest(
        string TypeName,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption
    {
        public static ConvertToRecordCliRequest FromFlags(RtFlagValues flags, string typeName)
        {
            return new ConvertToRecordCliRequest(typeName, flags.SolutionPath, flags.DryRun);
        }
    }
}
