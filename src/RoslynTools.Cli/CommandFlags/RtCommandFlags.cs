using System.CommandLine;
using System.CommandLine.Parsing;

namespace RoslynTools.Commands;

internal interface ISupportsSolutionOption
{
    string? SolutionPath { get; }
}

internal interface ISupportsDryRunOption
{
    bool DryRun { get; }
}

internal interface ISupportsTopOption
{
    int Top { get; }
}

internal interface ISupportsSymbolOption
{
    string? Symbol { get; }
}

internal interface ISupportsScopeOption
{
    string? Scope { get; }
}

internal interface ISupportsKindOption
{
    string? Kind { get; }
}

internal interface ISupportsTypeOption
{
    string? TypeName { get; }
}

internal interface ISupportsFileOption
{
    string? FilePath { get; }
}

internal interface ISupportsJsonOption
{
    bool Json { get; }
}

internal interface ISupportsFormatOption
{
    string? Format { get; }
}

internal interface ISupportsVerboseOption
{
    bool Verbose { get; }
}

internal interface ISupportsDecisionsOption
{
    string? DecisionsPath { get; }
}

internal interface ISupportsApplySkipOptions
{
    IReadOnlySet<string> ApplyIds { get; }
    IReadOnlySet<string> SkipIds { get; }
}

internal sealed record RtFlagValues(
    string? SolutionPath = null,
    bool DryRun = false,
    int Top = 0,
    string? Symbol = null,
    string? Scope = null,
    string? Kind = null,
    string? TypeName = null,
    string? FilePath = null,
    bool Json = false,
    string? Format = null,
    bool Verbose = false,
    string? DecisionsPath = null,
    IReadOnlySet<string>? ApplyIds = null,
    IReadOnlySet<string>? SkipIds = null)
    : ISupportsSolutionOption,
        ISupportsDryRunOption,
        ISupportsTopOption,
        ISupportsSymbolOption,
        ISupportsScopeOption,
        ISupportsKindOption,
        ISupportsTypeOption,
        ISupportsFileOption,
        ISupportsJsonOption,
        ISupportsFormatOption,
        ISupportsVerboseOption,
        ISupportsDecisionsOption,
        ISupportsApplySkipOptions
{
    public IReadOnlySet<string> ApplyIds { get; init; } = ApplyIds ?? new HashSet<string>(StringComparer.Ordinal);
    public IReadOnlySet<string> SkipIds { get; init; } = SkipIds ?? new HashSet<string>(StringComparer.Ordinal);
}

internal sealed class RtCommandFlagBinder
{
    private Option<string?>? _solutionOpt;
    private Option<bool>? _dryRunOpt;
    private Option<int>? _topOpt;
    private Option<string>? _symbolOpt;
    private Option<string?>? _scopeOpt;
    private Option<string?>? _kindOpt;
    private Option<string?>? _typeOpt;
    private Option<string?>? _fileOpt;
    private Option<bool>? _jsonOpt;
    private Option<string?>? _formatOpt;
    private Option<bool>? _verboseOpt;
    private Option<string?>? _decisionsOpt;
    private Option<string[]>? _applyOpt;
    private Option<string[]>? _skipOpt;

    public static RtCommandFlagBinder Create()
    {
        return new RtCommandFlagBinder();
    }

    public RtCommandFlagBinder WithSolution(string? description = null)
    {
        _solutionOpt = RtFlagCatalog.CreateSolutionOption(description);
        return this;
    }

    public RtCommandFlagBinder WithDryRun(string? description = null)
    {
        _dryRunOpt = RtFlagCatalog.CreateDryRunOption(description);
        return this;
    }

    public RtCommandFlagBinder WithTop(string? description = null)
    {
        _topOpt = RtFlagCatalog.CreateTopOption(description);
        return this;
    }

    public RtCommandFlagBinder WithSymbol(string description)
    {
        _symbolOpt = RtFlagCatalog.CreateSymbolOption(description);
        return this;
    }

    public RtCommandFlagBinder WithScope(string? description = null)
    {
        _scopeOpt = RtFlagCatalog.CreateScopeOption(description);
        return this;
    }

    public RtCommandFlagBinder WithKind(string? description = null)
    {
        _kindOpt = RtFlagCatalog.CreateKindOption(description);
        return this;
    }

    public RtCommandFlagBinder WithType(string? description = null)
    {
        _typeOpt = RtFlagCatalog.CreateTypeOption(description);
        return this;
    }

    public RtCommandFlagBinder WithFile(string? description = null)
    {
        _fileOpt = RtFlagCatalog.CreateFileOption(description);
        return this;
    }

    public RtCommandFlagBinder WithJson(string? description = null)
    {
        _jsonOpt = RtFlagCatalog.CreateJsonOption(description);
        return this;
    }

