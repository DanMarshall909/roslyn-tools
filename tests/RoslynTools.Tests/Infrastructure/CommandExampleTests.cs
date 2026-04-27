using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using RoslynTools;

namespace RoslynTools.Tests.Infrastructure;

public class CommandExampleTests
{
    [Fact]
    public void RtCommandInfo_examples_parse_against_command_tree()
    {
        var parser = BuildParser();
        var failures = new List<string>();

        foreach (var (_, info) in App.Commands.OrderBy(c => c.Info.Name, StringComparer.Ordinal))
        {
            foreach (var example in info.Examples)
            {
                var commandLine = StripExecutableName(example);
                var result = parser.Parse(commandLine);
                if (result.Errors.Count == 0)
                    continue;

                failures.Add(
                    $"{info.Name}: `{example}` => {string.Join("; ", result.Errors.Select(e => e.Message))}");
            }
        }

        Assert.True(
            failures.Count == 0,
            "Invalid RtCommandInfo example(s):" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    private static Parser BuildParser()
    {
        var root = new RootCommand("Roslyn-based C# refactoring tool");

        foreach (var (cmd, _) in App.Commands)
            root.AddCommand(cmd);

        root.AddGlobalOption(new Option<bool>("--json", "Emit results as JSON"));
        root.AddGlobalOption(new Option<bool>("--llm", "Emit compact output for LLM consumption"));
        root.AddGlobalOption(new Option<bool>("--markdown", "Emit results as Markdown"));

        return new CommandLineBuilder(root)
            .UseDefaults()
            .Build();
    }

    private static string StripExecutableName(string example)
    {
        var trimmed = StripShellSuffix(example.Trim());
        if (trimmed.Equals("rt", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return trimmed.StartsWith("rt ", StringComparison.OrdinalIgnoreCase)
            ? trimmed[3..]
            : trimmed;
    }

    private static string StripShellSuffix(string example)
    {
        var inQuote = false;
        for (var i = 0; i < example.Length; i++)
        {
            var ch = example[i];
            if (ch == '"')
                inQuote = !inQuote;

            if (!inQuote && (ch == '#' || ch == '|'))
                return example[..i].TrimEnd();
        }

        return example;
    }
}
