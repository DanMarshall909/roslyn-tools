using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for source-related operations.
/// </summary>
internal sealed class SourceCommand : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly SourceOperation _operation;
    public SourceCommand(SourceOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => SourceCommandDefinition.Info;

    public Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified member or type name");
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file")
            .WithKind("Symbol kind hint: type, method, property, field");

        var cmd = new Command("source", "Print the source code of a member or type by FQN (JSON)")
        {
            symbolArg
        };
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var request = flags.Bind(ctx.ParseResult, f => SourceCliRequest.FromFlags(f, symbol));
                var source = await _operation.ExecuteAsync(new SourceRequest(
                    request.Symbol, request.SolutionPath, request.Kind));

                RtResultRenderer.Write(RtResult<object>.Ok(source));
            });

        return cmd;
    }

    private sealed record SourceCliRequest(
        string Symbol,
        string? SolutionPath,
        string? Kind) : ISupportsSolutionOption, ISupportsKindOption
    {
        public static SourceCliRequest FromFlags(RtFlagValues flags, string symbol)
        {
            return new SourceCliRequest(symbol, flags.SolutionPath, flags.Kind);
        }
    }
}
