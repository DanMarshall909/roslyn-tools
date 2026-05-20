using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

internal sealed class SolidCommand : IRtCommand, IStructuredOutputCommand
{
    private SolidCommand()
    {
    }

    public static RtCommandInfo Info { get; } = new(
        "solid",
        Mutates: false,
        JsonOutput: true,
        Category: "analyze",
        WhenToUse:
        "Detect classes that may violate SOLID principles using heuristic analysis. --principle SRP flags classes with too many public methods and injected dependencies. --principle ISP flags fat interfaces and unimplemented members. --principle DIP flags concrete constructor params and inline new calls. --principle OCP flags switch/if chains that branch on concrete types. --principle LSP flags overrides and explicit interface impls that throw NotImplementedException. --principle all runs every active detector.",
        Examples:
        [
            "rt analyze solid --principle SRP --solution RoslynTools.slnx",
            "rt analyze solid --principle ISP --solution RoslynTools.slnx",
            "rt analyze solid --principle DIP --solution RoslynTools.slnx",
            "rt analyze solid --principle OCP --solution RoslynTools.slnx",
            "rt analyze solid --principle LSP --solution RoslynTools.slnx",
            "rt analyze solid --principle all --solution RoslynTools.slnx"
        ],
        SupportedFormats: [OutputFormat.Text, OutputFormat.Json]
    );

    public static Command Build()
    {
        var solutionOpt = CommandOptionFactory.CreateSolutionOption("Path to .sln/.slnx");
        var principleOpt = new Option<string>("--principle", () => "SRP",
            "SOLID principle to check: SRP, ISP, DIP, OCP, LSP, or all");
        var minMethodsOpt = new Option<int>("--min-methods", () => 10,
            "Minimum public method count to flag a class for SRP (default 10)");
        var minDepsOpt = new Option<int>("--min-deps", () => 5,
            "Minimum injected dependency count to flag a class for SRP (default 5)");
        var minMembersOpt = new Option<int>("--min-members", () => 7,
            "Minimum declared interface member count to flag a fat interface for ISP (default 7)");
        var minBranchesOpt = new Option<int>("--min-branches", () => 2,
            "Minimum concrete-type branches to flag a switch or if-chain for OCP (default 2)");

        var cmd = new Command("solid", "Detect SOLID principle violations using heuristic analysis");
        cmd.AddAll(solutionOpt, principleOpt, minMethodsOpt, minDepsOpt, minMembersOpt, minBranchesOpt);

        cmd.SetHandler(async context =>
        {
            var sp = context.BindingContext.GetService<IServiceProvider>();
            var loader = sp?.GetService<WorkspaceLoader>() ?? new WorkspaceLoader();
            context.ExitCode = await ExecuteAsync(
                context.ParseResult.GetValueForOption(solutionOpt),
                context.ParseResult.GetValueForOption(principleOpt)!,
                context.ParseResult.GetValueForOption(minMethodsOpt),
                context.ParseResult.GetValueForOption(minDepsOpt),
                context.ParseResult.GetValueForOption(minMembersOpt),
                context.ParseResult.GetValueForOption(minBranchesOpt),
                loader);
        });

        return cmd;
    }

