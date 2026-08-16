using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all implementations of a given symbol in the current file
/// </summary>
internal sealed class FindImplementationsCommand : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly FindImplementationsOperation _operation;
    public FindImplementationsCommand(FindImplementationsOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => FindImplementationsCommandDefinition.Info;

    public Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified interface or abstract class name");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");

        var cmd = new Command("find-impl", "Find all implementations of an interface or abstract class (JSON)")
        {
            symbolArg, solutionOpt
        };
        cmd.AddAlias("impls");

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
                var result = await _operation.ExecuteAsync(new FindImplementationsRequest(symbol, solution));

                if (result.SymbolKind != "Interface" && !result.IsAbstract)
                    RtLog.Warning("'{Symbol}' is not an interface or abstract class. Searching for derived types.",
                        symbol);
                RtResultRenderer.Write(RtResult<object>.Ok(result));
            });

        return cmd;
    }
}
