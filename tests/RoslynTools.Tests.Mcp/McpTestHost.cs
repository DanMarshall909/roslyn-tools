using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Daemon.Rpc;
using RoslynTools.Mcp;
using RoslynTools.Tests;

namespace RoslynTools.Tests.Mcp;

internal static class McpTestHost
{
    internal static AdhocProjectBuilder CreateInvoiceProject(
        string invoiceMembers = "",
        string serviceMembers = "public Invoice Create() => new Invoice();",
        string? baseOutputDirectory = null)
    {
        var project = new AdhocProjectBuilder(baseOutputDirectory: baseOutputDirectory);
        project.AddSource("Model.cs", $$"""
                                      namespace TestApp;

                                      public class Invoice
                                      {
                                          {{invoiceMembers}}
                                      }
                                      """);
        project.AddSource("Service.cs", $$"""
                                        namespace TestApp;

                                        public class InvoiceService
                                        {
                                            {{serviceMembers}}
                                        }
                                        """);
        return project;
    }

    internal static ServiceProvider CreateProvider(McpHostOptions? hostOptions = null)
    {
        var services = new ServiceCollection();
        RoslynTools.Bootstrap.RegisterServices(services);
        services.AddMcpServices(
            new StringReader(string.Empty),
            new StringWriter(),
            hostOptions);
        services.AddSingleton<IDaemonRpcClient, UnavailableDaemonRpcClient>();
        services.AddSingleton<IDaemonLauncher, UnavailableDaemonLauncher>();
        return services.BuildServiceProvider();
    }

    private sealed class UnavailableDaemonRpcClient : IDaemonRpcClient
    {
        public Task<DaemonRpcAttempt> ExecuteAsync(
            string commandName,
            JsonElement arguments,
            CommandSafety safety,
            CancellationToken cancellationToken = default) => Task.FromResult(new DaemonRpcAttempt(
            DaemonRpcAttemptState.UnavailableBeforeDispatch,
            null,
            DispatchStarted: false));
    }

    private sealed class UnavailableDaemonLauncher : IDaemonLauncher
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(false);
        }
    }
}
