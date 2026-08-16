using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all occurrences of a given pattern in the current file
/// </summary>
internal sealed class FindPatternCommand : IRtCommand, IStructuredOutputCommand, IInspectCommand
{
    private readonly FindPatternOperation _operation;

    public FindPatternCommand(FindPatternOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => FindPatternCommandDefinition.Info;

    public Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file containing the pattern.")
        { IsRequired = true };
        var startLineOpt = new Option<int>("--start-line", "First line of the pattern range (1-based).")
        { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the pattern range (1-based, inclusive).")
        { IsRequired = true };
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx. Auto-detected if omitted.")
            .WithScope("Filter matches to documents in this namespace prefix or project name.");

        var cmd = new Command("find-pattern",
            "Find all solution locations structurally matching a given statement range.");
        cmd.AddAlias("fp");
        cmd.AddAll(fileOpt, startLineOpt, endLineOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var file = ctx.ParseResult.GetValueForOption(fileOpt)!;
            var startLine = ctx.ParseResult.GetValueForOption(startLineOpt);
            var endLine = ctx.ParseResult.GetValueForOption(endLineOpt);
            var req = binder.Bind(ctx.ParseResult, f => new FindPatternCliRequest(f.SolutionPath, f.Scope));
            RtLog.Info("Searching for pattern at {File}:{StartLine}-{EndLine}...", file, startLine, endLine);
            try
            {
                var result = await _operation.ExecuteAsync(new FindPatternRequest(
                    file, startLine, endLine, req.SolutionPath, req.Scope));
                RtResultRenderer.Write(RtResult<object>.Ok(result));
            }
            catch (InvalidOperationException ex)
            {
                RtLog.Error("Error: {Message}", ex.Message);
                Environment.ExitCode = 1;
                return;
            }
        });

        return cmd;
    }

    private sealed record FindPatternCliRequest(string? SolutionPath, string? Scope)
        : ISupportsSolutionOption, ISupportsScopeOption;
}
