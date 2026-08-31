using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Replaces an old call with a new implementation or method.
/// </summary>
internal sealed class ReplaceNewCommand : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon;
    private readonly ReplaceNewOperation _operation;

    public ReplaceNewCommand(
        DaemonRpcRecoveryCoordinator daemon,
        ReplaceNewOperation operation)
    {
        _daemon = daemon;
        _operation = operation;
    }

    public static RtCommandInfo Info => ReplaceNewCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Type name to replace, e.g. Option<string> or Option<string?>")
        { IsRequired = true };
        var withOpt = new Option<string>("--with", "Replacement: method name or factory call") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview changes without writing to disk (default)");
        var ifArgIndexOpt = new Option<int?>("--if-arg",
            "Filter: only replace calls where argument at this index matches --if-arg-literal");
        var ifArgLiteralOpt = new Option<string?>("--if-arg-literal", "Filter: match this string literal value");
        var dropMatchedArgOpt = new Option<bool>("--drop-matched-arg",
            "Drop the matched --if-arg argument from the replacement call (remaining args are forwarded)");

        var cmd = new Command("replace-new", "Replace all constructor invocations of a type with a method call")
        {
            typeOpt, withOpt, solutionOpt, dryRunOpt, ifArgIndexOpt, ifArgLiteralOpt, dropMatchedArgOpt
        };
        cmd.AddAlias("rn");

        cmd.SetHandler(async context =>
        {
            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new ReplaceNewRequest(
                    context.ParseResult.GetValueForOption(typeOpt)!,
                    context.ParseResult.GetValueForOption(withOpt)!,
                    context.ParseResult.GetValueForOption(ifArgIndexOpt),
                    context.ParseResult.GetValueForOption(ifArgLiteralOpt),
                    context.ParseResult.GetValueForOption(dropMatchedArgOpt),
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }

}
