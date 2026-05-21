using System.CommandLine;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all callers of a given symbol in the current file
/// </summary>
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

        cmd.SetHandler(async ctx =>
        {
            var symbol = ctx.ParseResult.GetValueForOption(symbolOpt)!;
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var includeTests = ctx.ParseResult.GetValueForOption(includeTestsOpt);
            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<IWorkspaceSessionFactory>() ?? IWorkspaceSessionFactory.CreateDefault();

            var sln = await loader.GetSolutionAsync(solution);
            var result = await RtServiceProvider.Get<FindCallersService>().FindAsync(sln, symbol, includeTests);
            RtResultRenderer.WriteOutput(RtResult<FindCallersResult>.Ok(result));
        });

        return cmd;
    }

    // Forwarding stubs kept for backwards compatibility with tests
    internal static bool IsNameofExpression(InvocationExpressionSyntax invocation)
    {
        return RtServiceProvider.Get<FindCallersService>().IsNameofExpression(invocation);
    }
}
