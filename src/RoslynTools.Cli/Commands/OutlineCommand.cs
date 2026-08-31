using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Provides an outline of the solution's structure and elements.
/// </summary>
internal sealed class OutlineCommand(
    DaemonRpcRecoveryCoordinator daemon,
    OutlineOperation operation) : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly OutlineOperation _operation = operation;

    public static RtCommandInfo Info => OutlineCommandDefinition.Info;

    public Command Build()
    {
        var targetArg = new Argument<string?>("target", () => null,
            "File path, directory, glob pattern, or namespace to outline");
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file");
        var regexOpt = new Option<string?>("--regex", "Regex pattern matched against solution file paths");
        var summaryOpt = new Option<bool>("--summary",
            "Compact mode: one entry per top-level type (fqn, kind, lines, description if present); no member detail");
        var withLocationsOpt = new Option<bool>("--with-locations",
            "Legacy verbose mode: grouped members with per-member line ranges, instead of the compact signature array");

        var cmd = new Command("outline",
            "List types as a compact signature array (or verbose with --with-locations) in a file, glob, namespace, or regex (JSON)")
        {
            targetArg, regexOpt, summaryOpt, withLocationsOpt
        };
        binder.AddTo(cmd);

        cmd.SetHandler(async context =>
        {
            var target = context.ParseResult.GetValueForArgument(targetArg);
            var regex = context.ParseResult.GetValueForOption(regexOpt);
            var summary = context.ParseResult.GetValueForOption(summaryOpt);
            var withLocations = context.ParseResult.GetValueForOption(withLocationsOpt);
            var flags = binder.Bind(context.ParseResult, f => new OutlineCliRequest(f.SolutionPath));
            var request = new OutlineRequest(
                target,
                regex,
                flags.SolutionPath,
                summary,
                withLocations);
            var result = await DaemonCommandExecutor.ExecuteTypedAsync(
                _daemon,
                _operation,
                request,
                context.GetCancellationToken());
            result.Match(
                value => Render(value),
                error => RtErrorPresenter.Surface(context, error));
        });

        return cmd;
    }

    private static void Render(RtResultEnvelope<OutlineResult> result)
    {
        RtResultRenderer.Write(RtResult<RtResultEnvelope<OutlineResult>>.Ok(result));
    }

    private sealed record OutlineCliRequest(string? SolutionPath) : ISupportsSolutionOption;
}
