using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

internal sealed class ImplementInterfaceCommand : IRtCommand
{
    private ImplementInterfaceCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "implement-interface",
        true,
        false,
        "refactor",
        Aliases: ["ii"],
        WhenToUse:
        "Generating throw-stub implementations for all unimplemented interface members in a class. Use after extract-interface or when a class is added to an interface's base list and needs skeleton stubs before real implementations are written.",
        Examples:
        [
            "rt refactor implement-interface --type BookingRepository --interface IBookingRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt refactor ii --type EmailSender --dry-run",
            "rt refactor implement-interface --type HeroApiClient --solution RoslynTools.slnx"
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to add interface stubs to") { IsRequired = true };
        var interfaceOpt =
            new Option<string?>("--interface", "Interface to implement (optional; auto-detected if omitted)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("implement-interface",
            "Generate throw-stub implementations for unimplemented interface members");
        cmd.AddAlias("ii");
        cmd.Add(typeOpt);
        cmd.Add(interfaceOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, iface, solution, dryRun) => { await ExecuteAsync(type, iface, solution, dryRun); },
            typeOpt, interfaceOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string? interfaceName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await RtServiceProvider.Get<ImplementInterfaceService>().RunAsync(sln, typeName, interfaceName, dryRun);
        }
    }
}
