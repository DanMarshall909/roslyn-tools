using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Thin CLI adapter for dependency narrowing.</summary>
internal sealed class NarrowDependencyCommand(
    DaemonRpcRecoveryCoordinator daemon,
    NarrowDependencyOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly NarrowDependencyOperation _operation = operation;

    public static RtCommandInfo Info => NarrowDependencyCommandDefinition.Info;

    public Command Build()
    {
        var interfaceArg = new Argument<string>("interface", "Source interface to extract members from");
        var membersOpt = new Option<string[]>("--members", "Comma-separated or repeated member names to extract")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = true
        };
        var newNameOpt = CommandOptionFactory.CreateStringOption("--new-name", "Name for the new narrower interface");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var command = new Command(
            "narrow-dependency",
            "Extract a member subset into a new interface and narrow consumers that only use that subset");
        command.AddAlias("nd");
        command.Add(interfaceArg);
        command.AddAll(membersOpt, newNameOpt, solutionOpt, dryRunOpt);

        command.SetHandler(async context =>
        {
            var members = context.ParseResult.GetValueForOption(membersOpt)!
                .SelectMany(member => member.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .ToArray();
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new NarrowDependencyRequest(
                    context.ParseResult.GetValueForArgument(interfaceArg),
                    members,
                    context.ParseResult.GetValueForOption(newNameOpt)!,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return command;
    }
}
