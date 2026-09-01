using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for source-related operations.
/// </summary>
internal sealed class SourceCommand(
    DaemonRpcRecoveryCoordinator daemon,
    SourceOperation operation) : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly SourceOperation _operation = operation;
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;

    public static RtCommandInfo Info => SourceApplicationDefinition.Info;

    public Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified member or type name");
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file")
            .WithKind("Symbol kind hint: type, method, property, field");
        var includeRelatedOpt = new Option<bool>(
            "--include-related",
            "Include one-hop source declarations for the related symbols listed at the bottom");
        var relatedLimitOpt = new Option<int>(
            "--related-limit",
            () => 10,
            "Maximum related symbols to list (1-100)");

        var cmd = new Command("source", "Print a symbol's source and its related source symbols (JSON)")
        {
            symbolArg,
            includeRelatedOpt,
            relatedLimitOpt
        };
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
            var includeRelated = ctx.ParseResult.GetValueForOption(includeRelatedOpt);
            var relatedLimit = ctx.ParseResult.GetValueForOption(relatedLimitOpt);
            var request = flags.Bind(
                ctx.ParseResult,
                f => SourceCliRequest.FromFlags(
                    f,
                    symbol,
                    includeRelated,
                    relatedLimit));
            var source = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new SourceRequest(
                    request.Symbol,
                    request.SolutionPath,
                    request.Kind,
                    request.IncludeRelated,
                    request.RelatedLimit),
                ctx.GetCancellationToken());

            source.Match(
                value => RtResultRenderer.Write(RtResult<SourceResult>.Ok(value)),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private sealed record SourceCliRequest(
        string Symbol,
        string? SolutionPath,
        string? Kind,
        bool IncludeRelated,
        int RelatedLimit) : ISupportsSolutionOption, ISupportsKindOption
    {
        public static SourceCliRequest FromFlags(
            RtFlagValues flags,
            string symbol,
            bool includeRelated,
            int relatedLimit)
        {
            return new SourceCliRequest(
                symbol,
                flags.SolutionPath,
                flags.Kind,
                includeRelated,
                relatedLimit);
        }
    }
}
