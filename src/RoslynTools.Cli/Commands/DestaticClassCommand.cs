using System.CommandLine;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class DestaticClassCommand : IRtCommand
{
    private DestaticClassCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "destatic-class",
        Mutates: true,
        JsonOutput: false,
        Category: "refactor",
        Aliases: [],
        WhenToUse: "Migrating a static class to an instance class with DI: drops the static modifier, "
            + "generates a matching interface, registers in Bootstrap, and rewrites consumer call sites "
            + "to use injected references. Pairs with rt analyze static-class-inventory for sequencing.",
        Examples:
        [
            "rt refactor destatic-class RoslynTools.Services.InspectService --solution RoslynTools.slnx --dry-run",
            "rt refactor destatic-class RoslynTools.Services.RenameService --no-interface --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var typeArg = new Argument<string>("type", "Fully-qualified name of the static class to migrate");
        var interfaceNameOpt = new Option<string?>("--interface-name", "Name for the generated interface (default: I<TypeName>)");
        var noInterfaceOpt = new Option<bool>("--no-interface", "Skip interface generation");
        var noDiOpt = new Option<bool>("--no-di-registration", "Skip Bootstrap.RegisterServices update");
        var lifetimeOpt = new Option<string>("--lifetime", () => "singleton", "DI lifetime: singleton | scoped | transient");
        var solutionOpt = CommandOptionFactory.CreateSolutionOption();
        var dryRunOpt = CommandOptionFactory.CreateDryRunOption();

        var cmd = new Command("destatic-class", "Migrate a static class to instance with DI registration and call-site rewrites");
        cmd.AddArgument(typeArg);
        cmd.AddOption(interfaceNameOpt);
        cmd.AddOption(noInterfaceOpt);
        cmd.AddOption(noDiOpt);
        cmd.AddOption(lifetimeOpt);
        cmd.AddOption(solutionOpt);
        cmd.AddOption(dryRunOpt);

        cmd.SetHandler(_ =>
        {
            RtConsole.WriteLine(RtOutputContext.Diagnostic, "destatic-class is not yet implemented (US-023 in progress).");
            Environment.ExitCode = 1;
            return Task.CompletedTask;
        });

        return cmd;
    }
}
