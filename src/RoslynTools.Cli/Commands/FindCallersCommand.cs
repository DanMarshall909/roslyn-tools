using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FindCallersCommand : IRtCommand
{
    private FindCallersCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "find-callers",
        false,
        false,
        "inspect",
        Aliases: ["callers"],
        WhenToUse:
        "Finding all invocation sites of a method before renaming or deleting it, or to understand the blast radius of a signature change. Unlike find-refs, this filters to true call sites only, excluding nameof() and other non-invocation references.",
        Examples:
        [
            "rt inspect find-callers --symbol SendEmail --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect callers --symbol ProcessPayment --include-tests --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-callers --symbol BuildGraphAsync --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var symbolOpt = new Option<string>("--symbol", "Method name to find callers of") { IsRequired = true };
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var includeTestsOpt = CommandOptionFactory.CreateIncludeTestsOption();

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
                var result = await RtServiceProvider.Get<FindCallersService>().FindAsync(sln, symbol, includeTests);
                RtSerializer.Write(result);
            }
        }, symbolOpt, solutionOpt, includeTestsOpt);

        return cmd;
    }

    // Forwarding stubs kept for backwards compatibility with tests
    internal static bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        return RtServiceProvider.Get<FindCallersService>().IsNameofExpression(invocation);
    }
}