    internal static async Task<int> ExecuteAsync(
        string? solution, string principle, int minMethods, int minDeps, int minMembers, int minBranches,
        WorkspaceLoader? loader = null)
    {
        if (minMethods < 0)
        {
            RtLog.Error("--min-methods must be >= 0.");
            return 1;
        }

        if (minDeps < 0)
        {
            RtLog.Error("--min-deps must be >= 0.");
            return 1;
        }

        if (minMembers < 0)
        {
            RtLog.Error("--min-members must be >= 0.");
            return 1;
        }

        if (minBranches < 0)
        {
            RtLog.Error("--min-branches must be >= 0.");
            return 1;
        }

        var normalised = principle.ToUpperInvariant();
        if (normalised is not ("SRP" or "ISP" or "DIP" or "OCP" or "LSP" or "ALL"))
        {
            RtLog.Error("--principle must be SRP, ISP, DIP, OCP, LSP, or all.");
            return 1;
        }

        var effectiveLoader = loader ?? new WorkspaceLoader();
        var sln = await effectiveLoader.GetSolutionAsync(solution);
        RtLog.Info("Analyzing SOLID principles...");

        var result = new SolidResult();
        var runSrp = normalised is "SRP" or "ALL";
        var runIsp = normalised is "ISP" or "ALL";
        var runDip = normalised is "DIP" or "ALL";
        var runOcp = normalised is "OCP" or "ALL";
        var runLsp = normalised is "LSP" or "ALL";

        if (runSrp)
        {
            var srpReport = await SolidAnalyzer.AnalyzeSrpAsync(sln, minMethods, minDeps);
            result = result with
            {
                SRP = srpReport.Violations.Select(v => new SrpViolationItem
                {
                    TypeName = v.TypeName,
                    FilePath = v.FilePath,
                    Line = v.Line,
                    PublicMethodCount = v.PublicMethodCount,
                    DependencyCount = v.DependencyCount,
                    Detail = v.Detail
                }).ToList()
            };
        }

        if (runIsp)
        {
            var ispReport = await SolidAnalyzer.AnalyzeIspAsync(sln, minMembers);
            result = result with
            {
                ISP = new IspBlock
                {
                    FatInterfaces = ispReport.FatInterfaces.Select(f => new IspFatInterfaceItem
                    {
                        InterfaceName = f.InterfaceName,
                        FilePath = f.FilePath,
                        Line = f.Line,
                        MemberCount = f.MemberCount
                    }).ToList(),
                    UnimplementedMembers = ispReport.UnimplementedMembers.Select(u => new IspUnimplementedMemberItem
                    {
                        TypeName = u.TypeName,
                        MemberName = u.MemberName,
                        InterfaceName = u.InterfaceName,
                        FilePath = u.FilePath,
                        Line = u.Line
                    }).ToList()
                }
            };
        }

        if (runDip)
        {
            var dipReport = await SolidAnalyzer.AnalyzeDipAsync(sln);
            result = result with
            {
                DIP = new DipBlock
                {
                    ConcreteConstructorParams = dipReport.ConcreteConstructorParams.Select(c => new DipConcreteCtorParamItem
                    {
                        TypeName = c.TypeName,
                        ParamName = c.ParamName,
                        ConcreteType = c.ConcreteType,
                        FilePath = c.FilePath,
                        Line = c.Line
                    }).ToList(),
                    InlineNewCalls = dipReport.InlineNewCalls.Select(n => new DipInlineNewCallItem
                    {
                        TypeName = n.TypeName,
                        MemberName = n.MemberName,
                        ConcreteType = n.ConcreteType,
                        FilePath = n.FilePath,
                        Line = n.Line
                    }).ToList()
                }
            };
        }

        if (runOcp)
        {
            var ocpReport = await SolidAnalyzer.AnalyzeOcpAsync(sln, minBranches);
            result = result with
            {
                OCP = ocpReport.Violations.Select(v => new OcpViolationItem
                {
                    TypeName = v.TypeName,
                    MemberName = v.MemberName,
                    FilePath = v.FilePath,
                    Line = v.Line,
                    BranchCount = v.BranchCount,
                    ViolationType = v.ViolationType
                }).ToList()
            };
        }

        if (runLsp)
        {
            var lspReport = await SolidAnalyzer.AnalyzeLspAsync(sln);
            result = result with
            {
                LSP = new LspBlock
                {
                    ThrowingOverrides = lspReport.ThrowingOverrides.Select(t => new LspThrowingOverrideItem
                    {
                        TypeName = t.TypeName,
                        MemberName = t.MemberName,
                        ExceptionType = t.ExceptionType,
                        FilePath = t.FilePath,
                        Line = t.Line
                    }).ToList(),
                    ContractNarrowings = lspReport.ContractNarrowings.Select(c => new LspContractNarrowingItem
                    {
                        TypeName = c.TypeName,
                        MemberName = c.MemberName,
                        BaseType = c.BaseType,
                        FilePath = c.FilePath,
                        Line = c.Line
                    }).ToList()
                }
            };
        }

        RtResultRenderer.WriteOutput(RtResult<SolidResult>.Ok(result));
        if (runSrp)
            RtLog.Info("Found {Count} SRP violation(s).", result.SRP.Count);
        if (runIsp)
            RtLog.Info("Found {Fat} fat interface(s) and {Unimpl} unimplemented interface member(s).",
                result.ISP.FatInterfaces.Count, result.ISP.UnimplementedMembers.Count);
        if (runDip)
            RtLog.Info("Found {Ctor} concrete ctor param(s) and {New} inline new call(s).",
                result.DIP.ConcreteConstructorParams.Count, result.DIP.InlineNewCalls.Count);
        if (runOcp)
            RtLog.Info("Found {Count} OCP violation(s).", result.OCP.Count);
        if (runLsp)
            RtLog.Info("Found {Throw} throwing override(s) and {Narrow} contract narrowing(s).",
                result.LSP.ThrowingOverrides.Count, result.LSP.ContractNarrowings.Count);
        return 0;
    }
}
