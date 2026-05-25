using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a class to a static class.
/// </summary>
internal sealed class DestaticClassCommand : IRtCommand, IRefactorCommand, IMutatingCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;
    private readonly DestaticClassService _destaticClass;
    public DestaticClassCommand(IWorkspaceSessionFactory sessionFactory, DestaticClassService destaticClass)
    {
        _destaticClass = destaticClass;
        _sessionFactory = sessionFactory;
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

    public Command Build()
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
        cmd.AddAll(interfaceNameOpt, noInterfaceOpt, noDiOpt, lifetimeOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (type, interfaceName, noInterface, noDiRegistration, lifetime, solution, dryRun) =>
        {
            if (!noInterface)
                RtConsole.Log(RtLogLevel.Warning,
                    "interface generation is not yet implemented — skipping (track: US-037).");

            if (!noDiRegistration)
                RtConsole.Log(RtLogLevel.Warning,
                    "DI registration is not yet implemented — skipping (track: US-038).");

            if (!string.Equals(lifetime, "singleton", StringComparison.Ordinal)
                && !string.Equals(lifetime, "scoped", StringComparison.Ordinal)
                && !string.Equals(lifetime, "transient", StringComparison.Ordinal))
                throw new InvalidOperationException("--lifetime must be singleton, scoped, or transient.");

            var (workspace, sln) = await _sessionFactory.LoadWorkspaceAsync(solution);
            using (workspace)
            {
                await _destaticClass.RunAsync(sln, type, dryRun);
            }
        }, typeArg, interfaceNameOpt, noInterfaceOpt, noDiOpt, lifetimeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }
}
