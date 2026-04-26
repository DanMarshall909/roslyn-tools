using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FindCallersCommand : IRtCommand
{
    private FindCallersCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-callers",
        Mutates: false,
        JsonOutput: false,
        Category: "Navigation",
        Aliases: ["callers"],
        WhenToUse: "Finding all invocation sites of a method before renaming or deleting it, or to understand the blast radius of a signature change. Unlike find-refs, this filters to true call sites only, excluding nameof() and other non-invocation references.",
        Examples:
        [
            "rt find-callers --symbol SendEmail --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt callers --symbol ProcessPayment --include-tests --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt find-callers --symbol BuildGraphAsync --solution RoslynTools.slnx",
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Method name to find callers of") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var includeTestsOpt = new Option<bool>("--include-tests", "Include test projects in results");

        var cmd = new Command("find-callers", "Find all invocation sites for a method");
        cmd.AddAlias("callers");
        cmd.Add(symbolOpt);
        cmd.Add(solutionOpt);
        cmd.Add(includeTestsOpt);

        cmd.SetHandler(async (symbol, solution, includeTests) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var result = await FindCallersService.FindAsync(sln, symbol, includeTests);
                RtSerializer.Write(result);
            }
        }, symbolOpt, solutionOpt, includeTestsOpt);

        return cmd;
    }

    // Forwarding stubs kept for backwards compatibility with tests
    internal static bool IsNameofExpression(InvocationExpressionSyntax invocation) =>
        FindCallersService.IsNameofExpression(invocation);
}
