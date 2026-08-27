using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding XML documentation to the project.
/// </summary>
internal sealed class AddXmlDocCommand(
    DaemonRpcRecoveryCoordinator daemon,
    AddXmlDocOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly AddXmlDocOperation _operation = operation;

    public static RtCommandInfo Info => AddXmlDocCommandDefinition.Info;

    public Command Build()
    {
        var fromJsonOpt = new Option<FileInfo>("--from-json", "JSON file mapping FQN → summary string") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("add-xml-doc", "Insert XML <summary> doc comments from a JSON map");
        cmd.AddAlias("xmldoc");
        cmd.AddAll(fromJsonOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async context =>
        {
            var fromJson = context.ParseResult.GetValueForOption(fromJsonOpt)!;
            var request = new AddXmlDocRequest(
                fromJson.FullName,
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(dryRunOpt),
                Directory.GetCurrentDirectory());
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                request,
                context);
        });

        return cmd;
    }
}
