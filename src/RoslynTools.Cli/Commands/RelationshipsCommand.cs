using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class RelationshipsCommand(RelationshipsOperation operation)
    : IRtCommand, IInspectCommand, IStructuredOutputCommand
{
    private readonly RelationshipsOperation _operation = operation;

    public static RtCommandInfo Info => RelationshipsCommandDefinition.Info;

    public Command Build()
    {
        var mode = new Argument<RelationshipMode>("mode", "References, callers, or implementations");
        var symbol = new Argument<string>("symbol", "Stable symbol identity, FQN, signature, or unambiguous name");
        var solution = new Option<string?>("--solution", "Path to .sln or .slnx");
        var kind = new Option<string?>("--kind", "Optional symbol kind filter");
        var includeTests = new Option<bool>("--include-tests", "Include callers from test projects");
        var limit = new Option<int>("--limit", () => 100, "Maximum relationship items (1-100)");
        var command = new Command("relationships", "Inspect bounded semantic relationships")
        {
            mode,
            symbol,
            solution,
            kind,
            includeTests,
            limit
        };

        command.SetHandler(async context =>
        {
            var request = new RelationshipsRequest(
                context.ParseResult.GetValueForArgument(symbol),
                context.ParseResult.GetValueForArgument(mode),
                context.ParseResult.GetValueForOption(solution),
                context.ParseResult.GetValueForOption(kind),
                context.ParseResult.GetValueForOption(includeTests),
                context.ParseResult.GetValueForOption(limit));
            var result = await _operation.ExecuteAsync(request, context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<RtResultEnvelope<RelationshipsResult>>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error));
        });
        return command;
    }
}
