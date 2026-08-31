using System.CommandLine;
using RoslynTools.Cli;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class DoctorCommand(DoctorOperation operation)
    : IRtCommand, IInspectCommand, IStructuredOutputCommand
{
    private readonly DoctorOperation _operation = operation;

    public static RtCommandInfo Info => DoctorCommandDefinition.Info;

    public Command Build()
    {
        var directory = new Option<DirectoryInfo>(
            "--directory",
            () => new DirectoryInfo(System.IO.Directory.GetCurrentDirectory()),
            "Absolute repository or workspace directory");
        var solution = new Option<string?>(
            "--solution",
            "Explicit .sln or .slnx path; relative paths resolve under --directory");
        var command = new Command(
            "doctor",
            "Check installation, solution selection, trust, workspace readiness, and limits")
        {
            directory,
            solution
        };

        command.SetHandler(async context =>
        {
            var request = new DoctorRequest(
                context.ParseResult.GetValueForOption(directory)!.FullName,
                context.ParseResult.GetValueForOption(solution));
            var result = await _operation.ExecuteAsync(request, context.GetCancellationToken());
            result.Match(
                value => RtResultRenderer.Write(RtResult<RtResultEnvelope<DoctorResult>>.Ok(value)),
                error => RtErrorPresenter.Surface(context, error));
        });

        return command;
    }
}
