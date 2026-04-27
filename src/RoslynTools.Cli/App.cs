using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Reflection;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Core.Decisions;
using RoslynTools.Core.Output;

namespace RoslynTools;

internal static class App
{
    /// <summary>
    /// Enforces the convention "every class in RoslynTools.Commands ending with 'Command'
    /// implements IRtCommand". Runs at first access to any <see cref="App"/> member, so it fires
    /// during startup — including the post-build <c>rt dump-help</c> target, which makes this a
    /// build-time check in practice.
    /// </summary>
    static App()
    {
        // Note: static classes in C# are marked abstract+sealed in IL, so we intentionally do
        // NOT filter by IsAbstract — a legacy static class that should implement IRtCommand needs
        // to be caught here so the dev converts it to `sealed class : IRtCommand`.
        var missing = typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace == "RoslynTools.Commands"
                        && !t.IsInterface
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && t.Name != nameof(DumpHelpCommand)
                        && !typeof(IRtCommand).IsAssignableFrom(t))
            .Select(t => t.Name)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidOperationException(
                "These classes in RoslynTools.Commands end with 'Command' but do not implement IRtCommand: "
                + string.Join(", ", missing)
                + ". Add `: IRtCommand` and supply static `Info` + `Build()`.");
        }
    }

    /// <summary>
    /// Registry of every <see cref="IRtCommand"/> in this assembly, with its <see cref="RtCommandInfo"/>.
    /// Discovered via reflection — add a new command by creating a class that implements
    /// <see cref="IRtCommand"/>; no edit to this file required.
    /// </summary>
    internal static IReadOnlyList<(Command Cmd, RtCommandInfo Info)> Commands { get; } = Discover();

    internal static async Task<int> RunAsync(string[] args)
    {
        var root = new RootCommand("Roslyn-based C# refactoring tool");

        foreach (var (cmd, _) in Commands)
        {
            root.AddCommand(cmd);
        }

        // Hidden meta-command, not part of the public registry (does not implement IRtCommand).
        root.AddCommand(DumpHelpCommand.Build());

        var jsonOpt = new Option<bool>("--json", "Emit results as JSON");
        var llmOpt = new Option<bool>("--llm", "Emit compact output for LLM consumption");
        var markdownOpt = new Option<bool>("--markdown", "Emit results as Markdown");
        var decisionsOpt = new Option<FileInfo?>(
            "--decisions",
            "Path to a YAML file recording disambiguation choices. "
            + "If unanswered entries exist (or new ones surface), rt writes them and exits 2.");
        root.AddGlobalOption(jsonOpt);
        root.AddGlobalOption(llmOpt);
        root.AddGlobalOption(markdownOpt);
        root.AddGlobalOption(decisionsOpt);

        return await new CommandLineBuilder(root)
            .UseDefaults()
            .AddMiddleware(async (context, next) =>
            {
                var md = context.ParseResult.GetValueForOption(markdownOpt);
                var json = context.ParseResult.GetValueForOption(jsonOpt);
                var llm = context.ParseResult.GetValueForOption(llmOpt);
                RtContext.Format = md ? OutputFormat.Markdown
                                 : json ? OutputFormat.Json
                                 : llm ? OutputFormat.Llm
                                 : OutputFormat.Text;

                var decisionsFile = context.ParseResult.GetValueForOption(decisionsOpt);
                if (decisionsFile is not null)
                {
                    DecisionContext.Store = DecisionStore.Load(decisionsFile.FullName);
                    try
                    {
                        await next(context);
                    }
                    catch (DecisionsRequiredException)
                    {
                        var store = DecisionContext.Store!;
                        store.Save(decisionsFile.FullName);
                        Console.Error.WriteLine(
                            $"wrote {store.PendingCount} pending decision(s) to {decisionsFile.FullName}; "
                            + "resolve `choose:` fields and re-run.");
                        context.ExitCode = 2;
                    }
                    finally
                    {
                        DecisionContext.Store = null;
                    }
                }
                else
                {
                    await next(context);
                }
            }, MiddlewareOrder.Configuration)
            .Build()
            .InvokeAsync(args);
    }

    private static List<(Command, RtCommandInfo)> Discover()
    {
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public;

        var implementers = typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IRtCommand).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var list = new List<(Command, RtCommandInfo)>(implementers.Length);
        foreach (var type in implementers)
        {
            var build = type.GetMethod("Build", staticFlags)
                        ?? throw new InvalidOperationException(
                            $"{type.Name} implements IRtCommand but exposes no public static Build() — "
                            + "check visibility (interface members are implicitly public).");
            var infoProp = type.GetProperty("Info", staticFlags)
                           ?? throw new InvalidOperationException(
                               $"{type.Name} implements IRtCommand but exposes no public static Info property.");

            var cmd = (Command)build.Invoke(null, null)!;
            var info = (RtCommandInfo)infoProp.GetValue(null)!;
            list.Add((cmd, info));
        }

        return list;
    }
}