    public RtCommandFlagBinder WithFormat(string? description = null)
    {
        _formatOpt = RtFlagCatalog.CreateFormatOption(description);
        return this;
    }

    public RtCommandFlagBinder WithVerbose(string? description = null)
    {
        _verboseOpt = RtFlagCatalog.CreateVerboseOption(description);
        return this;
    }

    public RtCommandFlagBinder WithDecisions(string? description = null)
    {
        _decisionsOpt = RtFlagCatalog.CreateDecisionsOption(description);
        return this;
    }

    public RtCommandFlagBinder WithApplySkip(string? applyDescription = null, string? skipDescription = null)
    {
        _applyOpt = RtFlagCatalog.CreateApplyOption(applyDescription);
        _skipOpt = RtFlagCatalog.CreateSkipOption(skipDescription);
        return this;
    }

    public void AddTo(Command command)
    {
        AddIfPresent(command, _solutionOpt);
        AddIfPresent(command, _dryRunOpt);
        AddIfPresent(command, _topOpt);
        AddIfPresent(command, _symbolOpt);
        AddIfPresent(command, _scopeOpt);
        AddIfPresent(command, _kindOpt);
        AddIfPresent(command, _typeOpt);
        AddIfPresent(command, _fileOpt);
        AddIfPresent(command, _jsonOpt);
        AddIfPresent(command, _formatOpt);
        AddIfPresent(command, _verboseOpt);
        AddIfPresent(command, _decisionsOpt);
        AddIfPresent(command, _applyOpt);
        AddIfPresent(command, _skipOpt);
    }

    public RtFlagValues Bind(ParseResult parseResult)
    {
        var applyIds = ToSet(_applyOpt is null ? [] : parseResult.GetValueForOption(_applyOpt) ?? []);
        var skipIds = ToSet(_skipOpt is null ? [] : parseResult.GetValueForOption(_skipOpt) ?? []);

        var values = new RtFlagValues(
            _solutionOpt is null ? null : parseResult.GetValueForOption(_solutionOpt),
            _dryRunOpt is not null && parseResult.GetValueForOption(_dryRunOpt),
            _topOpt is null ? 0 : parseResult.GetValueForOption(_topOpt),
            _symbolOpt is null ? null : parseResult.GetValueForOption(_symbolOpt),
            _scopeOpt is null ? null : parseResult.GetValueForOption(_scopeOpt),
            _kindOpt is null ? null : parseResult.GetValueForOption(_kindOpt),
            _typeOpt is null ? null : parseResult.GetValueForOption(_typeOpt),
            _fileOpt is null ? null : parseResult.GetValueForOption(_fileOpt),
            _jsonOpt is not null && parseResult.GetValueForOption(_jsonOpt),
            _formatOpt is null ? null : parseResult.GetValueForOption(_formatOpt),
            _verboseOpt is not null && parseResult.GetValueForOption(_verboseOpt),
            _decisionsOpt is null ? null : parseResult.GetValueForOption(_decisionsOpt),
            applyIds,
            skipIds);

        if (_topOpt is not null && values.Top < 0)
            throw new InvalidOperationException("--top must be >= 0.");

        if (values.ApplyIds.Count > 0 && values.SkipIds.Count > 0)
            throw new InvalidOperationException("--apply and --skip are mutually exclusive.");

        return values;
    }

    public TRequest Bind<TRequest>(
        ParseResult parseResult,
        Func<RtFlagValues, TRequest> createRequest)
    {
        return createRequest(Bind(parseResult));
    }

    private static void AddIfPresent<T>(Command command, Option<T>? option)
    {
        if (option is not null)
            command.Add(option);
    }

    private static HashSet<string> ToSet(IEnumerable<string> values)
    {
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.Ordinal);
    }
}

internal sealed record RtFlagDefinition(
    string Name,
    Type ValueType,
    string Description,
    Func<Option> Create);

internal static class RtFlagCatalog
{
    private static readonly RtFlagDefinition[] DefinitionsInternal =
    [
        new("solution", typeof(string), "Path to .sln/.slnx", () => CreateSolutionOption()),
        new("dry-run", typeof(bool), "Preview without writing", () => CreateDryRunOption()),
        new("top", typeof(int), "Return only top N results (0 = all)", () => CreateTopOption()),
        new("symbol", typeof(string), "Symbol name or fully-qualified name", () => CreateSymbolOption("Symbol name or fully-qualified name")),
        new("scope", typeof(string), "Namespace/project prefix filter", () => CreateScopeOption()),
        new("kind", typeof(string), "Symbol kind hint", () => CreateKindOption()),
        new("type", typeof(string), "Type name or fully-qualified type name", () => CreateTypeOption()),
        new("file", typeof(string), "Path to a C# source file", () => CreateFileOption()),
        new("json", typeof(bool), "Emit JSON output", () => CreateJsonOption()),
        new("format", typeof(string), "Output format", () => CreateFormatOption()),
        new("verbose", typeof(bool), "Emit verbose diagnostic detail", () => CreateVerboseOption()),
        new("decisions", typeof(string), "Path to a decisions file", () => CreateDecisionsOption()),
        new("apply", typeof(string[]), "Only apply the selected decision ids", () => CreateApplyOption()),
        new("skip", typeof(string[]), "Skip the selected decision ids", () => CreateSkipOption())
    ];

