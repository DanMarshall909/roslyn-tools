using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>A thin CLI adapter for safe symbol deletion.</summary>
internal sealed class SafeDeleteCommand(
    DaemonRpcRecoveryCoordinator daemon,
    SafeDeleteOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly SafeDeleteOperation _operation = operation;

    public static RtCommandInfo Info => SafeDeleteCommandDefinition.Info;

    public Command Build()
    {
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx")
            .WithDryRun("Preview without writing")
            .WithSymbol("Type or member FQN or simple name to delete")
            .WithKind("Kind of symbol: type, method, or property");

        var command = new Command("safe-delete", "Delete a type or member only if it has no references");
        command.AddAlias("sd");
        flags.AddTo(command);

        command.SetHandler(async context =>
        {
            var values = flags.Bind(context.ParseResult, SafeDeleteCliRequest.FromFlags);
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new SafeDeleteRequest(
                    values.Symbol!,
                    values.Kind,
                    values.SolutionPath,
                    values.DryRun),
                context);
        });

        return command;
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
}
