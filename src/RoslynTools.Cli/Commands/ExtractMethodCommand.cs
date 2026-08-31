using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Extracts methods from the current file
/// </summary>
internal sealed class ExtractMethodCommand(
    DaemonRpcRecoveryCoordinator daemon,
    ExtractMethodOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly ExtractMethodOperation _operation = operation;

    public static RtCommandInfo Info => ExtractMethodCommandDefinition.Info;

    public Command Build()
    {
        var fileArg = new Argument<string>("file", "Path to the .cs file containing the method to extract from.");
        var newNameArg = new Argument<string>("new-name", "Name for the extracted method.");

        var startLineOpt = new Option<int>("--start-line", "First line of the statements to extract (1-based).")
        { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the statements to extract (1-based, inclusive).")
        { IsRequired = true };
        var afterOpt = new Option<string?>("--after",
            "Insert the new method after this member name. Default: after the containing method.");
        var beforeOpt = new Option<string?>("--before", "Insert the new method before this member name.");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln file. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", () => true, "Preview changes without writing to disk (default).");
        var replaceDuplicatesOpt = new Option<bool>("--replace-duplicates",
            "After extraction, find structurally-matching code elsewhere in the solution and replace it with calls to the new method.");

        var cmd = new Command("extract-method", "Extract a range of statements into a new private method.")
        {
            fileArg, newNameArg, startLineOpt, endLineOpt, afterOpt, beforeOpt, solutionOpt, dryRunOpt,
            replaceDuplicatesOpt
        };
        cmd.AddAlias("em");

        cmd.SetHandler(async ctx =>
        {
            var request = new ExtractMethodRequest(
                ctx.ParseResult.GetValueForArgument(fileArg),
                ctx.ParseResult.GetValueForArgument(newNameArg),
                ctx.ParseResult.GetValueForOption(startLineOpt),
                ctx.ParseResult.GetValueForOption(endLineOpt),
                ctx.ParseResult.GetValueForOption(afterOpt),
                ctx.ParseResult.GetValueForOption(beforeOpt),
                ctx.ParseResult.GetValueForOption(solutionOpt),
                ctx.ParseResult.GetValueForOption(dryRunOpt),
                ctx.ParseResult.GetValueForOption(replaceDuplicatesOpt));
            await DaemonCommandExecutor.ExecuteAsync(_daemon, _operation, request, ctx);
        });

        return cmd;
    }
}
