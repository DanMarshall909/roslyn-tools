using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all references to a given symbol in the current file
/// </summary>
internal sealed class FindRefsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    FindRefsOperation operation) : IRtCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly FindRefsOperation _operation = operation;

    public static RtCommandInfo Info => FindRefsCommandDefinition.Info;

    public Command Build()
    {
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file")
            .WithSymbol("Simple name of the symbol")
            .WithKind("Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("find-refs", "Find all references to a symbol across the solution");
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var request = flags.Bind(ctx.ParseResult, FindRefsCliRequest.FromFlags);
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new FindRefsRequest(request.Symbol!, request.SolutionPath, request.Kind),
                ctx.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<FindRefsResult>.Ok(ToOutput(value))),
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private static FindRefsResult ToOutput(FindRefsOperationResult result)
    {
        return new FindRefsResult
        {
            SymbolName = result.SymbolName,
            FullyQualifiedName = result.FullyQualifiedName,
            References = result.References.Select(static reference => new ReferenceResult
            {
                FilePath = reference.FilePath,
                Line = reference.Line,
                LineText = reference.LineText
            }).ToList(),
            Suggestions = result.Suggestions
        };
    }

    private sealed record FindRefsCliRequest(
        string? SolutionPath,
        string? Symbol,
        string? Kind) : ISupportsSolutionOption, ISupportsSymbolOption, ISupportsKindOption
    {
        public static FindRefsCliRequest FromFlags(RtFlagValues flags)
        {
            return new FindRefsCliRequest(flags.SolutionPath, flags.Symbol, flags.Kind);
        }
    }
}
