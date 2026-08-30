using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding static imports to the project.
/// </summary>
internal sealed class AddStaticImportCommand(
    DaemonRpcRecoveryCoordinator daemon,
    AddStaticImportOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly AddStaticImportOperation _operation = operation;

    public static RtCommandInfo Info => AddStaticImportCommandDefinition.Info;

    public Command Build()
    {
        var fileOpt = CommandOptionFactory.CreateStringOption("--file", "Path to the .cs file to update");
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Fully qualified type to import statically");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();
        var simplifyOpt = new Option<bool>("--simplify",
            "After adding the directive, strip the `<Type>.` qualifier from references to --type in the same file.");
        var simplifyAllOpt = new Option<bool>("--simplify-all-static-imports",
            "Implies --simplify. Also strips qualifiers for every other type already imported via `using static` in the file.");

        var cmd = new Command("add-static-import", "Add a using static directive to a file");
        cmd.AddAlias("asi");
        cmd.AddAll(fileOpt, typeOpt, solutionOpt, dryRunOpt, simplifyOpt, simplifyAllOpt);

        cmd.SetHandler(async context =>
        {
            var file = context.ParseResult.GetValueForOption(fileOpt)!;
            var type = context.ParseResult.GetValueForOption(typeOpt)!;
            var solution = context.ParseResult.GetValueForOption(solutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOpt);
            var simplify = context.ParseResult.GetValueForOption(simplifyOpt);
            var simplifyAll = context.ParseResult.GetValueForOption(simplifyAllOpt);
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new AddStaticImportRequest(file, type, solution, dryRun, simplify, simplifyAll),
                context);
        });

        return cmd;
    }
}
