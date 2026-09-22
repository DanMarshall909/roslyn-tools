using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class CiCheckCommand(CiCheckOperation operation)
    : IRtCommand, ICiCommand, IStructuredOutputCommand
{
    private readonly CiCheckOperation _operation = operation;

    public static RtCommandInfo Info => CiCheckCommandDefinition.Info;

    public Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var configOpt = new Option<string>("--config", () => ".rt/ci.json",
            "Checked-in CI policy path relative to the solution directory");
        var command = new Command(Info.Name, "Enforce configured rules and fail when any check fails");
        command.AddAll(solutionOpt, configOpt);
        command.SetHandler(async context =>
        {
            var result = await _operation.ExecuteAsync(new CiCheckRequest(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(configOpt)!), context.GetCancellationToken());
            result.Match(
                value =>
                {
                    RtResultRenderer.Write(RtResult<CiCheckResult>.Ok(value));
                    context.ExitCode = value.Passed ? 0 : 1;
                },
                error => RtErrorPresenter.Surface(context, error));
        });
        return command;
    }
}
