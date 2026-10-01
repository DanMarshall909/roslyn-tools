using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Moves types or namespaces through the shared typed operation.</summary>
internal sealed class MoveCommand(
    DaemonRpcRecoveryCoordinator daemon,
    MoveOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly MoveOperation _operation = operation;

    public static RtCommandInfo Info => MoveCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var typeOpt = new Option<string?>("--type",
            "Fully-qualified type name (e.g. ProjectX.Core.Services.BookingService)");
        var namespaceOpt = new Option<string?>("--namespace",
            "Source namespace root; descendant namespaces are included");
        var toNamespaceOpt = new Option<string?>("--to-namespace",
            "Target namespace. Optional when --to-project preserves the existing namespace.");
        var toProjectOpt = new Option<string?>("--to-project",
            "Target project for a type-to-project move");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview changes without writing to disk (default)");
        var moveFileOpt = new Option<bool>("--move-file",
            "Move declaration files to the target namespace folder for type or namespace moves; cannot be combined with --to-project");

        var command = new Command(
            "move",
            "Move a type or namespace with structured scope and prepared filesystem changes")
        {
            solutionOpt,
            typeOpt,
            namespaceOpt,
            toNamespaceOpt,
            toProjectOpt,
            dryRunOpt,
            moveFileOpt
        };

        command.SetHandler(async context =>
        {
            var type = context.ParseResult.GetValueForOption(typeOpt);
            var sourceNamespace = context.ParseResult.GetValueForOption(namespaceOpt);
            var targetProject = context.ParseResult.GetValueForOption(toProjectOpt);
            var mode = sourceNamespace is not null
                ? MoveMode.NamespaceToNamespace
                : targetProject is not null
                    ? MoveMode.TypeToProject
                    : MoveMode.TypeToNamespace;
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new MoveRequest(
                    mode,
                    type,
                    sourceNamespace,
                    context.ParseResult.GetValueForOption(toNamespaceOpt),
                    targetProject,
                    context.ParseResult.GetValueForOption(moveFileOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return command;
    }
}
