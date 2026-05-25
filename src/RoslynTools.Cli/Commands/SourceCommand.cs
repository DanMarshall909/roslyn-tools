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
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;
    public SourceCommand(IWorkspaceSessionFactory sessionFactory, InspectService inspect)
    {
        _inspect = inspect;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "source",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Reading the full source of a specific method, property, type, or field by FQN without opening the file. Returns the declaration including XML doc comments, with file path and line numbers. Prefer this over Read when you know the exact member you need.",
        Examples:
        [
            "rt inspect source RoslynTools.Services.OutlineService.BuildTypeSummary --solution RoslynTools.slnx",
            "rt inspect source RoslynTools.Core.WorkspaceLoader --solution RoslynTools.slnx",
            "rt inspect source ProjectX.Core.Services.BookingService.CreateBookingAsync --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public Command Build()
    {
        var symbolArg = new Argument<string>("symbol", "Fully-qualified member or type name");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var kindOpt = new Option<string?>("--kind", "Symbol kind hint: type, method, property, field");

        var cmd = new Command("source", "Print the source code of a member or type by FQN (JSON)")
        {
            symbolArg, solutionOpt, kindOpt
        };

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
                var kind = ctx.ParseResult.GetValueForOption(kindOpt);
                var sln = await _sessionFactory.GetSolutionAsync(solution);
                var sym = await SymbolLocator.FindSymbolByFqnAsync(sln, symbol, kind);
                var source = await _inspect.GetSourceAsync(sym);

                RtResultRenderer.Write(
                    RtResult<object>.Ok(source),
                    SymbolFormatter.DefaultJsonOptions);
            });

        return cmd;
    }
}
