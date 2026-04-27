using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class FindRefsCommand : IRtCommand
{
    private FindRefsCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "find-refs",
        Mutates: false,
        JsonOutput: false,
        Category: "inspect",
        Aliases: null,
        WhenToUse: "Finding all references to any symbol (type, method, property, field, namespace) across the solution using Roslyn's semantic analysis. Prefer this over grep because it resolves overloads and type inference correctly and skips string literals/comments.",
        Examples:
        [
            "rt inspect find-refs --symbol BookingService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol CreateBooking --kind method --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt inspect find-refs --symbol IEmailService --kind type --solution RoslynTools.slnx",
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

        cmd.SetHandler(async (solution, symbol, kind) =>
        {
            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                var result = await FindRefsService.FindAsync(sln, symbol, kind);
                RtSerializer.Write(result);
            }
        },
        solutionOpt, symbolOpt, kindOpt);

        return cmd;
    }
}
