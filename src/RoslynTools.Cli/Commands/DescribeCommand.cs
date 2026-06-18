using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Suggest;

namespace RoslynTools.Commands;

/// <summary>
/// Provides a description of a command or its usage.
/// </summary>
internal sealed class DescribeCommand : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly InspectService _inspect;
    public DescribeCommand(IWorkspaceSessionFactory sessionFactory, InspectService inspect)
    {
        _inspect = inspect;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "describe",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Getting a type's surface area as a compact JSON signature array { fqn, kind, signatures: [...] } — one canonical signature per member, no line numbers (stable across edits). Drill into a single member by passing its signature ('Type.Method(paramType, ...)') for full detail. Add --with-locations for the legacy verbose shape (per-member line numbers + locations).",
        Examples:
        [
            "rt inspect describe BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect describe \"RoslynTools.Core.SymbolFormatter.ExtractMembers(INamedTypeSymbol, bool)\" --solution RoslynTools.slnx",
            "rt inspect describe IRtCommand --solution RoslynTools.slnx --with-locations"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public Command Build()
    {
        var symbolArg = new Argument<string>("symbol",
            "Fully-qualified type name, or a member signature ('Type.Method(paramType, ...)') to drill in");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");
        var withLocationsOpt = new Option<bool>("--with-locations",
            "Restore the legacy verbose shape (per-member line numbers + locations) instead of the compact signature array");

        var cmd = new Command("describe",
            "Show a type's compact signature array, or drill into a member by signature (JSON)")
        {
            symbolArg, solutionOpt, kindOpt, withLocationsOpt
        };

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
                var kind = ctx.ParseResult.GetValueForOption(kindOpt);
                var withLocations = ctx.ParseResult.GetValueForOption(withLocationsOpt);

                var sln = await _sessionFactory.GetSolutionAsync(solution);
                var inspection = await _inspect.Describe(sln, symbol, kind, withLocations);

                inspection.Match(
                    result =>
                    {
                        if (result is CompactDescription cd)
                            result = cd with { Suggestions = SuggestionHelper.ForDescribe(cd, solution) };
                        RtResultRenderer.Write(RtResult<object>.Ok(result));
                    },
                    error => RtErrorPresenter.Surface(ctx, error));
            });

        return cmd;
    }
}
