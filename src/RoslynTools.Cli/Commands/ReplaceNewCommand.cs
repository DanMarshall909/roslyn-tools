using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ReplaceNewCommand : IRtCommand
{
    private ReplaceNewCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "replace-new",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: ["rn"],
        WhenToUse: "Replacing all constructor invocations of a type with a method call or different constructor. Use when consolidating option construction patterns into factory methods.",
        Examples:
        [
            "rt refactor replace-new --type \"Option<string>\" --with \"Factory.CreateStringOption\" --solution RoslynTools.slnx --dry-run",
            "rt refactor rn --type \"Option<string?>\" --with \"CreateFieldOption\" --if-arg 0 --if-arg-literal \"--field\" --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Type name to replace, e.g. Option<string> or Option<string?>") { IsRequired = true };
        var withOpt = new Option<string>("--with", "Replacement: method name or factory call") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var ifArgIndexOpt = new Option<int?>("--if-arg", "Filter: only replace calls where argument at this index matches --if-arg-literal");
        var ifArgLiteralOpt = new Option<string?>("--if-arg-literal", "Filter: match this string literal value");

        var cmd = new Command("replace-new", "Replace all constructor invocations of a type with a method call")
        {
            typeOpt, withOpt, solutionOpt, dryRunOpt, ifArgIndexOpt, ifArgLiteralOpt
        };
        cmd.AddAlias("rn");

        cmd.SetHandler(async (type, with_method, solution, dryRun, ifArgIndex, ifArgLiteral) =>
        {
            await ExecuteAsync(type, with_method, solution, dryRun, ifArgIndex, ifArgLiteral);
        },
        typeOpt, withOpt, solutionOpt, dryRunOpt, ifArgIndexOpt, ifArgLiteralOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string type, string with_method, string? solutionPath, bool dryRun, int? ifArgIndex = null, string? ifArgLiteral = null)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await new ReplaceNewService().RunAsync(solution, type, with_method, dryRun, ifArgIndex, ifArgLiteral);
        }
    }
}
