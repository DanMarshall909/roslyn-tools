using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Moves a method from one location to another in the solution.
/// </summary>
internal sealed class MoveMethodCommand : IRtCommand
{
    private MoveMethodCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "move-method",
        true,
        false,
        "refactor",
        "Moving a static method from one class to another, rewriting all call sites across the solution. Use when reorganising utility helpers or pulling methods toward the types they operate on. Pass --create-file to create the target class if it does not yet exist.",
        [
            "rt refactor move-method App.Source.Add --to-type App.Target --solution RoslynTools.slnx --dry-run",
            "rt refactor move-method MathApp.MathHelper.Double --to-type MathApp.MathUtils --solution MySolution.sln",
            "rt refactor move-method Helpers.StringUtils.Truncate --to-type Helpers.TextHelper --create-file --solution MySolution.sln"
        ]
    );

    public static Command Build()
    {
        var methodFqnArg = new Argument<string>("method-fqn", "Fully-qualified method name (e.g. App.Source.Add)");
        var toTypeOpt = new Option<string>("--to-type", "Fully-qualified target class name (e.g. App.Target)")
        { IsRequired = true };
        var createFileOpt = new Option<bool>("--create-file", "Create the target class file if it does not exist");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");

        var cmd = new Command("move-method", "Move a static method to another class, rewriting all call sites")
        {
            methodFqnArg, toTypeOpt, createFileOpt, solutionOpt, dryRunOpt
        };

        cmd.SetHandler(
            async (methodFqn, toType, createFile, solution, dryRun) =>
            {
                await ExecuteAsync(methodFqn, toType, createFile, solution, dryRun);
            }, methodFqnArg, toTypeOpt, createFileOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Asynchronously executes a method move with additional parameters.
    /// </summary>
    internal static async Task ExecuteAsync(
        string methodFqn,
        string toType,
        bool createFile,
        string? solutionPath,
        bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var ok = await RtServiceProvider.Get<MoveMethodService>()
                .RunAsync(sln, methodFqn, toType, createFile, dryRun);
            if (!ok)
                Environment.ExitCode = 1;
        }
    }
}
