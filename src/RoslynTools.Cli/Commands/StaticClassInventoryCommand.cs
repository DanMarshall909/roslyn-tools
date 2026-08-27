using System.CommandLine;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// A command for retrieving static class inventory information.
/// </summary>
internal sealed class StaticClassInventoryCommand(IWorkspaceSessionFactory sessionFactory, StaticClassInventoryService inventory) : IRtCommand, IStructuredOutputCommand, IAnalyzeCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory = sessionFactory;
    private readonly StaticClassInventoryService _inventory = inventory;
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

    public Command Build()
    {
        var binder = RtCommandFlagBinder.Create()
            .WithSolution("Path to the .sln/.slnx file");

        var cmd = new Command("static-class-inventory",
            "List every static class with call-site count, member count, and inbound coupling for migration planning");
        binder.AddTo(cmd);

        cmd.SetHandler(async ctx =>
        {
            var req = binder.Bind(ctx.ParseResult, f => new StaticClassInventoryCliRequest(f.SolutionPath));

            var sln = await _sessionFactory.GetSolutionAsync(req.SolutionPath);
            var report = await _inventory.AnalyzeAsync(sln);
            var result = StaticClassInventoryResult.FromReport(report);

            RtResultRenderer.Write(RtResult<StaticClassInventoryResult>.Ok(result));
        });

        return cmd;
    }

    private sealed record StaticClassInventoryCliRequest(string? SolutionPath) : ISupportsSolutionOption;
}
