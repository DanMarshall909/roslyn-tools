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

internal sealed record RtFlagValues(
    string? SolutionPath = null,
    bool DryRun = false,
    int Top = 0,
    string? Symbol = null,
    string? Scope = null,
    string? Kind = null)
    : ISupportsSolutionOption,
        ISupportsDryRunOption,
        ISupportsTopOption,
        ISupportsSymbolOption,
        ISupportsScopeOption,
        ISupportsKindOption;

internal sealed class RtCommandFlagBinder
{
    private Option<string?>? _solutionOpt;
    private Option<bool>? _dryRunOpt;
    private Option<int>? _topOpt;
    private Option<string>? _symbolOpt;
    private Option<string?>? _scopeOpt;
    private Option<string?>? _kindOpt;

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

    public void AddTo(Command command)
    {
        AddIfPresent(command, _solutionOpt);
        AddIfPresent(command, _dryRunOpt);
        AddIfPresent(command, _topOpt);
        AddIfPresent(command, _symbolOpt);
        AddIfPresent(command, _scopeOpt);
        AddIfPresent(command, _kindOpt);
    }

    public RtFlagValues Bind(ParseResult parseResult)
    {
        var values = new RtFlagValues(
            _solutionOpt is null ? null : parseResult.GetValueForOption(_solutionOpt),
            _dryRunOpt is not null && parseResult.GetValueForOption(_dryRunOpt),
            _topOpt is null ? 0 : parseResult.GetValueForOption(_topOpt),
            _symbolOpt is null ? null : parseResult.GetValueForOption(_symbolOpt),
            _scopeOpt is null ? null : parseResult.GetValueForOption(_scopeOpt),
            _kindOpt is null ? null : parseResult.GetValueForOption(_kindOpt));

        if (_topOpt is not null && values.Top < 0)
            throw new InvalidOperationException("--top must be >= 0.");

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
}

internal static class RtFlagCatalog
{
    public static readonly IReadOnlySet<string> CatalogOptionNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "solution",
        "dry-run",
        "top",
        "symbol",
        "scope",
        "kind"
    };

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
}
