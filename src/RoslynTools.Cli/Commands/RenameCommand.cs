using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Renames symbols within the solution.
/// </summary>
internal sealed class RenameCommand(
    DaemonRpcRecoveryCoordinator daemon,
    RenameOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly RenameOperation _operation = operation;

    public static RtCommandInfo Info => RenameCommandDefinition.Info;

    public Command Build()
    {
        var symbolArg = new Argument<string?>("symbol", () => null, "Fully-qualified symbol name to rename.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var newNameArg = new Argument<string?>("new-name", () => null, "New name for the symbol.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file. Defaults to the single solution in current directory.")
            .WithDryRun("Preview changes without writing to disk")
            .WithKind("Symbol kind: type, method, property, field, namespace");
        var csvOpt = new Option<string?>("--csv",
            "Path to a CSV file with source_symbol/target_name, or prefixed *_fqn plus *_name_new columns.");
        var newNameOpt = new Option<string?>("--new-name",
            "New name for the symbol. Use this when the symbol is supplied via piped stdin JSON.");
        var relatedEditOpt = new Option<string[]>(
            "--related-edit",
            "Related edit ID returned by a previous preview. Repeat to select multiple edits.")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var cmd = new Command("rename", "Rename a symbol and all its references across the solution")
        {
            symbolArg, newNameArg, csvOpt, newNameOpt, relatedEditOpt
        };
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var newName = ctx.ParseResult.GetValueForOption(newNameOpt)
                              ?? ctx.ParseResult.GetValueForArgument(newNameArg);
                var csv = ctx.ParseResult.GetValueForOption(csvOpt);
                var relatedEditIds = ctx.ParseResult.GetValueForOption(relatedEditOpt) ?? [];
                if (string.IsNullOrWhiteSpace(csv))
                    symbol = await PipedSymbolReader.ResolveAsync(symbol, RtConsole.IsInputRedirected, RtConsole.In);
                var request = flags.Bind(ctx.ParseResult, f => RenameCliRequest.FromFlags(f, symbol, newName, csv));
                var mode = string.IsNullOrWhiteSpace(request.CsvPath)
                    ? RenameOperationMode.Single
                    : RenameOperationMode.Batch;
                await DaemonCommandExecutor.ExecuteAsync(
                    _daemon,
                    _operation,
                    new RenameOperationRequest(
                        mode,
                        request.Symbol,
                        request.NewName,
                        request.CsvPath,
                        request.Kind,
                        request.SolutionPath,
                        request.DryRun,
                        relatedEditIds,
                        Directory.GetCurrentDirectory()),
                    ctx);
            });

        cmd.AddAlias("ren");

        return cmd;
    }

    private sealed record RenameCliRequest(
        string? Symbol,
        string? NewName,
        string? CsvPath,
        string? SolutionPath,
        string? Kind,
        bool DryRun) : ISupportsSolutionOption, ISupportsKindOption, ISupportsDryRunOption
    {
        public static RenameCliRequest FromFlags(
            RtFlagValues flags,
            string? symbol,
            string? newName,
            string? csvPath)
        {
            return new RenameCliRequest(
                symbol,
                newName,
                csvPath,
                flags.SolutionPath,
                flags.Kind,
                flags.DryRun);
        }
    }
}
