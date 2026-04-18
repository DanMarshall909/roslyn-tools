using System.CommandLine;

namespace RoslynTools.Commands;

internal static class ImplementInterfaceCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to add interface stubs to") { IsRequired = true };
        var interfaceOpt = new Option<string?>("--interface", "Interface to implement (optional; auto-detected if omitted)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("implement-interface", "Generate throw-stub implementations for unimplemented interface members");
        cmd.AddAlias("ii");
        cmd.Add(typeOpt);
        cmd.Add(interfaceOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, iface, solution, dryRun) =>
        {
            await ExecuteAsync(type, iface, solution, dryRun);
        }, typeOpt, interfaceOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static Task ExecuteAsync(string typeName, string? interfaceName, string? solutionPath, bool dryRun)
    {
        throw new NotImplementedException("implement-interface is not yet implemented.");
    }
}
