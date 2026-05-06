using System.Text;
using System.Text.Json;
using RoslynTools;
using RoslynTools.Core;

namespace RoslynTools.Mcp.Tools;

internal sealed class CliCommandBridge
{
    private readonly IServiceProvider _services;
    private readonly Dictionary<string, List<string>> _positionalArgNames;

    public CliCommandBridge(IServiceProvider services)
    {
        _services = services;
        _positionalArgNames = App.Commands.ToDictionary(
            e => e.Info.Name,
            e => e.Build().Arguments.Select(a => a.Name).ToList(),
            StringComparer.Ordinal);
    }

    public async Task<ToolResult> InvokeAsync(
        string toolName,
        JsonElement arguments,
        CancellationToken cancellationToken = default)
    {
        var commandName = toolName.Replace('_', '-');
        var entry = App.Commands.FirstOrDefault(c => c.Info.Name == commandName);
        if (entry.Info is null)
            return new ToolResult(true, $"Unknown tool: {toolName}");

        var positionalNames = _positionalArgNames.GetValueOrDefault(commandName) ?? [];
        var cliArgs = BuildCliArgs(commandName, arguments, entry.Info, positionalNames);

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
        RtCommandInfo info,
        List<string> positionalNames)
    {
        var list = new List<string> { commandName };

        if (info.JsonOutput)
            list.Add("--json");

        var hasDryRun = false;
        var positionals = new string?[positionalNames.Count];
        var named = new List<string>();

        foreach (var prop in args.EnumerateObject())
        {
            if (prop.Name == "dry-run")
            {
                hasDryRun = true;
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
                default:
                    named.Add("--" + prop.Name);
                    named.Add(GetValue(prop.Value));
                    break;
            }
        }

        foreach (var val in positionals.OfType<string>())
            list.Add(val);

        list.AddRange(named);

        if (info.Mutates && !hasDryRun)
            list.Add("--dry-run");

        return [.. list];
    }

    private static string GetValue(JsonElement element) =>
        element.ValueKind == JsonValueKind.String
            ? element.GetString()!
            : element.ToString();
}
