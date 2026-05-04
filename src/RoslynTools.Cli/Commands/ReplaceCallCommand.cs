using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ReplaceCallCommand : IRtCommand
{
    private ReplaceCallCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "replace-call",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["rc"],
        WhenToUse: "Replacing all invocations of a static method with a different method name across the solution. Use when renaming a utility method that isn't captured by a semantic rename (e.g. because callers use it via a static import or the old name needs to stay as an overload).",
        Examples:
        [
            "rt refactor replace-call --from Logger.Log --to LogInfo --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor rc --from DateHelper.UtcNow --to TimeProvider.GetUtcNow --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor replace-call --from Console.WriteLine --to Logger.Info --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var fromOpt = new Option<string>("--from", "Full method reference to replace, e.g. Logger.Log") { IsRequired = true };
        var toOpt = new Option<string>("--to", "New method name, e.g. LogInfo") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var ifArgIndexOpt = new Option<int?>("--if-arg", "Filter: only replace calls where argument at this index matches --if-arg-literal");
        var ifArgLiteralOpt = new Option<string?>("--if-arg-literal", "Filter: match this string literal value");

        var cmd = new Command("replace-call", "Replace all invocations of one static method with another")
        {
            fromOpt, toOpt, solutionOpt, dryRunOpt, ifArgIndexOpt, ifArgLiteralOpt
        };
        cmd.AddAlias("rc");

        cmd.SetHandler(async (from, to, solution, dryRun, ifArgIndex, ifArgLiteral) =>
        {
            await ExecuteAsync(from, to, solution, dryRun, ifArgIndex, ifArgLiteral);
        },
        fromOpt, toOpt, solutionOpt, dryRunOpt, ifArgIndexOpt, ifArgLiteralOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string from, string to, string? solutionPath, bool dryRun, int? ifArgIndex = null, string? ifArgLiteral = null)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await ReplaceCallService.RunAsync(solution, from, to, dryRun, ifArgIndex, ifArgLiteral);
        }
    }
}
