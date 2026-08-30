using System.Text;
using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.CommandDescriptors;
using RoslynTools.Core.Output;

namespace RoslynTools.Mcp.Tools;

/// <summary>
/// Temporary compatibility adapter for descriptors that have not yet migrated to an
/// <see cref="ICommandApplicationPort" />. New commands should provide a port and never
/// enter this CLI parsing path.
/// </summary>
internal sealed class CliCommandBridge(IServiceProvider services)
{
    private readonly IServiceProvider _services = services;

    public async Task<ToolResult> InvokeAsync(
        ApplicationCommandDescriptor descriptor,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var commandName = descriptor.Path[^1];
        var positionalNames = descriptor.Input.Arguments.Select(argument => argument.Name).ToList();
        var cliArgs = BuildCliArgs(
            commandName,
            arguments,
            positionalNames,
            descriptor.Structured);

        var capture = new StringBuilder();
        int exitCode;
        using var capturing = new CapturingConsole(capture);
        using (RtConsole.UseConsole(capturing))
        {
            exitCode = await App.RunAsync(cliArgs, _services);
        }

        var output = capture.ToString().Trim();
        return new ToolResult(exitCode != 0, output.Length > 0 ? output : "(no output)");
    }

    private static string[] BuildCliArgs(
        string commandName,
        JsonElement args,
        List<string> positionalNames,
        bool structured)
    {
        var list = new List<string> { commandName };

        if (structured)
            list.Add("--json");

        var positionals = new string?[positionalNames.Count];
        var named = new List<string>();

        foreach (var prop in args.EnumerateObject())
        {
            if (prop.Name == "dry-run")
            {
                if (prop.Value.ValueKind == JsonValueKind.True)
                    list.Add("--dry-run");
                continue;
            }

            var argIdx = positionalNames.IndexOf(prop.Name);
            if (argIdx >= 0)
            {
                positionals[argIdx] = GetValue(prop.Value);
                continue;
            }

            switch (prop.Value.ValueKind)
            {
                case JsonValueKind.True:
                    named.Add("--" + prop.Name);
                    break;
                case JsonValueKind.False:
                    break;
                case JsonValueKind.Array:
                    foreach (var item in prop.Value.EnumerateArray())
                    {
                        named.Add("--" + prop.Name);
                        named.Add(GetValue(item));
                    }

                    break;
                default:
                    named.Add("--" + prop.Name);
                    named.Add(GetValue(prop.Value));
                    break;
            }
        }

        list.AddRange(positionals.OfType<string>());
        list.AddRange(named);

        return [.. list];
    }

    private static string GetValue(JsonElement element)
    {
        return element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : element.ToString();
    }
}
