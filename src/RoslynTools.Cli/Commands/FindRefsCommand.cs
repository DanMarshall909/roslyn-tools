using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Finds all references to a given symbol in the current file
/// </summary>
internal sealed class FindRefsCommand : IRtCommand
{
    private FindRefsCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "find-refs",
        false,
        false,
        "inspect",
        Aliases: null,
        WhenToUse:
        "Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.",
        Examples:
        [
            "rt inspect find-refs --symbol BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol CreateBooking --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var symbolOpt = new Option<string>("--symbol", "Simple name of the symbol") { IsRequired = true };
        var kindOpt = new Option<string?>("--kind", "Symbol kind: type, method, property, field, namespace");

        var cmd = new Command("find-refs", "Find all references to a symbol across the solution")
        {
            solutionOpt, symbolOpt, kindOpt
        };

        cmd.SetHandler(async ctx =>
        {
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var symbol = ctx.ParseResult.GetValueForOption(symbolOpt)!;
            var kind = ctx.ParseResult.GetValueForOption(kindOpt);
            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<WorkspaceLoader>() ?? new WorkspaceLoader();
            var sln = await loader.GetSolutionAsync(solution);
            var result = await RtServiceProvider.Get<FindRefsService>().FindAsync(sln, symbol, kind);
            RtSerializer.Write(result);
        });

        return cmd;
    }
}
