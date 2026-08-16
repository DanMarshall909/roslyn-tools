using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for adding dependencies to a solution.
/// </summary>
internal sealed class AddDependencyCommand : IRtCommand, IRefactorCommand
{
    private readonly AddDependencyOperation _operation;
    public AddDependencyCommand(AddDependencyOperation operation)
    {
        _operation = operation;
    }

    public static RtCommandInfo Info => AddDependencyCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string>("--type", "Target class name") { IsRequired = true };
        var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)")
        { IsRequired = true };
        var fieldOpt =
            CommandOptionFactory.CreateFieldOption("--field", "Field name override (default: derived from type)");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
        cmd.AddAlias("ad");
        cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(
            async (type, dep, field, solution, dryRun) => { await ExecuteAsync(type, dep, field, solution, dryRun); },
            typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the add dependency command asynchronously.
    /// </summary>
    internal async Task ExecuteAsync(string typeName, string depType, string? fieldName, string? solutionPath,
        bool dryRun)
    {
        var result = await _operation.ExecuteAsync(
            new AddDependencyRequest(typeName, depType, fieldName, solutionPath, dryRun));
        RtResultRenderer.Write(RtResult<MutationExecutionResult<AddDependencyResult>>.Ok(result));
    }
}
