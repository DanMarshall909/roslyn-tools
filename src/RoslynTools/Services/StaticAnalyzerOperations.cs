using System.Text.Json.Serialization;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Services.Results;

namespace RoslynTools.Services;

internal sealed record ComplexityRequest(
    string? Solution = null,
    int Threshold = 0,
    string Sort = "cyclomatic",
    int Top = 0);

internal static class ComplexitySortValues
{
    internal static readonly string[] All = ["cyclomatic", "cognitive", "loc", "params"];
}

internal sealed class ComplexityOperation(IWorkspaceSessionFactory sessions)
    : CommandApplicationPort<ComplexityRequest, ComplexityResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;

    public override ApplicationCommandDefinition Definition => ComplexityCommandDefinition.Definition;

    public override async Task<Result<ComplexityResult, RtError>> ExecuteAsync(
        ComplexityRequest request,
        CancellationToken cancellationToken = default)
    {
        var error = Validate(request);
        if (error is not null)
            return Result<ComplexityResult, RtError>.Err(error);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
            cancellationToken.ThrowIfCancellationRequested();
            using (workspace)
            {
                var report = await ComplexityAnalyzer.AnalyzeAsync(
                    solution,
                    request.Threshold,
                    request.Sort,
                    request.Top,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return Result<ComplexityResult, RtError>.Ok(Map(report));
            }
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            return Result<ComplexityResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, exception.Message));
        }
    }

    private static RtError? Validate(ComplexityRequest request)
    {
        if (request.Threshold < 0)
            return new RtError(RtErrorKind.InvalidInput, "--threshold must be >= 0.");
        if (request.Top < 0)
            return new RtError(RtErrorKind.InvalidInput, "--top must be >= 0.");
        if (!ComplexitySortValues.All.Contains(request.Sort, StringComparer.OrdinalIgnoreCase))
            return new RtError(
                RtErrorKind.InvalidInput,
                "--sort must be one of: cyclomatic, cognitive, loc, params.");

        return null;
    }

    private static ComplexityResult Map(ComplexityReport report) => new()
    {
        Methods = report.Methods.Select(method => new MethodComplexityItem
        {
            Method = method.Method,
            Type = method.Type,
            Namespace = method.Namespace,
            File = method.File,
            Line = method.Line,
            Cyclomatic = method.Cyclomatic,
            Cognitive = method.Cognitive,
            Loc = method.Loc,
            Params = method.Params
        }).ToList(),
        TypeRollups = report.TypeRollups.Select(type => new TypeRollupItem
        {
            Type = type.Type,
            Namespace = type.Namespace,
            MethodCount = type.MethodCount,
            CyclomaticTotal = type.CyclomaticTotal,
            CognitiveTotal = type.CognitiveTotal,
            LocTotal = type.LocTotal,
            ParamsTotal = type.ParamsTotal
        }).ToList(),
        NamespaceRollups = report.NamespaceRollups.Select(item => new NamespaceRollupItem
        {
            Namespace = item.Namespace,
            MethodCount = item.MethodCount,
            CyclomaticTotal = item.CyclomaticTotal,
            CognitiveTotal = item.CognitiveTotal,
            LocTotal = item.LocTotal,
            ParamsTotal = item.ParamsTotal
        }).ToList()
    };
}

internal sealed record ListSmellsRequest(
    string? Solution = null,
    string? Scope = null,
    int Top = 0,
    int MaxClassLines = 300,
    int MaxMethodLines = 40,
    int MaxParams = 7);

internal sealed class ListSmellsOperation(IWorkspaceSessionFactory sessions)
    : CommandApplicationPort<ListSmellsRequest, ListSmellsResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;

    public override ApplicationCommandDefinition Definition => ListSmellsCommandDefinition.Definition;

    public override async Task<Result<ListSmellsResult, RtError>> ExecuteAsync(
        ListSmellsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (new[] { request.Top, request.MaxClassLines, request.MaxMethodLines, request.MaxParams }
            .Any(value => value < 0))
            return Result<ListSmellsResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Smell limits must be >= 0."));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
            cancellationToken.ThrowIfCancellationRequested();
            using (workspace)
            {
                var smells = await SmellAnalyzer.AnalyzeAsync(
                    solution,
                    request.MaxClassLines,
                    request.MaxMethodLines,
                    request.MaxParams,
                    request.Scope);
                cancellationToken.ThrowIfCancellationRequested();
                var selected = request.Top > 0 ? smells.Take(request.Top) : smells;
                return Result<ListSmellsResult, RtError>.Ok(new ListSmellsResult
                {
                    Smells = selected.Select(smell => new SmellItemResult
                    {
                        SmellType = smell.SmellType,
                        TypeName = smell.TypeName,
                        MemberName = smell.MemberName,
                        FilePath = smell.FilePath ?? string.Empty,
                        Line = smell.Line,
                        Detail = smell.Detail,
                        SuggestedFixes = FixCatalog.ForSmell(
                            smell.SmellType,
                            smell.TypeName,
                            smell.FilePath,
                            smell.Line,
                            solution.FilePath)
                    }).ToList()
                });
            }
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            return Result<ListSmellsResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, exception.Message));
        }
    }
}

