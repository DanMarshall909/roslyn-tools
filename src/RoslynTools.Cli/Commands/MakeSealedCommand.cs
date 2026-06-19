using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a class to be sealed
/// </summary>
internal sealed class MakeSealedCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly MakeSealedService _makeSealed;
    public MakeSealedCommand(IWorkspaceSessionFactory sessionFactory, MakeSealedService makeSealed)
    {
        _makeSealed = makeSealed;
        _sessionFactory = sessionFactory;
    }

    // Stryker disable all
    public static RtCommandInfo Info { get; } = new(
        "make-sealed",
        true,
        false,
        "refactor",
        Aliases: ["ms"],
        WhenToUse:
        "Adding the sealed modifier to a class that has no derived types in the solution. Sealing classes improves runtime dispatch performance and communicates design intent. The command verifies no derived classes exist before modifying, making it safe to run without a prior manual check.",
        Examples:
        [
            "rt refactor make-sealed --type BookingValidator --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ms --type CsvFormatter --dry-run",
            "rt refactor make-sealed --type RtCommandInfo --solution RoslynTools.slnx"
        ]
    );
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
            await ExecuteAsync(req.Type, req.SolutionPath, req.DryRun);
        });

        return cmd;
    }
    // Stryker restore all

    /// <summary>
    /// Executes the MakeSealedCommand with async parameters
    /// </summary>
    internal async Task ExecuteAsync(string typeName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _makeSealed.RunAsync(sln, typeName, dryRun);
        }
    }

    private sealed record MakeSealedCliRequest(
        string Type,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption;
}
