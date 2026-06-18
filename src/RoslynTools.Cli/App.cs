using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Cli.Suggest;
using RoslynTools.Commands;
using RoslynTools.Core;
using RoslynTools.Core.Decisions;
using RoslynTools.Core.Output;
using RoslynTools.Feedback;
using RoslynTools.Services.Suggest;
using Spectre.Console;
using YamlDotNet.Core;

namespace RoslynTools;

/// <summary>
/// The main application class for Roslyn tools.
/// </summary>
internal static class App
{
    /// <summary>
    ///     The three top-level groups under which every command is filed. Each command's
    ///     <see cref="RtCommandInfo.Category" /> must equal one of these keys exactly — checked
    ///     at startup in <see cref="RunAsync" />.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, string> GroupDescriptions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["inspect"] = "Read-only: examine a single symbol or file",
            ["analyze"] = "Read-only: scan for cross-cutting issues",
            ["refactor"] = "Mutations: rename, move, extract, replace, modify"
        };

    /// <summary>
    ///     Enforces the convention "every class in RoslynTools.Commands ending with 'Command'
    ///     implements IRtCommand". Runs at first access to any <see cref="App" /> member, so it fires
    ///     during startup — including the post-build <c>rt dump-help</c> target, which makes this a
    ///     build-time check in practice.
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
            throw new InvalidOperationException(
                "These classes in RoslynTools.Commands end with 'Command' but do not implement IRtCommand: "
                + string.Join(", ", missing)
                + ". Add `: IRtCommand` and supply static `Info` + `Build()`.");

        // Bidirectional consistency: JsonOutput in RtCommandInfo ↔ IStructuredOutputCommand.
        // For inspect/analyze commands, both must be set together. This prevents shipping a
        // command that claims JSON output but forgets the interface, or vice versa.
        const BindingFlags staticPublic = BindingFlags.Static | BindingFlags.Public;
        var inconsistentOutput = typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace == "RoslynTools.Commands"
                        && !t.IsInterface
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && t.Name != nameof(DumpHelpCommand)
                        && typeof(IRtCommand).IsAssignableFrom(t))
            .Select(t =>
            {
                var info = t.GetProperty("Info", staticPublic)?.GetValue(null) as RtCommandInfo;
                return (t.Name, info?.Category,
                    IsStructured: typeof(IStructuredOutputCommand).IsAssignableFrom(t),
                    ClaimsJson: info?.JsonOutput == true);
            })
            .Where(x => x.Category is "inspect" or "analyze" && x.IsStructured != x.ClaimsJson)
            .Select(x => $"{x.Name} (JsonOutput={x.ClaimsJson}, IStructuredOutputCommand={x.IsStructured})")
            .ToList();

        if (inconsistentOutput.Count > 0)
            throw new InvalidOperationException(
                "These inspect/analyze commands have inconsistent structured-output state "
                + "(RtCommandInfo.JsonOutput and IStructuredOutputCommand must be set together): "
                + string.Join(", ", inconsistentOutput));

        // Category-marker biconditional: every concrete command in RoslynTools.Commands
        // (excluding the DumpHelp meta-command) must implement EXACTLY ONE of
        // IInspectCommand / IAnalyzeCommand / IRefactorCommand. The category marker
        // and RtCommandInfo.Category together describe the same fact in two places;
        // this ratchet keeps them in sync at startup so new commands can't forget to
        // declare a category — the post-build docs/COMMANDS.md regeneration will fail.
        var categoryOffenders = typeof(App).Assembly.GetTypes()
            .Where(t => t.Namespace == "RoslynTools.Commands"
                        && !t.IsInterface
                        && t.Name.EndsWith("Command", StringComparison.Ordinal)
                        && t.Name != nameof(DumpHelpCommand)
                        && typeof(IRtCommand).IsAssignableFrom(t))
            .Select(t => new
            {
                t.Name,
                Markers = new[]
                {
                    typeof(IInspectCommand).IsAssignableFrom(t) ? "IInspectCommand" : null,
                    typeof(IAnalyzeCommand).IsAssignableFrom(t) ? "IAnalyzeCommand" : null,
                    typeof(IRefactorCommand).IsAssignableFrom(t) ? "IRefactorCommand" : null
                }.Where(m => m is not null).ToArray()
            })
            .Where(x => x.Markers.Length != 1)
            .Select(x => x.Markers.Length == 0
                ? $"{x.Name} (no category marker)"
                : $"{x.Name} (multiple: {string.Join(", ", x.Markers)})")
            .ToList();

        if (categoryOffenders.Count > 0)
            throw new InvalidOperationException(
                "These commands must implement exactly one category marker "
                + "(IInspectCommand, IAnalyzeCommand, or IRefactorCommand): "
                + string.Join(", ", categoryOffenders)
                + ". Add the appropriate marker to the class declaration.");
    }

    /// <summary>
    ///     Registry of every <see cref="IRtCommand" /> in this assembly, with its <see cref="RtCommandInfo" />.
    ///     Discovered via reflection — add a new command by creating a class that implements
    ///     <see cref="IRtCommand" />; no edit to this file required. Each entry's <c>Build</c> is
    ///     a factory that produces a fresh <see cref="Command" /> per call, so the same command can
    ///     be added to multiple parents (root shortcut + category group).
    /// </summary>
    internal static IReadOnlyList<(Func<Command> Build, RtCommandInfo Info)> Commands { get; } = Discover();

    /// <summary>
    /// Runs the application asynchronously with command-line arguments and service provider.
    /// </summary>
    internal static async Task<int> RunAsync(string[] args, IServiceProvider? services = null,
        IReadOnlyList<Command>? additionalCommands = null)
    {
        // Ensure an SP is always available for instance-based commands. Callers that pass
        // an explicit SP (CLI host, MCP host) win; callers that pass null (dump-help fast
        // path, tests) get a default Bootstrap-wired SP synthesised here. We own the
        // lifetime of the synthesised one and dispose it on the way out.
        ServiceProvider? ownedServices = null;
        if (services is null)
        {
            ownedServices = BuildDefaultServiceProvider();
            services = ownedServices;
        }

        _currentServices = services;

        try
        {
            return await RunCoreAsync(args, services, additionalCommands);
        }
        finally
        {
            _currentServices = null;
            ownedServices?.Dispose();
        }
    }

    private static ServiceProvider BuildDefaultServiceProvider()
    {
        var sc = new ServiceCollection();
        Bootstrap.RegisterServices(sc);
        return sc.BuildServiceProvider();
    }

    // Lazy process-scoped SP used by App.Commands consumers that operate outside RunAsync
    // (architecture tests, dump-help fast path). Never disposed — the AppDomain owns its lifetime.
    private static ServiceProvider? _registryServices;
    private static readonly object _registryServicesLock = new();
    private static ServiceProvider GetOrCreateRegistryServices()
    {
        if (_registryServices is not null)
            return _registryServices;
        lock (_registryServicesLock)
        {
            _registryServices ??= BuildDefaultServiceProvider();
            return _registryServices;
        }
    }

    private static async Task<int> RunCoreAsync(string[] args, IServiceProvider services,
        IReadOnlyList<Command>? additionalCommands)
    {
        var root = new RootCommand("Roslyn-based C# refactoring tool");

        var groups = GroupDescriptions.ToDictionary(
            kv => kv.Key,
            kv => new Command(kv.Key, kv.Value),
            StringComparer.Ordinal);

        foreach (var (build, info) in Commands)
        {
            if (!groups.TryGetValue(info.Category, out var group))
                throw new InvalidOperationException(
                    $"Command '{info.Name}' has Category='{info.Category}'. Must be one of: "
                    + string.Join(", ", groups.Keys) + ".");

            // Always available under the category group: `rt <group> <name>`.
            group.AddCommand(build());

            // Also expose at root as a shortcut when the name doesn't collide with a group name.
            if (!groups.ContainsKey(info.Name)) root.AddCommand(build());
        }

        foreach (var group in groups.Values) root.AddCommand(group);

        // Hidden meta-command, not part of the public registry (does not implement IRtCommand).
        root.AddCommand(DumpHelpCommand.Build());

        // Meta-command for natural-language command discovery. Reaches into the
        // catalog (via ICommandCatalog → AppCommandCatalog) rather than any
        // user solution, so it sits outside the inspect/analyze/refactor groups.
        root.AddCommand(SuggestCommand.Build(services));

        if (additionalCommands is not null)
            foreach (var cmd in additionalCommands)
                root.AddCommand(cmd);

        var (jsonOpt, llmOpt, markdownOpt, yamlOpt, decisionsOpt, strictJsonOpt, relaxJsonOpt,
            showWorkspaceWarningsOpt, noSuggestOpt) = RegisterGlobalOptions(root);

        return await new CommandLineBuilder(root)
            .UseVersionOption()
            .UseHelp(ctx => ctx.HelpBuilder.CustomizeLayout(BuildHelpLayout))
            .UseEnvironmentVariableDirective()
            .UseParseDirective()
            .UseSuggestDirective()
            .RegisterWithDotnetSuggest()
            .UseTypoCorrections()
            .UseParseErrorReporting()
            .UseExceptionHandler()
            .CancelOnProcessTermination()
            .AddMiddleware((context, next) =>
            {
                context.BindingContext.AddService<IServiceProvider>(_ => services);
                return next(context);
            })
            .AddMiddleware((context, next) =>
            {
                // Unknown-command short-circuit. When the user (or an LLM) types a
                // token at the root that doesn't resolve to a command, run BM25 over
                // the token via SuggestService and surface ranked candidates on
                // stderr with exit 1 — far more useful than the default "Required
                // command was not provided" + full catalog dump. Below a score floor
                // we suppress suggestions (gibberish in → nothing out) and fall
                // through to the standard parse-error path.
                var pr = context.ParseResult;
                if (pr.CommandResult.Command is RootCommand
                    && pr.Errors.Count > 0
                    && pr.UnmatchedTokens.Count > 0
                    && !pr.Tokens.Any(t => t.Value is "-h" or "--help" or "--version"))
                {
                    var typed = pr.UnmatchedTokens[0];
                    var suggest = services.GetRequiredService<SuggestService>();
                    var response = suggest.Suggest(new SuggestRequest(typed, Top: 3));
                    const double minScore = 1.0;
                    var hits = response.Results.Where(r => r.Score >= minScore).ToList();
                    if (hits.Count > 0)
                    {
                        RtLog.Error("'{Typed}' is not a recognised command.", typed);
                        RtLog.Info("Did you mean one of these?");
                        foreach (var hit in hits)
                        {
                            var name = Markup.Escape(hit.Name);
                            var desc = Markup.Escape(TruncateOneLine(hit.WhenToUse, 100));
                            RtConsole.WriteLine(
                                RtOutputContext.Diagnostic | RtOutputContext.Ui,
                                $"  [cyan]{name}[/]  - [grey]{desc}[/]");
                        }
                        RtLog.Info("Or run: rt suggest \"<task in plain English>\"");
                        context.ExitCode = 1;
                        return Task.CompletedTask;
                    }
                }
                return next(context);
            }, MiddlewareOrder.Configuration)
            .AddMiddleware(async (context, next) =>
            {
                var md = context.ParseResult.GetValueForOption(markdownOpt);
                var yaml = context.ParseResult.GetValueForOption(yamlOpt);
                var json = context.ParseResult.GetValueForOption(jsonOpt);
                var llm = context.ParseResult.GetValueForOption(llmOpt);
                RtContext.Format = md ? OutputFormat.Markdown
                    : yaml ? OutputFormat.Yaml
                    : json ? OutputFormat.Json
                    : llm ? OutputFormat.Llm
                    : OutputFormat.Text;
                RtContext.RelaxJson = context.ParseResult.GetValueForOption(relaxJsonOpt);
                // --strict-json is now a no-op alias for the default behaviour: the converged JSON paths
                // (StructuredOutput, JsonRenderer, MCP handlers) always escape unless --relax-json is set.
                // Keep populating RtContext.StrictJson for un-migrated legacy renderers
                // (CommandSurfaceBuilder, RoslynTools.Cli.Commands.Results.*Result) that still branch on it.
                // When users explicitly pass --strict-json, emit a one-line deprecation note to stderr.
                RtContext.StrictJson = context.ParseResult.GetValueForOption(strictJsonOpt);
                if (context.ParseResult.FindResultFor(strictJsonOpt) is not null)
                    RtLog.Warning("--strict-json is deprecated; strict escaping is now the default. Pass --relax-json to opt out.");
                RtContext.ShowWorkspaceWarnings = context.ParseResult.GetValueForOption(showWorkspaceWarningsOpt);
                RtContext.NoSuggest = context.ParseResult.GetValueForOption(noSuggestOpt);

                var decisionsFile = context.ParseResult.GetValueForOption(decisionsOpt);
                if (decisionsFile is not null)
                {
                    try
                    {
                        // Load is inside the try so malformed YAML / missing file errors flow
                        // through TryHandleUserErrorAsync and get the clean exit-1 treatment.
                        DecisionContext.Store = DecisionStore.Load(decisionsFile.FullName);
                        await next(context);
                    }
                    catch (DecisionsRequiredException)
                    {
                        var store = DecisionContext.Store!;
                        store.Save(decisionsFile.FullName);
                        RtLog.Warning(
                            "wrote {PendingCount} pending decision(s) to {DecisionsFile}; resolve `choose:` fields and re-run.",
                            store.PendingCount,
                            decisionsFile.FullName);
                        context.ExitCode = 2;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (await TryHandleUserErrorAsync(context, ex))
                            return;
                        throw;
                    }
                    finally
                    {
                        DecisionContext.Store = null;
                    }
                }
                else
                {
                    try
                    {
                        await next(context);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (await TryHandleUserErrorAsync(context, ex))
                            return;
                        throw;
                    }
                }
            }, MiddlewareOrder.Configuration)
            .Build()
            .InvokeAsync(args);
    }

    /// <summary>
    /// Registers global command options with the root command.
    /// </summary>
    internal static (Option<bool> jsonOpt, Option<bool> llmOpt, Option<bool> markdownOpt, Option<bool> yamlOpt,
        Option<FileInfo?> decisionsOpt, Option<bool> strictJsonOpt, Option<bool> relaxJsonOpt,
        Option<bool> showWorkspaceWarningsOpt, Option<bool> noSuggestOpt)
        RegisterGlobalOptions(RootCommand root)
    {
        var jsonOpt = new Option<bool>("--json", "Emit results as JSON");
        var llmOpt = new Option<bool>("--llm", "Emit compact output for LLM consumption");
        var markdownOpt = new Option<bool>("--markdown", "Emit results as Markdown");
        var yamlOpt = new Option<bool>("--yaml", "Emit results as YAML");
        // --strict-json was the original opt-in for \uXXXX-escaping of non-ASCII chars. It is now the
        // default behaviour for the converged paths (StructuredOutput, JsonRenderer, MCP handlers), so the
        // flag is a no-op alias kept for back-compat with scripts/CI. A deprecation note is emitted when
        // it's passed explicitly.
        var strictJsonOpt = new Option<bool>("--strict-json",
            "Deprecated: strict escaping is now the default. Kept as a no-op alias for back-compat.");
        var relaxJsonOpt = new Option<bool>("--relax-json",
            "Emit raw non-ASCII and HTML-sensitive characters in JSON output "
            + "(JavaScriptEncoder.UnsafeRelaxedJsonEscaping). Default off keeps output strict-deserialiser safe.");
        var decisionsOpt = new Option<FileInfo?>(
            "--decisions",
            "Path to a YAML file recording disambiguation choices. "
            + "If unanswered entries exist (or new ones surface), rt writes them and exits 2.");
        var showWorkspaceWarningsOpt = new Option<bool>("--show-workspace-warnings",
            "Show MSBuild workspace warnings (NuGet audit, unresolved imports, etc.). Off by default.");
        var noSuggestOpt = new Option<bool>("--no-suggest",
            "Suppress 'Did you mean: ...' suggestion tails on drill-in not-found errors. Off by default; "
            + "useful for strict-output pipelines that pin error text.");
        foreach (var option in new Option[]
                     { jsonOpt, llmOpt, markdownOpt, yamlOpt, strictJsonOpt, relaxJsonOpt, decisionsOpt,
                         showWorkspaceWarningsOpt, noSuggestOpt })
            root.AddGlobalOption(option);

        return (jsonOpt, llmOpt, markdownOpt, yamlOpt, decisionsOpt, strictJsonOpt, relaxJsonOpt,
            showWorkspaceWarningsOpt, noSuggestOpt);
    }

    private static string TruncateOneLine(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var firstLine = text.Split('\n', 2)[0].Trim();
        return firstLine.Length <= max ? firstLine : firstLine[..(max - 1)] + "…";
    }

    private static IEnumerable<HelpSectionDelegate> BuildHelpLayout(HelpContext ctx)
    {
        if (ctx.Command is RootCommand)
        {
            var snapshot = Commands.Select(e => (Cmd: e.Build(), e.Info)).ToList();
            return [_ => DumpHelpCommand.RenderColoured(snapshot)];
        }

        if (GroupDescriptions.ContainsKey(ctx.Command.Name))
        {
            var cat = ctx.Command.Name;
            var snapshot = Commands
                .Where(e => e.Info.Category == cat)
                .Select(e => (Cmd: e.Build(), e.Info))
                .ToList();
            return [_ => DumpHelpCommand.RenderColouredGroup(cat, snapshot)];
        }

        var entry = Commands.FirstOrDefault(e =>
            e.Info.Name == ctx.Command.Name ||
            e.Info.Aliases?.Contains(ctx.Command.Name) == true);

        if (entry.Info is not null)
            return
            [
                c => DumpHelpCommand.RenderColouredCommandHeader(c, entry.Info),
                HelpBuilder.Default.OptionsSection()
            ];

        return HelpBuilder.Default.GetLayout();
    }

    private static async Task CaptureExceptionAsync(InvocationContext context, Exception ex)
    {
        var cmdLine = string.Join(" ", context.ParseResult.Tokens.Select(t => t.Value));
        var solutionPath = TryExtractSolutionPath(context);
        var parsedOptions = TryCaptureParsedOptions(context);
        await FeedbackStore.AppendExceptionAsync(cmdLine, ex, solutionPath, parsedOptions);
        RtLog.Warning("Exception captured — run `rt feedback list` to view.");
    }

    /// <summary>
    /// Translates known user-error exception types into a clean stderr line + exit 1 so the
    /// CLI never surfaces a raw stack trace for predictable failures: missing solution paths
    /// (FileNotFoundException), file I/O issues (IOException), invalid-input checks inside
    /// services (InvalidOperationException — e.g. analyze-interface on a non-interface),
    /// argument validation failures (ArgumentException), malformed JSON (JsonException —
    /// e.g. add-xml-doc --from-json), and malformed YAML (YamlException — e.g. --decisions file).
    /// These mapped paths intentionally skip
    /// <see cref="CaptureExceptionAsync"/> — they are documented, expected user-error UX
    /// flows; capturing them to feedback.json adds noise without adding signal (US-076).
    /// Unmapped exception types ARE captured, then return false so the caller rethrows and
    /// the developer sees the full stack alongside a triageable feedback entry.
    /// </summary>
    private static async Task<bool> TryHandleUserErrorAsync(InvocationContext context, Exception ex)
    {
        if (ex is FileNotFoundException fnf)
        {
            RtLog.Error("solution not found: {Path}", fnf.FileName ?? "(unknown)");
            context.ExitCode = 1;
            return true;
        }

        if (ex is IOException or InvalidOperationException or ArgumentException or JsonException or YamlException)
        {
            RtLog.Error("Error: {Message}", ex.Message);
            context.ExitCode = 1;
            return true;
        }

        await CaptureExceptionAsync(context, ex);
        return false;
    }

    private static string? TryExtractSolutionPath(InvocationContext context)
    {
        try
        {
            var tokens = context.ParseResult.Tokens.Select(t => t.Value).ToList();
            for (var i = 0; i < tokens.Count - 1; i++)
            {
                if (tokens[i] is "--solution" or "-s")
                    return tokens[i + 1];
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, string?>? TryCaptureParsedOptions(InvocationContext context)
    {
        try
        {
            var dict = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var opt in context.ParseResult.CommandResult.Command.Options)
            {
                var raw = context.ParseResult.FindResultFor(opt);
                if (raw is null) continue;
                var value = context.ParseResult.GetValueForOption(opt);
                dict[opt.Name] = value?.ToString();
            }
            return dict.Count == 0 ? null : dict;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     Set by <see cref="RunAsync" /> before any command is built; read by the closures
    ///     produced in <see cref="Discover" /> to resolve instance-based commands from DI.
    ///     Cleared in the <c>finally</c> of <see cref="RunAsync" />.
    /// </summary>
    private static IServiceProvider? _currentServices;

    private static List<(Func<Command>, RtCommandInfo)> Discover()
    {
        const BindingFlags staticFlags = BindingFlags.Static | BindingFlags.Public;
        const BindingFlags instanceFlags = BindingFlags.Instance | BindingFlags.Public;

        var implementers = typeof(App).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && !t.IsInterface && typeof(IRtCommand).IsAssignableFrom(t))
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

        var list = new List<(Func<Command>, RtCommandInfo)>(implementers.Length);
        foreach (var type in implementers)
        {
            var infoProp = type.GetProperty("Info", staticFlags)
                           ?? throw new InvalidOperationException(
                               $"{type.Name} implements IRtCommand but exposes no public static Info property.");
            var info = (RtCommandInfo)infoProp.GetValue(null)!;

            var staticBuild = type.GetMethod("Build", staticFlags);
            var instanceBuild = type.GetMethod("Build", instanceFlags);

            if (staticBuild is null && instanceBuild is null)
                throw new InvalidOperationException(
                    $"{type.Name} implements IRtCommand but exposes no public Build() — add either "
                    + "a `public static Command Build()` or a `public Command Build()` instance method.");

            // Capture in locals so the closure doesn't carry the whole iteration variable.
            var capturedType = type;
            var capturedStatic = staticBuild;
            var capturedInstance = instanceBuild;

            Func<Command> factory = capturedStatic is not null
                ? () => (Command)capturedStatic.Invoke(null, null)!
                : () =>
                {
                    // Prefer the SP scoped to the current RunAsync invocation. If callers reach
                    // App.Commands outside RunAsync (architecture tests, dump-help fast path),
                    // fall back to a lazily-built default SP so registry-only consumers don't
                    // crash. The default SP lifetime is process-scoped — never disposed.
                    var sp = _currentServices ?? GetOrCreateRegistryServices();
                    var instance = sp.GetRequiredService(capturedType);
                    return (Command)capturedInstance!.Invoke(instance, null)!;
                };

            list.Add((factory, info));
        }

        return list;
    }
}
