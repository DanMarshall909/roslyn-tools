using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for retrieving static class inventory information.
/// </summary>
internal sealed class StaticClassInventoryCommand : IRtCommand, IStructuredOutputCommand
{
    private StaticClassInventoryCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "static-class-inventory",
        false,
        true,
        "analyze",
        "Planning a static-to-instance migration by listing every static class with its call-site count, member count, and inbound static coupling. Results are sorted leaf-first so the safest classes to de-static appear at the top.",
        [
            "rt analyze static-class-inventory --solution RoslynTools.slnx",
            "rt analyze static-class-inventory --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt static-class-inventory --solution RoslynTools.slnx --json"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    public static Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to the .sln/.slnx file");

        var cmd = new Command("static-class-inventory",
            "List every static class with call-site count, member count, and inbound coupling for migration planning");
        cmd.Add(solutionOpt);

        cmd.SetHandler(async ctx =>
        {
            var solution = ctx.ParseResult.GetValueForOption(solutionOpt);
            var sp = ctx.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<WorkspaceLoader>() ?? new WorkspaceLoader();

            var sln = await loader.GetSolutionAsync(solution);
            var report = await RtServiceProvider.Get<StaticClassInventoryService>().AnalyzeAsync(sln);
            var result = StaticClassInventoryResult.FromReport(report);

            RtResultRenderer.WriteOutput(RtResult<StaticClassInventoryResult>.Ok(result));
        });

        return cmd;
    }
}
