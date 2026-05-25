using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a type to a record format.
/// </summary>
internal sealed class ConvertToRecordCommand : IRtCommand, IRefactorCommand, IMutatingCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly ConvertToRecordService _convertToRecord;
    public ConvertToRecordCommand(IWorkspaceSessionFactory sessionFactory, ConvertToRecordService convertToRecord)
    {
        _convertToRecord = convertToRecord;
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "convert-to-record",
        true,
        false,
        "refactor",
        Aliases: ["ctr"],
        WhenToUse:
        "Converting a class with init-only properties to a positional record, automatically removing redundant Equals/GetHashCode overrides and == operators that the record synthesises. Best suited to immutable DTOs and value objects.",
        Examples:
        [
            "rt refactor convert-to-record --type BookingRequest --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ctr --type Money --dry-run",
            "rt refactor convert-to-record --type ProductDto --solution RoslynTools.slnx"
        ]
    );

    public Command Build()
    {
        var typeOpt = CommandOptionFactory.CreateTypeOption();
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
        cmd.AddAlias("ctr");
        cmd.AddAll(typeOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, solution, dryRun) => { await ExecuteAsync(type, solution, dryRun); }, typeOpt,
            solutionOpt, dryRunOpt);

        return cmd;
    }

    /// <summary>
    /// Executes the conversion of a type to a record.
    /// </summary>
    internal async Task ExecuteAsync(string typeName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await _convertToRecord.RunAsync(sln, typeName, dryRun);
        }
    }
}
