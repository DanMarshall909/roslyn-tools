using System.CommandLine;
using System.Text.Json;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Provides a description of a command or its usage.
/// </summary>
internal sealed class DescribeCommand(
    DaemonRpcRecoveryCoordinator daemon,
    DescribeOperation operation)
    : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly DescribeOperation _operation = operation;

    public static RtCommandInfo Info => DescribeApplicationDefinition.Info;

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

            var inspection = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                new DescribeRequest(
                    req.Symbol,
                    req.SolutionPath,
                    req.Kind,
                    req.WithLocations),
                ctx.GetCancellationToken());

            inspection.Match(
                Render,
                error => RtErrorPresenter.Surface(ctx, error));
        });

        return cmd;
    }

    private static void Render(DescribeResult result)
    {
        if (result.Kind == DescribeResultKind.Compact && result.Compact is not null)
        {
            RtResultRenderer.Write(RtResult<CompactDescription>.Ok(result.Compact));
            return;
        }

        if (result.Kind == DescribeResultKind.Verbose && result.Verbose is { } verbose)
        {
            RtResultRenderer.Write(RtResult<JsonElement>.Ok(verbose));
            return;
        }

        throw new InvalidOperationException("The describe result did not contain its declared value.");
    }

    private sealed record DescribeCliRequest(
        string Symbol,
        string? SolutionPath,
        string? Kind,
        bool WithLocations) : ISupportsSolutionOption, ISupportsKindOption;
}
