using System.CommandLine;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>
/// Converts a class to a static class.
/// </summary>
internal sealed class DestaticClassCommand(
    DaemonRpcRecoveryCoordinator daemon,
    DestaticClassOperation operation) : IRtCommand, IRefactorCommand
{
    private readonly DaemonRpcRecoveryCoordinator _daemon = daemon;
    private readonly DestaticClassOperation _operation = operation;

    public static RtCommandInfo Info => DestaticClassCommandDefinition.Info;

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

        cmd.SetHandler(async context =>
        {
            var type = context.ParseResult.GetValueForArgument(typeArg);
            var noInterface = context.ParseResult.GetValueForOption(noInterfaceOpt);
            var noDiRegistration = context.ParseResult.GetValueForOption(noDiOpt);
            var lifetime = context.ParseResult.GetValueForOption(lifetimeOpt)!;
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

            await DaemonCommandExecutor.ExecuteAsync(
                _daemon,
                _operation,
                new DestaticClassRequest(
                    type,
                    context.ParseResult.GetValueForOption(solutionOpt),
                    context.ParseResult.GetValueForOption(dryRunOpt)),
                context);
        });

        return cmd;
    }
}
