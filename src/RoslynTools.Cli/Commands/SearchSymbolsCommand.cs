using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class SearchSymbolsCommand(
    DaemonRpcRecoveryCoordinator daemon,
    SearchSymbolsOperation operation)
    : IRtCommand, IInspectCommand, IStructuredOutputCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly SearchSymbolsOperation _operation = operation;

    public static RtCommandInfo Info => SearchSymbolsCommandDefinition.Info;

    public Command Build()
    {
        var query = new Argument<string>("query", "Exact or partial symbol name, FQN, or signature text");
        var solution = new Option<string?>("--solution", "Path to .sln or .slnx");
        var kind = new Option<string?>("--kind", "Symbol kind, for example NamedType, Method, Property, Field, or Event");
        var project = new Option<string?>("--project", "Exact project name");
        var @namespace = new Option<string?>("--namespace", "Exact namespace");
        var accessibility = new Option<string?>("--accessibility", "Declared accessibility");
        var genericArity = new Option<int?>("--generic-arity", "Exact generic arity");
        var signature = new Option<string?>("--signature", "Required signature substring");
        var limit = new Option<int>("--limit", () => 50, "Maximum candidates (1-100)");
        var fuzzy = new Option<bool>("--fuzzy", "Enable explicit subsequence matching after exact and partial matches");
        var command = new Command("search-symbols", "Find and rank typed symbol candidates")
        {
            query,
            solution,
            kind,
            project,
            @namespace,
            accessibility,
            genericArity,
            signature,
            limit,
            fuzzy
        };

        command.SetHandler(async context =>
        {
            var request = new SearchSymbolsRequest(
                context.ParseResult.GetValueForArgument(query),
                context.ParseResult.GetValueForOption(solution),
                context.ParseResult.GetValueForOption(kind),
                context.ParseResult.GetValueForOption(project),
                context.ParseResult.GetValueForOption(@namespace),
                context.ParseResult.GetValueForOption(accessibility),
                context.ParseResult.GetValueForOption(genericArity),
                context.ParseResult.GetValueForOption(signature),
                context.ParseResult.GetValueForOption(limit),
                context.ParseResult.GetValueForOption(fuzzy));
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                request,
                context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<RtResultEnvelope<SearchSymbolsResult>>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error));
        });

        return command;
    }
}
