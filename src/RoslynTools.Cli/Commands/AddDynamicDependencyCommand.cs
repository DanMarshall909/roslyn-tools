using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Adds DynamicDependency annotations for reflectively-created types.</summary>
internal sealed class AddDynamicDependencyCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly AddDynamicDependencyService _service;

    public AddDynamicDependencyCommand(
        IWorkspaceSessionFactory sessionFactory,
        AddDynamicDependencyService service)
    {
        _sessionFactory = sessionFactory;
        _service = service;
    }

    public static RtCommandInfo Info { get; } = new(
        "add-dynamic-dependency",
        true,
        false,
        "refactor",
        WhenToUse:
        "Recording a durable static reference for types instantiated through reflection or assembly-scanning dependency injection, after reviewing probe-di findings.",
        Examples:
        [
            "rt refactor add-dynamic-dependency --type MyApp.Services.EmailSender --root Bootstrap.RegisterServices --solution MyApp.slnx",
            "rt refactor add-dynamic-dependency --type ServiceA --type ServiceB --root Bootstrap.RegisterServices --dry-run"
        ],
        SeeAlso: ["probe-di", "find-unused"]);

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

            var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(request.SolutionPath);
            using (workspace)
                await _service.RunAsync(solution, request.Types, request.Root, request.DryRun);
        });

        return command;
    }

    private sealed record Request(
        IReadOnlyCollection<string> Types,
        string Root,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption;
}
