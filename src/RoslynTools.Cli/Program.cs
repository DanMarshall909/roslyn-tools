using Microsoft.Extensions.Hosting;
using RoslynTools;
using RoslynTools.Core;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Fast path: `rt dump-help` only enumerates the command registry in-process and emits
        // Markdown — it doesn't load an MSBuild workspace. Skip MSBuild registration and host
        // construction so the post-build target that regenerates docs/COMMANDS.md stays fast.
        if (args.Length > 0 && args[0] == "dump-help")
        {
            // Force UTF-8 so em-dashes and other Unicode in RtCommandInfo strings survive
            // `cmd > file` redirection on Windows (default console encoding is often cp1252).
            RtConsole.SetOutputEncoding(System.Text.Encoding.UTF8);
            return await App.RunAsync(args);
        }

        MsBuildBootstrap.Register();

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(Bootstrap.RegisterServices)
            .Build();

        return await App.RunAsync(args, host.Services);
    }
}