    public static IReadOnlyList<RtFlagDefinition> Definitions { get; } = DefinitionsInternal;

    public static IReadOnlySet<string> CatalogOptionNames { get; } = DefinitionsInternal
        .Select(definition => definition.Name)
        .ToHashSet(StringComparer.Ordinal);

    public static bool IsCatalogOptionName(string name)
    {
        return CatalogOptionNames.Contains(name);
    }

    public static Option<string?> CreateSolutionOption(string? description = null)
    {
        return new Option<string?>("--solution", description ?? "Path to .sln/.slnx");
    }

    public static Option<bool> CreateDryRunOption(string? description = null)
    {
        return new Option<bool>("--dry-run", description ?? "Preview without writing");
    }

    public static Option<int> CreateTopOption(string? description = null)
    {
        return new Option<int>("--top", () => 0, description ?? "Return only top N results (0 = all)");
    }

    public static Option<string> CreateSymbolOption(string description)
    {
        return new Option<string>("--symbol", description) { IsRequired = true };
    }

    public static Option<string?> CreateScopeOption(string? description = null)
    {
        return new Option<string?>("--scope", description ?? "Namespace/project prefix filter");
    }

    public static Option<string?> CreateKindOption(string? description = null)
    {
        return new Option<string?>("--kind", description ?? "Symbol kind hint");
    }

    public static Option<string?> CreateTypeOption(string? description = null)
    {
        return new Option<string?>("--type", description ?? "Type name or fully-qualified type name");
    }

    public static Option<string?> CreateFileOption(string? description = null)
    {
        return new Option<string?>("--file", description ?? "Path to a C# source file");
    }

    public static Option<bool> CreateJsonOption(string? description = null)
    {
        return new Option<bool>("--json", description ?? "Emit JSON output");
    }

    public static Option<string?> CreateFormatOption(string? description = null)
    {
        return new Option<string?>("--format", description ?? "Output format");
    }

    public static Option<bool> CreateVerboseOption(string? description = null)
    {
        return new Option<bool>("--verbose", description ?? "Emit verbose diagnostic detail");
    }

    public static Option<string?> CreateDecisionsOption(string? description = null)
    {
        return new Option<string?>("--decisions", description ?? "Path to a decisions file");
    }

    public static Option<string[]> CreateApplyOption(string? description = null)
    {
        return new Option<string[]>("--apply", description ?? "Only apply the selected decision ids")
        {
            AllowMultipleArgumentsPerToken = true
        };
    }

    public static Option<string[]> CreateSkipOption(string? description = null)
    {
        return new Option<string[]>("--skip", description ?? "Skip the selected decision ids")
        {
            AllowMultipleArgumentsPerToken = true
        };
    }
}

internal static class RtFlagCapabilityMap
{
    public static IReadOnlyDictionary<string, Type> OptionToCapability { get; } = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        ["solution"] = typeof(ISupportsSolutionOption),
        ["dry-run"] = typeof(ISupportsDryRunOption),
        ["top"] = typeof(ISupportsTopOption),
        ["symbol"] = typeof(ISupportsSymbolOption),
        ["scope"] = typeof(ISupportsScopeOption),
        ["kind"] = typeof(ISupportsKindOption),
        ["type"] = typeof(ISupportsTypeOption),
        ["file"] = typeof(ISupportsFileOption),
        ["json"] = typeof(ISupportsJsonOption),
        ["format"] = typeof(ISupportsFormatOption),
        ["verbose"] = typeof(ISupportsVerboseOption),
        ["decisions"] = typeof(ISupportsDecisionsOption),
        ["apply"] = typeof(ISupportsApplySkipOptions),
        ["skip"] = typeof(ISupportsApplySkipOptions)
    };

    public static IReadOnlyDictionary<Type, string[]> CapabilityToOptions { get; } = OptionToCapability
        .GroupBy(kv => kv.Value)
        .ToDictionary(
            group => group.Key,
            group => group.Select(kv => kv.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray());
}
