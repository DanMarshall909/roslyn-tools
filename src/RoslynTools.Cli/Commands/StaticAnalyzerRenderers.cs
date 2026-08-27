using CliResults = RoslynTools.Commands.Results;
using ServiceResults = RoslynTools.Services;

namespace RoslynTools.Commands;

/// <summary>Projects host-neutral static-analysis responses into the established CLI renderers.</summary>
internal static class StaticAnalyzerResultAdapters
{
    internal static CliResults.ComplexityResult Complexity(
        ServiceResults.ComplexityResult value) => new()
        {
            Methods = value.Methods.Select(method => new CliResults.MethodComplexityItem
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
            TypeRollups = value.TypeRollups.Select(type => new CliResults.TypeRollupItem
            {
                Type = type.Type,
                Namespace = type.Namespace,
                MethodCount = type.MethodCount,
                CyclomaticTotal = type.CyclomaticTotal,
                CognitiveTotal = type.CognitiveTotal,
                LocTotal = type.LocTotal,
                ParamsTotal = type.ParamsTotal
            }).ToList(),
            NamespaceRollups = value.NamespaceRollups.Select(item => new CliResults.NamespaceRollupItem
            {
                Namespace = item.Namespace,
                MethodCount = item.MethodCount,
                CyclomaticTotal = item.CyclomaticTotal,
                CognitiveTotal = item.CognitiveTotal,
                LocTotal = item.LocTotal,
                ParamsTotal = item.ParamsTotal
            }).ToList()
        };

    internal static CliResults.SolidResult Solid(ServiceResults.SolidResult value) => new()
    {
        SRP = value.SRP.Select(item => new CliResults.SrpViolationItem
        {
            TypeName = item.TypeName,
            FilePath = item.FilePath,
            Line = item.Line,
            PublicMethodCount = item.PublicMethodCount,
            DependencyCount = item.DependencyCount,
            Detail = item.Detail
        }).ToList(),
        OCP = value.OCP.Select(item => new CliResults.OcpViolationItem
        {
            TypeName = item.TypeName,
            MemberName = item.MemberName,
            FilePath = item.FilePath,
            Line = item.Line,
            BranchCount = item.BranchCount,
            ViolationType = item.ViolationType
        }).ToList(),
        ISP = new CliResults.IspBlock
        {
            FatInterfaces = value.ISP.FatInterfaces.Select(item => new CliResults.IspFatInterfaceItem
            {
                InterfaceName = item.InterfaceName,
                FilePath = item.FilePath,
                Line = item.Line,
                MemberCount = item.MemberCount
            }).ToList(),
            UnimplementedMembers = value.ISP.UnimplementedMembers.Select(
                item => new CliResults.IspUnimplementedMemberItem
                {
                    TypeName = item.TypeName,
                    MemberName = item.MemberName,
                    InterfaceName = item.InterfaceName,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList()
        },
        DIP = new CliResults.DipBlock
        {
            ConcreteConstructorParams = value.DIP.ConcreteConstructorParams.Select(
                item => new CliResults.DipConcreteCtorParamItem
                {
                    TypeName = item.TypeName,
                    ParamName = item.ParamName,
                    ConcreteType = item.ConcreteType,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList(),
            InlineNewCalls = value.DIP.InlineNewCalls.Select(item => new CliResults.DipInlineNewCallItem
            {
                TypeName = item.TypeName,
                MemberName = item.MemberName,
                ConcreteType = item.ConcreteType,
                FilePath = item.FilePath,
                Line = item.Line
            }).ToList()
        },
        LSP = new CliResults.LspBlock
        {
            ThrowingOverrides = value.LSP.ThrowingOverrides.Select(
                item => new CliResults.LspThrowingOverrideItem
                {
                    TypeName = item.TypeName,
                    MemberName = item.MemberName,
                    ExceptionType = item.ExceptionType,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList(),
            ContractNarrowings = value.LSP.ContractNarrowings.Select(
                item => new CliResults.LspContractNarrowingItem
                {
                    TypeName = item.TypeName,
                    MemberName = item.MemberName,
                    BaseType = item.BaseType,
                    FilePath = item.FilePath,
                    Line = item.Line
                }).ToList()
        }
    };

    internal static CliResults.ListSmellsResult ListSmells(
        ServiceResults.ListSmellsResult value) => new()
        {
            Smells = value.Smells.Select(item => new CliResults.SmellItemResult
            {
                SmellType = item.SmellType,
                TypeName = item.TypeName,
                MemberName = item.MemberName,
                FilePath = item.FilePath,
                Line = item.Line,
                Detail = item.Detail,
                SuggestedFixes = item.SuggestedFixes
            }).ToList()
        };
}
