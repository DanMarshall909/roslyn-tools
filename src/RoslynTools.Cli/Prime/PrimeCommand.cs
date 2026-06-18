using System.CommandLine;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services;

namespace RoslynTools.Cli.Prime;

/// <summary>
///     Compact session-starter for agents: prints the gate reminder, entry point, hot
///     commands, and <c>_suggestions</c> note in &lt; 400 tokens. No MSBuild or solution load.
///     Registered at root next to <c>suggest</c> and <c>dump-help</c>.
/// </summary>
/// <remarks>
///     Not an <see cref="IRtCommand" /> — registered at root. Sits outside the
///     inspect/analyze/refactor groups because it queries the catalog only.
/// </remarks>
internal static class PrimeCommand
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    internal static Command Build(IServiceProvider services)
    {
        var cmd = new Command("prime",
            "Print a compact session-start summary: gate, entry point, and hot commands (no MSBuild)");

        cmd.SetHandler(_ =>
        {
            var prime = services.GetRequiredService<PrimeService>();
            var result = prime.Build();

            if (RtContext.Format == OutputFormat.Json)
            {
                RtConsole.WriteLine(RtOutputContext.Result, JsonSerializer.Serialize(result, JsonOptions));
                return;
            }

            RtConsole.WriteLine(RtOutputContext.Result, "rt — session primer");
            RtConsole.WriteLine(RtOutputContext.Result, "");
            RtConsole.WriteLine(RtOutputContext.Result, $"Gate:  {result.Gate}");
            RtConsole.WriteLine(RtOutputContext.Result, $"Entry: {result.Entry}");
            RtConsole.WriteLine(RtOutputContext.Result, $"Note:  {result.Note}");
            RtConsole.WriteLine(RtOutputContext.Result, "");
            RtConsole.WriteLine(RtOutputContext.Result, "Hot commands:");
            foreach (var cmd2 in result.HotCommands)
                RtConsole.WriteLine(RtOutputContext.Result, $"  {cmd2}");
        });

        return cmd;
    }
}
