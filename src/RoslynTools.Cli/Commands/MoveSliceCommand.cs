using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Moves a reviewed architectural slice as one prepared transaction.</summary>
internal sealed class MoveSliceCommand(
    DaemonRpcRecoveryCoordinator daemon,
    MoveSliceOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly MoveSliceOperation _operation = operation;

    public static RtCommandInfo Info => MoveSliceCommandDefinition.Info;

    public Command Build()
    {
        var typeOpt = new Option<string[]>(
            "--type",
            "Fully-qualified source type. Repeat to move multiple types as one slice.")
        {
            AllowMultipleArgumentsPerToken = false
        };
        var namespaceOpt = new Option<string?>(
            "--namespace",
            "Recursive source namespace root. Mutually exclusive with --type.");
        var toProjectOpt = new Option<string>("--to-project", "Target project name")
        {
            IsRequired = true
        };
        var toNamespaceOpt = new Option<string>("--to-namespace", "Target namespace root")
        {
            IsRequired = true
        };
        var analyzeDependenciesOpt = new Option<bool>(
            "--analyze-dependencies",
            "Report related source-project types without adding them to the slice.");
        var updateProjectReferencesOpt = new Option<bool>(
            "--update-project-references",
            "Allow only the project-reference additions listed in the reviewed manifest.");
        var projectReferenceJustificationOpt = new Option<string?>(
            "--project-reference-justification",
            "Required justification when --update-project-references is enabled.");
        var addInternalsVisibleToOpt = new Option<string[]>(
            "--add-internals-visible-to",
            "Explicit Project=Assembly friend declaration. Repeat for multiple edits.")
        {
            AllowMultipleArgumentsPerToken = false
        };
        var internalsVisibleToJustificationOpt = new Option<string?>(
            "--internals-visible-to-justification",
            "Required justification when --add-internals-visible-to is supplied.");
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file")
            .WithDryRun("Preview the complete manifest without writing (default)");

        var command = new Command(
            "move-slice",
            "Move a coherent type or namespace slice across projects as one transaction")
        {
            typeOpt,
            namespaceOpt,
            toProjectOpt,
            toNamespaceOpt,
            analyzeDependenciesOpt,
            updateProjectReferencesOpt,
            projectReferenceJustificationOpt,
            addInternalsVisibleToOpt,
            internalsVisibleToJustificationOpt
        };
        flags.AddTo(command);

        command.SetHandler(async context =>
        {
            var request = flags.Bind(context.ParseResult, values => new MoveSliceCliRequest(
                context.ParseResult.GetValueForOption(typeOpt) ?? [],
                context.ParseResult.GetValueForOption(namespaceOpt),
                context.ParseResult.GetValueForOption(toProjectOpt)!,
                context.ParseResult.GetValueForOption(toNamespaceOpt)!,
                context.ParseResult.GetValueForOption(analyzeDependenciesOpt),
                context.ParseResult.GetValueForOption(updateProjectReferencesOpt),
                context.ParseResult.GetValueForOption(projectReferenceJustificationOpt),
                context.ParseResult.GetValueForOption(addInternalsVisibleToOpt) ?? [],
                context.ParseResult.GetValueForOption(internalsVisibleToJustificationOpt),
                values.SolutionPath,
                values.DryRun));

            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new MoveSliceRequest(
                    request.Types,
                    request.Namespace,
                    request.ToProject,
                    request.ToNamespace,
                    request.AnalyzeDependencies,
                    request.UpdateProjectReferences,
                    request.ProjectReferenceJustification,
                    request.AddInternalsVisibleTo,
                    request.InternalsVisibleToJustification,
                    request.SolutionPath,
                    request.DryRun),
                context);
        });

        return command;
    }

    private sealed record MoveSliceCliRequest(
        IReadOnlyList<string> Types,
        string? Namespace,
        string ToProject,
        string ToNamespace,
        bool AnalyzeDependencies,
        bool UpdateProjectReferences,
        string? ProjectReferenceJustification,
        IReadOnlyList<string> AddInternalsVisibleTo,
        string? InternalsVisibleToJustification,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption;
}
