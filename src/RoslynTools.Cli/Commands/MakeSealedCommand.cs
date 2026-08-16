using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a class to be sealed
/// </summary>
internal sealed class MakeSealedCommand : IRtCommand, IRefactorCommand
{
    private readonly MakeSealedOperation _operation;

    public MakeSealedCommand(MakeSealedOperation operation)
    {
        _operation = operation;
    }

    // Stryker disable all
    public static RtCommandInfo Info => MakeSealedCommandDefinition.Info;
    // Stryker restore all

    // Stryker disable all
    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption("--type", "Class to seal");
        var binder = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithDryRun();

        var cmd = new Command("make-sealed", "Add the sealed modifier to a class that has no derived types");
        cmd.AddAlias("ms");
        cmd.Add(typeOpt);
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var req = binder.Bind(ctx.ParseResult, f =>
                new MakeSealedCliRequest(ctx.ParseResult.GetValueForOption(typeOpt)!, f.SolutionPath, f.DryRun));
            var result = await ExecuteAsync(req.Type, req.SolutionPath, req.DryRun);
            RtResultRenderer.Write(RtResult<MutationExecutionResult<MakeSealedResult>>.Ok(result));
        });

        return cmd;
    }
    // Stryker restore all

    /// <summary>
    /// Executes the MakeSealedCommand with async parameters
    /// </summary>
    internal Task<MutationExecutionResult<MakeSealedResult>> ExecuteAsync(
        string typeName,
        string? solutionPath,
        bool dryRun)
    {
        return _operation.ExecuteAsync(new MakeSealedRequest(typeName, solutionPath, dryRun));
    }

    private sealed record MakeSealedCliRequest(
        string Type,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption;
}
