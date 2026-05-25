using System.CommandLine;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Extracts methods from the current file
/// </summary>
internal sealed class ExtractMethodCommand : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    public ExtractMethodCommand(IWorkspaceSessionFactory sessionFactory, ISolutionChangeWriter solutionChangeWriter)
    {
        _solutionChangeWriter = solutionChangeWriter;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "extract-method",
        true,
        false,
        "refactor",
        Aliases: ["em"],
        WhenToUse:
        "Pulling a consecutive range of statements out of a long method body into a new private method. Specify the line range from the source file; the command infers parameter and return types from context. Use --after/--before to control insertion point.",
        Examples:
        [
            "rt refactor extract-method src/Services/BookingService.cs ValidateBooking --start-line 42 --end-line 58 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor em src/Controllers/AdminController.cs BuildCsvRow --start-line 110 --end-line 125 --dry-run",
            "rt refactor extract-method src/RoslynTools/Commands/RenameCommand.cs BuildOptions --start-line 30 --end-line 45 --after Build --solution RoslynTools.slnx"
        ],
        KnownIssues:
        [
            "--replace-duplicates generic type parameterization (US-049): type constraints are NOT inferred. " +
            "When duplicates differ by user-defined generic type arguments (e.g. List<MyType> vs List<OtherType>), " +
            "the extracted method gets <T> but no 'where T : ...' constraint is emitted. " +
            "This means the generated method may not compile if the body calls type-specific members. " +
            "Deferred: constraint inference requires semantic analysis of all member access on T across all call sites.",

            "--replace-duplicates generic type parameterization: only user-defined type arguments (IdentifierNameSyntax) " +
            "are detected as varying. Predefined keyword types (int, string, bool, etc.) produce a PatternMatcher " +
            "structural mismatch so duplicates differing only in keyword types are not detected as generic candidates."
        ],
        SeeAlso: ["inline-method", "find-duplicates", "find-pattern", "transform-parameters"]
    );

    public Command Build()
    {
        var fileArg = new Argument<string>("file", "Path to the .cs file containing the method to extract from.");
        var newNameArg = new Argument<string>("new-name", "Name for the extracted method.");

        var startLineOpt = new Option<int>("--start-line", "First line of the statements to extract (1-based).")
        { IsRequired = true };
        var endLineOpt = new Option<int>("--end-line", "Last line of the statements to extract (1-based, inclusive).")
        { IsRequired = true };
        var afterOpt = new Option<string?>("--after",
            "Insert the new method after this member name. Default: after the containing method.");
        var beforeOpt = new Option<string?>("--before", "Insert the new method before this member name.");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln file. Auto-detected if omitted.");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk.");
        var replaceDuplicatesOpt = new Option<bool>("--replace-duplicates",
            "After extraction, find structurally-matching code elsewhere in the solution and replace it with calls to the new method.");

        var cmd = new Command("extract-method", "Extract a range of statements into a new private method.")
        {
            fileArg, newNameArg, startLineOpt, endLineOpt, afterOpt, beforeOpt, solutionOpt, dryRunOpt,
            replaceDuplicatesOpt
        };
        cmd.AddAlias("em");

        cmd.SetHandler(async ctx =>
        {
            var file = ctx.ParseResult.GetValueForArgument(fileArg);
            var newName = ctx.ParseResult.GetValueForArgument(newNameArg);
            var startLine = ctx.ParseResult.GetValueForOption(startLineOpt);
            var endLine = ctx.ParseResult.GetValueForOption(endLineOpt);
            var after = ctx.ParseResult.GetValueForOption(afterOpt);
            var before = ctx.ParseResult.GetValueForOption(beforeOpt);
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var dryRun = ctx.ParseResult.GetValueForOption(dryRunOpt);
            var replaceDuplicates = ctx.ParseResult.GetValueForOption(replaceDuplicatesOpt);

            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                RtLog.Info("Extracting lines {StartLine}–{EndLine} of '{File}' → '{NewName}'...", startLine, endLine,
                    file, newName);

                Solution newSln;
                try
                {
                    newSln = await MethodExtractor.ExtractAsync(
                        sln,
                        file,
                        newName,
                        startLine,
                        endLine,
                        after,
                        before,
                        replaceDuplicates);
                }
                catch (InvalidOperationException ex)
                {
                    RtLog.Error("extract-method: {Message}", ex.Message);
                    ctx.ExitCode = 1;
                    return;
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    RtLog.Error("extract-method: {Message}", ex.Message);
                    ctx.ExitCode = 1;
                    return;
                }

                await _solutionChangeWriter.ApplyChangesAsync(sln, newSln, dryRun);
            }
        });

        return cmd;
    }
}
