using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a class to a static class.
/// </summary>
internal sealed class DestaticClassCommand : IRtCommand
{
    private DestaticClassCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "convert-static-class",
        true,
        false,
        "refactor",
        Aliases: [],
        WhenToUse: "Migrating a static class to an instance class with DI: drops the static modifier, "
                   + "generates a matching interface, registers in Bootstrap, and rewrites consumer call sites "
                   + "to use injected references. Pairs with rt analyze static-class-inventory for sequencing.",
        Examples:
        [
            "rt refactor convert-static-class RoslynTools.Services.InspectService --solution RoslynTools.slnx --dry-run",
            "rt refactor convert-static-class RoslynTools.Services.RenameService --no-interface --solution RoslynTools.slnx"
        ]
    );

    public static Command Build()
    {
        var typeArg = new Argument<string>("type", "Fully-qualified name of the static class to migrate");
        var interfaceNameOpt =
            new Option<string?>("--interface-name", "Name for the generated interface (default: I<TypeName>)");
        var noInterfaceOpt = new Option<bool>("--no-interface", "Skip interface generation");
        var noDiOpt = new Option<bool>("--no-di-registration", "Skip Bootstrap.RegisterServices update");
        var lifetimeOpt =
            new Option<string>("--lifetime", () => "singleton", "DI lifetime: singleton | scoped | transient");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("convert-static-class",
            "Migrate a static class to instance with DI registration and call-site rewrites");
        cmd.AddAlias("destatic-class");
        cmd.AddArgument(typeArg);
        cmd.AddOption(interfaceNameOpt);
        cmd.AddOption(noInterfaceOpt);
        cmd.AddOption(noDiOpt);
        cmd.AddOption(lifetimeOpt);
        cmd.AddOption(solutionOpt);
        cmd.AddOption(dryRunOpt);

        cmd.SetHandler(async (type, interfaceName, noInterface, noDiRegistration, lifetime, solution, dryRun) =>
        {
            if (!noInterface)
                Console.Error.WriteLine(
                    "warning: interface generation is not yet implemented — skipping. Track: US-032.");

            if (!noDiRegistration)
                Console.Error.WriteLine(
                    "warning: DI registration is not yet implemented — skipping. Track: US-033.");

            if (!string.Equals(lifetime, "singleton", StringComparison.Ordinal)
                && !string.Equals(lifetime, "scoped", StringComparison.Ordinal)
                && !string.Equals(lifetime, "transient", StringComparison.Ordinal))
                throw new InvalidOperationException("--lifetime must be singleton, scoped, or transient.");

            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                await RtServiceProvider.Get<DestaticClassService>().RunAsync(sln, type, dryRun);
            }
        }, typeArg, interfaceNameOpt, noInterfaceOpt, noDiOpt, lifetimeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
