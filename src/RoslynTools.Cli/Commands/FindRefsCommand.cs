using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all references to a given symbol in the current file
/// </summary>
internal sealed class FindRefsCommand(FindRefsOperation operation) : IRtCommand, IInspectCommand
{
    private readonly FindRefsOperation _operation = operation;

    public static RtCommandInfo Info { get; } = new(
        "find-refs",
        false,
        false,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.",
        Examples:
        [
            "rt inspect find-refs --symbol BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol CreateBooking --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx"
        ],
        SeeAlso: ["find-callers", "find-impl", "rename"],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

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
            var result = await _operation.ExecuteAsync(new FindRefsRequest(
                request.Symbol!,
                request.SolutionPath,
                request.Kind));
            RtResultRenderer.Write(RtResult<FindRefsResult>.Ok(result));
        });

        return cmd;
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
