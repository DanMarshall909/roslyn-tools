using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Adds DynamicDependency annotations for reflectively-created types.</summary>
internal sealed class AddDynamicDependencyCommand(
    DaemonRpcRecoveryCoordinator daemon,
    AddDynamicDependencyOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly AddDynamicDependencyOperation _operation = operation;

    public static RtCommandInfo Info => AddDynamicDependencyCommandDefinition.Info;

    public Command Build()
    {
        var typeOption = new Option<string[]>("--type", "Fully-qualified implementation type to annotate")
        {
            IsRequired = true,
            AllowMultipleArgumentsPerToken = false
        };
        var rootOption = new Option<string>("--root", "Composition-root method in Type.Method form")
        {
            IsRequired = true
        };
        var binder = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithDryRun();

        var command = new Command(
            "add-dynamic-dependency",
            "Add DynamicDependency attributes to a composition-root method");
        command.Add(typeOption);
        command.Add(rootOption);
        binder.AddTo(command);

        command.SetHandler(async context =>
        {
            var request = binder.Bind(context.ParseResult, flags => new Request(
                context.ParseResult.GetValueForOption(typeOption) ?? [],
                context.ParseResult.GetValueForOption(rootOption)!,
                flags.SolutionPath,
                flags.DryRun));

            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new AddDynamicDependencyRequest(
                    request.Types.ToArray(),
                    request.Root,
                    request.SolutionPath,
                    request.DryRun),
                context);
        });

        return command;
    }

    private sealed record Request(
        IReadOnlyCollection<string> Types,
        string Root,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption;
}
