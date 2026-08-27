using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a type to a record format.
/// </summary>
internal sealed class ConvertToRecordCommand(IWorkspaceSessionFactory sessionFactory, ConvertToRecordService convertToRecord) : IRtCommand, IRefactorCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly ConvertToRecordService _convertToRecord = convertToRecord;
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
        var flags = RtCommandFlagBinder.Create()
            .WithSolution()
            .WithDryRun();

        var cmd = new Command("convert-to-record", "Convert a class to a record type with positional parameters");
        cmd.AddAlias("ctr");
        cmd.Add(typeOpt);
        flags.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var type = ctx.ParseResult.GetValueForOption(typeOpt)!;
            var request = flags.Bind(ctx.ParseResult, f => ConvertToRecordCliRequest.FromFlags(f, type));
            await ExecuteAsync(request.TypeName, request.SolutionPath, request.DryRun);
        });

        return cmd;
    }

    private sealed record ConvertToRecordCliRequest(
        string TypeName,
        string? SolutionPath,
        bool DryRun) : ISupportsSolutionOption, ISupportsDryRunOption
    {
        public static ConvertToRecordCliRequest FromFlags(RtFlagValues flags, string typeName)
        {
            return new ConvertToRecordCliRequest(typeName, flags.SolutionPath, flags.DryRun);
        }
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