internal sealed record SolidRequest(
    string? Solution = null,
    string Principle = "SRP",
    int MinMethods = 10,
    int MinDeps = 5,
    int MinMembers = 7,
    int MinBranches = 2);

internal sealed class SolidOperation(IWorkspaceSessionFactory sessions)
    : CommandApplicationPort<SolidRequest, SolidResult>, IRtService
{
    private readonly IWorkspaceSessionFactory _sessions = sessions;

    public override ApplicationCommandDefinition Definition => SolidCommandDefinition.Definition;

    public override async Task<Result<SolidResult, RtError>> ExecuteAsync(
        SolidRequest request,
        CancellationToken cancellationToken = default)
    {
        var principle = request.Principle.ToUpperInvariant();
        if (principle is not ("SRP" or "ISP" or "DIP" or "OCP" or "LSP" or "ALL")
            || new[] { request.MinMethods, request.MinDeps, request.MinMembers, request.MinBranches }
                .Any(value => value < 0))
            return Result<SolidResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Invalid SOLID principle or threshold."));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (workspace, solution) = await _sessions.LoadWorkspaceAsync(request.Solution);
            cancellationToken.ThrowIfCancellationRequested();
            using (workspace)
            {
                var result = new SolidResult();
                if (principle is "SRP" or "ALL")
                    result = result with
                    {
                        SRP = await AnalyzeSrpAsync(solution, request.MinMethods, request.MinDeps)
                    };
                if (principle is "ISP" or "ALL")
                    result = result with { ISP = await AnalyzeIspAsync(solution, request.MinMembers) };
                if (principle is "DIP" or "ALL")
                    result = result with { DIP = await AnalyzeDipAsync(solution) };
                if (principle is "OCP" or "ALL")
                    result = result with
                    {
                        OCP = await AnalyzeOcpAsync(solution, request.MinBranches)
                    };
                if (principle is "LSP" or "ALL")
                    result = result with { LSP = await AnalyzeLspAsync(solution) };

                cancellationToken.ThrowIfCancellationRequested();
                return Result<SolidResult, RtError>.Ok(result);
            }
        }
        catch (Exception exception)
            when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            return Result<SolidResult, RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, exception.Message));
        }
    }

    internal static async Task<IList<SrpViolationItem>> AnalyzeSrpAsync(
        Microsoft.CodeAnalysis.Solution solution,
        int minMethods,
        int minDependencies)
    {
        var report = await SolidAnalyzer.AnalyzeSrpAsync(solution, minMethods, minDependencies);
        return report.Violations.Select(item => new SrpViolationItem
        {
            TypeName = item.TypeName,
            FilePath = item.FilePath,
            Line = item.Line,
            PublicMethodCount = item.PublicMethodCount,
            DependencyCount = item.DependencyCount,
            Detail = item.Detail
        }).ToList();
    }

    internal static async Task<IspBlock> AnalyzeIspAsync(
        Microsoft.CodeAnalysis.Solution solution,
        int minMembers)
    {
        var report = await SolidAnalyzer.AnalyzeIspAsync(solution, minMembers);
        return new IspBlock
        {
            FatInterfaces = report.FatInterfaces.Select(item => new IspFatInterfaceItem
            {
                InterfaceName = item.InterfaceName,
                FilePath = item.FilePath,
                Line = item.Line,
                MemberCount = item.MemberCount
            }).ToList(),
            UnimplementedMembers = report.UnimplementedMembers.Select(
                item => new IspUnimplementedMemberItem
                {
                    TypeName = item.TypeName,
                    MemberName = item.MemberName,
                    InterfaceName = item.InterfaceName,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList()
        };
    }

    internal static async Task<DipBlock> AnalyzeDipAsync(Microsoft.CodeAnalysis.Solution solution)
    {
        var report = await SolidAnalyzer.AnalyzeDipAsync(solution);
        return new DipBlock
        {
            ConcreteConstructorParams = report.ConcreteConstructorParams.Select(
                item => new DipConcreteCtorParamItem
                {
                    TypeName = item.TypeName,
                    ParamName = item.ParamName,
                    ConcreteType = item.ConcreteType,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList(),
            InlineNewCalls = report.InlineNewCalls.Select(item => new DipInlineNewCallItem
            {
                TypeName = item.TypeName,
                MemberName = item.MemberName,
                ConcreteType = item.ConcreteType,
                FilePath = item.FilePath,
                Line = item.Line
            }).ToList()
        };
    }

    internal static async Task<IList<OcpViolationItem>> AnalyzeOcpAsync(
        Microsoft.CodeAnalysis.Solution solution,
        int minBranches)
    {
        var report = await SolidAnalyzer.AnalyzeOcpAsync(solution, minBranches);
        return report.Violations.Select(item => new OcpViolationItem
        {
            TypeName = item.TypeName,
            MemberName = item.MemberName,
            FilePath = item.FilePath,
            Line = item.Line,
            BranchCount = item.BranchCount,
            ViolationType = item.ViolationType
        }).ToList();
    }

    internal static async Task<LspBlock> AnalyzeLspAsync(Microsoft.CodeAnalysis.Solution solution)
    {
        var report = await SolidAnalyzer.AnalyzeLspAsync(solution);
        return new LspBlock
        {
            ThrowingOverrides = report.ThrowingOverrides.Select(
                item => new LspThrowingOverrideItem
                {
                    TypeName = item.TypeName,
                    MemberName = item.MemberName,
                    ExceptionType = item.ExceptionType,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList(),
            ContractNarrowings = report.ContractNarrowings.Select(
                item => new LspContractNarrowingItem
                {
                    TypeName = item.TypeName,
                    MemberName = item.MemberName,
                    BaseType = item.BaseType,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList()
        };
    }
}

internal static class ComplexityCommandDefinition
{
    internal const string Name = "complexity";

    internal static RtCommandInfo Info { get; } = new(
        "complexity",
        false,
        true,
        "analyze",
        Aliases: ["cx"],
        WhenToUse:
        "Use this instead of Read when measuring cyclomatic and cognitive complexity, lines of code, and parameter counts for methods in a solution. Use --threshold to surface only the most complex methods, --sort to rank by the metric of interest, and --top to limit output.",
        Examples:
        [
            "rt analyze complexity --solution RoslynTools.slnx",
            "rt analyze cx --threshold 10 --sort cyclomatic --solution RoslynTools.slnx",
            "rt analyze complexity --threshold 5 --sort cognitive --top 20 --solution C:\\code\\libraries\\ProjectX.Libraries.sln"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ComplexityRequest),
        typeof(ComplexityResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}

internal static class ListSmellsCommandDefinition
{
    internal const string Name = "list_smells";

    internal static RtCommandInfo Info { get; } = new(
        "list-smells",
        false,
        false,
        "analyze",
        Aliases: ["smells"],
        WhenToUse:
        "Use this instead of Read when getting a prioritised list of code smells — oversized classes, long methods, and constructors with too many parameters — to guide a refactor session. Use --scope to focus on your domain code and skip generated or test noise.",
        Examples:
        [
            "rt analyze list-smells --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze smells --scope ProjectX.Core --max-class-lines 200 --max-method-lines 30 --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt analyze list-smells --solution RoslynTools.slnx --max-params 5",
            "rt analyze list-smells --solution RoslynTools.slnx --scope RoslynTools --top 30"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json, OutputFormat.Markdown, OutputFormat.Llm]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(ListSmellsRequest),
        typeof(ListSmellsResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}

internal static class SolidCommandDefinition
{
    internal const string Name = "solid";

    internal static RtCommandInfo Info { get; } = new(
        "solid",
        false,
        true,
        "analyze",
        WhenToUse:
        "Use this instead of Read when detecting classes that may violate SOLID principles using heuristic analysis. --principle SRP flags classes with too many public methods and injected dependencies. --principle ISP flags fat interfaces and unimplemented members. --principle DIP flags concrete constructor params and inline new calls. --principle OCP flags switch/if chains that branch on concrete types. --principle LSP flags overrides and explicit interface impls that throw NotImplementedException. --principle all runs every active detector.",
        Examples:
        [
            "rt analyze solid --principle SRP --solution RoslynTools.slnx",
            "rt analyze solid --principle ISP --solution RoslynTools.slnx",
            "rt analyze solid --principle DIP --solution RoslynTools.slnx",
            "rt analyze solid --principle OCP --solution RoslynTools.slnx",
            "rt analyze solid --principle LSP --solution RoslynTools.slnx",
            "rt analyze solid --principle all --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]);

    internal static ApplicationCommandDefinition Definition { get; } = new(
        Info,
        typeof(SolidRequest),
        typeof(SolidResult),
        true,
        CommandSafety.ReadOnly,
        CommandExecutionPlacement.DaemonPreferred,
        []);
}
