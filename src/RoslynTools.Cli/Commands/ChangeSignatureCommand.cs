using System.CommandLine;
using RoslynTools.Commands.CommandHandlers;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Command for changing the signature of a command.
/// </summary>
internal sealed class ChangeSignatureCommand : IRtCommand, IRefactorCommand, IMutatingCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ChangeSignatureService _changeSignature;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    public ChangeSignatureCommand(IWorkspaceSessionFactory sessionFactory, ChangeSignatureService changeSignature, ISolutionChangeWriter solutionChangeWriter)
    {
        _solutionChangeWriter = solutionChangeWriter;
        _changeSignature = changeSignature;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "change-signature",
        true,
        false,
        "refactor",
        "Change method signatures and update affected call sites.",
        [
            "rt refactor change-signature rename-param \"MyApp.Services.OrderService.PlaceOrder\" oldName newName --solution MyApp.sln",
            "rt refactor change-signature reorder \"MyApp.Services.OrderService.PlaceOrder\" \"2,0,1\" --solution MyApp.sln",
            "rt refactor change-signature add-param \"MyApp.Services.OrderService.PlaceOrder\" \"int timeout = 30\" --after orderId --solution MyApp.sln",
            "rt refactor change-signature remove-param \"MyApp.Services.OrderService.PlaceOrder\" timeout --solution MyApp.sln"
        ]
    );

    public Command Build()
    {
        var methodSpecArg =
            new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var oldNameArg = new Argument<string>("oldName", "Existing parameter name.");
        var newNameArg = new Argument<string>("newName", "Replacement parameter name.");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var renameParam = new Command("rename-param", "Rename a method parameter and update named-argument call sites.")
        {
            methodSpecArg,
            oldNameArg,
            newNameArg,
            solutionOpt,
            dryRunOpt
        };

        renameParam.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(methodSpecArg);
            var oldName = context.ParseResult.GetValueForArgument(oldNameArg);
            var newName = context.ParseResult.GetValueForArgument(newNameArg);
            var solution = context.ParseResult.GetValueForOption(solutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(dryRunOpt);

            context.ExitCode =
                await ChangeSignatureCommandHandlers.RenameParamAsync(_sessionFactory, _changeSignature, _solutionChangeWriter, methodSpec, oldName, newName, solution, dryRun);
        });

        // ── reorder ────────────────────────────────────────────────────────────
        var reorderMethodSpecArg =
            new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var orderArg = new Argument<string>("order",
            "Comma-separated new parameter order: indices (\"2,0,1\") or names (\"c,a,b\").");
        var reorderSolutionOpt = CommandOptionFactory.CreateSolutionOption();
        var reorderDryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var reorder = new Command("reorder", "Reorder method parameters and update all call sites.")
        {
            reorderMethodSpecArg,
            orderArg,
            reorderSolutionOpt,
            reorderDryRunOpt
        };

        reorder.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(reorderMethodSpecArg);
            var orderRaw = context.ParseResult.GetValueForArgument(orderArg);
            var solution = context.ParseResult.GetValueForOption(reorderSolutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(reorderDryRunOpt);

            var newOrder = orderRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            context.ExitCode =
                await ChangeSignatureCommandHandlers.ReorderAsync(_sessionFactory, _changeSignature, _solutionChangeWriter, methodSpec, newOrder, solution, dryRun);
        });

        // ── add-param ──────────────────────────────────────────────────────────
        var addMethodSpecArg =
            new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var paramSpecArg = new Argument<string>("paramSpec", "Parameter declaration, e.g. \"int timeout = 30\"");
        var afterOpt = new Option<string?>("--after",
            "Insert the new parameter immediately after this existing parameter name.");
        var addSolutionOpt = CommandOptionFactory.CreateSolutionOption();
        var addDryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var addParam = new Command("add-param", "Add a parameter to a method signature (must have a default value).")
        {
            addMethodSpecArg,
            paramSpecArg,
            afterOpt,
            addSolutionOpt,
            addDryRunOpt
        };

        addParam.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(addMethodSpecArg);
            var paramSpec = context.ParseResult.GetValueForArgument(paramSpecArg);
            var after = context.ParseResult.GetValueForOption(afterOpt);
            var solution = context.ParseResult.GetValueForOption(addSolutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(addDryRunOpt);

            context.ExitCode =
                await ChangeSignatureCommandHandlers.AddParamAsync(_sessionFactory, _changeSignature, _solutionChangeWriter, methodSpec, paramSpec, after, solution, dryRun);
        });

        // ── remove-param ───────────────────────────────────────────────────────
        var removeMethodSpecArg =
            new Argument<string>("methodSpec", "Method spec, e.g. MyApp.Services.OrderService.PlaceOrder");
        var paramNameArg = new Argument<string>("paramName", "Name of the parameter to remove.");
        var forceOpt = new Option<bool>("--force", "Remove even if the parameter has no default value.");
        var removeSolutionOpt = CommandOptionFactory.CreateSolutionOption();
        var removeDryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var removeParam = new Command("remove-param",
            "Remove a parameter from a method signature and update all call sites.")
        {
            removeMethodSpecArg,
            paramNameArg,
            forceOpt,
            removeSolutionOpt,
            removeDryRunOpt
        };

        removeParam.SetHandler(async context =>
        {
            var methodSpec = context.ParseResult.GetValueForArgument(removeMethodSpecArg);
            var paramName = context.ParseResult.GetValueForArgument(paramNameArg);
            var force = context.ParseResult.GetValueForOption(forceOpt);
            var solution = context.ParseResult.GetValueForOption(removeSolutionOpt);
            var dryRun = context.ParseResult.GetValueForOption(removeDryRunOpt);

            context.ExitCode =
                await ChangeSignatureCommandHandlers.RemoveParamAsync(_sessionFactory, _changeSignature, _solutionChangeWriter, methodSpec, paramName, force, solution, dryRun);
        });

        var command = new Command("change-signature", "Change method signatures.")
        {
            renameParam,
            reorder,
            addParam,
            removeParam
        };

        return command;
    }
}
