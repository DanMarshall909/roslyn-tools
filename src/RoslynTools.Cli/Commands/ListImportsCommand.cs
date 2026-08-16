using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Lists all imported namespaces in the current file
/// </summary>
internal sealed class ListImportsCommand : IRtCommand, IAnalyzeCommand
{
    private readonly ListImportsOperation _operation;
    public ListImportsCommand(ListImportsOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => ListImportsCommandDefinition.Info;

    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to .sln/.slnx");
        var kindOpt = new Option<string>("--kind", () => "all", "Filter: all, global, static, or alias");

        var cmd = new Command("list-imports", "List using directives, including global and static imports");
        cmd.AddAlias("imports");
        cmd.Add(kindOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var kind = ctx.ParseResult.GetValueForOption(kindOpt);
            var req = binder.Bind(ctx.ParseResult, f => new ListImportsCliRequest(f.SolutionPath, kind!));
            var result = await _operation.ExecuteAsync(new ListImportsRequest(req.SolutionPath, req.ImportKind));
            RtResultRenderer.Write(RtResult<ImportDirectiveResult>.Ok(ImportDirectiveResult.FromImports(result.Imports)));
        });

        return cmd;
    }

    private sealed record ListImportsCliRequest(string? SolutionPath, string ImportKind) : ISupportsSolutionOption;
}
