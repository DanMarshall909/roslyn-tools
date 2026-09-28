using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Daemon;

public sealed class DaemonApplicationDispatchTests
{
    [Fact]
    public void Public_read_only_application_commands_have_typed_or_local_dispatch()
    {
        var services = new ServiceCollection();
        global::RoslynTools.Bootstrap.RegisterServices(services);
        using var provider = services.BuildServiceProvider();
        var typedNames = provider.GetServices<ICommandApplicationPort>()
            .Where(port => port is not ILegacyCommandApplicationPort)
            .Select(port => port.Definition.Info.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unmatched = App.Commands
            .Select(command => command.Info)
            .Where(info => !info.Mutates
                           && !App.LocalOnlyCommandNames.Contains(info.Name)
                           && CliInvocationClassifier.RequiresEagerMsBuildRegistration([info.Name])
                           && !typedNames.Contains(info.Name))
            .Select(info => info.Name)
            .ToArray();

        unmatched.ShouldBeEmpty();
    }
}
