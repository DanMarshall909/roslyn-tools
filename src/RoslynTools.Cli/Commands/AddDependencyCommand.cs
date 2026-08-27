using System.CommandLine;
using System.CommandLine.Invocation;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding dependencies to a solution.
/// </summary>
internal sealed class AddDependencyCommand(DaemonRpcClient daemon) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcClient _daemon = daemon;

    public static RtCommandInfo Info => AddDependencyCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Target class name") { IsRequired = true };
        var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)")
        { IsRequired = true };
        var fieldOpt =
            CommandOptionFactory.CreateFieldOption("--field", "Field name override (default: derived from type)");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview without writing (default)");
        var applyOpt = new Option<bool>("--apply", "Apply the constructor change to disk");

        var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
        cmd.AddAlias("ad");
        cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt, applyOpt);

        cmd.SetHandler(async context =>
        {
            var request = new AddDependencyRequest(
                context.ParseResult.GetValueForOption(typeOpt)!,
                context.ParseResult.GetValueForOption(depOpt)!,
                context.ParseResult.GetValueForOption(fieldOpt),
                context.ParseResult.GetValueForOption(solutionOpt),
                !context.ParseResult.GetValueForOption(applyOpt));
            await ExecuteThroughDaemonAsync(request, context);
        });

        return cmd;
    }

    private async Task ExecuteThroughDaemonAsync(AddDependencyRequest request, InvocationContext context)
    {
        await DaemonCommandExecutor.ExecuteAsync(
            _daemon,
            AddDependencyCommandDefinition.Name,
            request,
            context);
    }
}
