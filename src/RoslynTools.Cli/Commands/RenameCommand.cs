using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Models;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Renames symbols within the solution.
/// </summary>
internal sealed class RenameCommand(
    IWorkspaceSessionFactory sessionFactory,
    RenameService rename,
    RenameOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly RenameService _rename = rename;
    private readonly RenameOperation _operation = operation;

    public static RtCommandInfo Info { get; } = new(
        "rename",
        true,
        false,
        "refactor",
        Aliases: ["ren"],
        WhenToUse:
        "Renaming a C# symbol (type, method, property, field, namespace) and updating every reference across the solution. Prefer this over Edit or a manual grep+sed: rt uses Roslyn semantic rename so references that only bind by type inference are also updated, and string literals/comments are not accidentally rewritten. Use --csv to batch-apply a curated rename mapping file.",
        Examples:
        [
            "rt refactor rename OldTypeName NewTypeName --solution MySolution.sln",
            "rt refactor rename MyNs.MyType.OldProp NewProp --kind property --dry-run",
            "rt refactor rename --csv scripts/renames.csv --solution MySolution.sln --dry-run",
            "rt refactor ren FooService BarService              # short alias, auto-detect solution",
            "rt inspect find-refs --symbol Foo --json | rt refactor rename --new-name Bar --dry-run   # symbol piped from stdin"
        ]
    );

    public Command Build()
    {
        var symbolArg = new Argument<string?>("symbol", () => null, "Fully-qualified symbol name to rename.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var newNameArg = new Argument<string?>("new-name", () => null, "New name for the symbol.")
        {
            Arity = ArgumentArity.ZeroOrOne
        };
        var flags = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file. Defaults to the single solution in current directory.")
            .WithDryRun("Preview changes without writing to disk")
            .WithKind("Symbol kind: type, method, property, field, namespace");
        var csvOpt = new Option<string?>("--csv",
            "Path to a CSV file with source_symbol/target_name, or prefixed *_fqn plus *_name_new columns.");
        var newNameOpt = new Option<string?>("--new-name",
            "New name for the symbol. Use this when the symbol is supplied via piped stdin JSON.");
        var relatedEditOpt = new Option<string[]>(
            "--related-edit",
            "Related edit ID returned by a previous preview. Repeat to select multiple edits.")
        {
            AllowMultipleArgumentsPerToken = true
        };

        var cmd = new Command("rename", "Rename a symbol and all its references across the solution")
        {
            symbolArg, newNameArg, csvOpt, newNameOpt, relatedEditOpt
        };
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
            {
                var symbol = ctx.ParseResult.GetValueForArgument(symbolArg);
                var newName = ctx.ParseResult.GetValueForOption(newNameOpt)
                              ?? ctx.ParseResult.GetValueForArgument(newNameArg);
                var csv = ctx.ParseResult.GetValueForOption(csvOpt);
                var relatedEditIds = ctx.ParseResult.GetValueForOption(relatedEditOpt) ?? [];
                if (string.IsNullOrWhiteSpace(csv))
                    symbol = await PipedSymbolReader.ResolveAsync(symbol, RtConsole.IsInputRedirected, RtConsole.In);
                var request = flags.Bind(ctx.ParseResult, f => RenameCliRequest.FromFlags(f, symbol, newName, csv));
                if (!string.IsNullOrWhiteSpace(request.CsvPath))
                {
                    var (workspace, solution) =
                        await _sessionFactory.LoadWorkspaceAsync(request.SolutionPath);
                    using (workspace)
                    {
                        await _rename.ExecuteBatchAsync(
                            solution,
                            request.CsvPath,
                            request.DryRun);
                    }
                    return;
                }

                if (string.IsNullOrWhiteSpace(request.Symbol)
                    || string.IsNullOrWhiteSpace(request.NewName))
                    throw new InvalidOperationException(
                        "Provide either <symbol> <new-name> or --csv <path>.");

                var result = await _operation.ExecuteAsync(new RenameOperationRequest(
                    request.Symbol,
                    request.NewName,
                    request.Kind,
                    request.SolutionPath,
                    request.DryRun,
                    relatedEditIds));
                WriteRelatedRenamePlan(result);
            });

        cmd.AddAlias("ren");

        return cmd;
    }

    private static void WriteRelatedRenamePlan(RenameResult result)
    {
        if (result.RelatedRenamePlan.Count == 0)
            return;

        RtConsole.WriteLine(RtOutputContext.Diagnostic, "Related rename plan (opt-in):");
        foreach (var edit in result.RelatedRenamePlan)
        {
            var selected = result.SelectedRelatedEdits.Contains(edit.Id, StringComparer.Ordinal)
                ? "selected"
                : "not selected";
            var prose = edit.Prose ? ", prose" : string.Empty;
            RtConsole.WriteLine(
                RtOutputContext.Diagnostic,
                $"  [{selected}{prose}] {edit.Id}: {edit.OldText} -> {edit.NewText}");
        }
    }

    /// <summary>
    /// Asynchronously renames multiple symbols in the solution.
    /// </summary>
    internal Task ExecuteBatchAsync(
        Solution sln, string csvPath, bool dryRun)
    {
        return _rename.ExecuteBatchAsync(sln, csvPath, dryRun);
    }

    private sealed record RenameCliRequest(
        string? Symbol,
        string? NewName,
        string? CsvPath,
        string? SolutionPath,
        string? Kind,
        bool DryRun) : ISupportsSolutionOption, ISupportsKindOption, ISupportsDryRunOption
    {
        public static RenameCliRequest FromFlags(
            RtFlagValues flags,
            string? symbol,
            string? newName,
            string? csvPath)
        {
            return new RenameCliRequest(
                symbol,
                newName,
                csvPath,
                flags.SolutionPath,
                flags.Kind,
                flags.DryRun);
        }
    }
}
