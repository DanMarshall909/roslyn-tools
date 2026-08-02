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
    private readonly DescribeOperation _operation;

    public DescribeCommand(DescribeOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info { get; } = new(
        "describe",
        false,
        true,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Getting a type's surface area as a compact JSON signature array { fqn, kind, signatures: [...] } — one canonical signature per member, no line numbers (stable across edits). Prefer this over Read when you need one type's or member's signature rather than the whole file. Drill into a single member by passing its signature ('Type.Method(paramType, ...)') for full detail. Add --with-locations for the legacy verbose shape (per-member line numbers + locations).",
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
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file")
            .WithKind("Symbol kind: type, method, property, field, namespace");
        var withLocationsOpt = new Option<bool>("--with-locations",
            "Restore the legacy verbose shape (per-member line numbers + locations) instead of the compact signature array");

        var cmd = new Command("describe",
            "Show a type's compact signature array, or drill into a member by signature (JSON)")
        {
            symbolArg, withLocationsOpt
        };
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var withLocations = ctx.ParseResult.GetValueForOption(withLocationsOpt);
                var req = binder.Bind(ctx.ParseResult,
                    f => new DescribeCliRequest(symbol, f.SolutionPath, f.Kind, withLocations));

                var inspection = await _operation.ExecuteAsync(new DescribeRequest(
                    req.Symbol,
                    req.SolutionPath,
                    req.Kind,
                    req.WithLocations));

                inspection.Match(
                    result =>
                    {
                        if (result is CompactDescription cd)
                            result = cd with { Suggestions = SuggestionHelper.ForDescribe(cd, req.SolutionPath) };
                        RtResultRenderer.Write(RtResult<object>.Ok(result));
                    },
                    error => RtErrorPresenter.Surface(ctx, error));
            });

        return cmd;
    }

    private sealed record DescribeCliRequest(
        string Symbol,
        string? SolutionPath,
        string? Kind,
        bool WithLocations) : ISupportsSolutionOption, ISupportsKindOption;
}
