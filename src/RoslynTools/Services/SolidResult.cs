namespace RoslynTools.Services;

internal sealed record SrpViolationItem
{
    public string TypeName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
    public int PublicMethodCount { get; init; }
    public int DependencyCount { get; init; }
    public string Detail { get; init; } = "";
}

internal sealed record IspFatInterfaceItem
{
    public string InterfaceName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
    public int MemberCount { get; init; }
}

internal sealed record IspUnimplementedMemberItem
{
    public string TypeName { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string InterfaceName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
}

internal sealed record IspBlock
{
    public IList<IspFatInterfaceItem> FatInterfaces { get; init; } = [];
    public IList<IspUnimplementedMemberItem> UnimplementedMembers { get; init; } = [];
}

internal sealed record DipConcreteCtorParamItem
{
    public string TypeName { get; init; } = "";
    public string ParamName { get; init; } = "";
    public string ConcreteType { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
}

internal sealed record DipInlineNewCallItem
{
    public string TypeName { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string ConcreteType { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
}

internal sealed record DipBlock
{
    public IList<DipConcreteCtorParamItem> ConcreteConstructorParams { get; init; } = [];
    public IList<DipInlineNewCallItem> InlineNewCalls { get; init; } = [];
}

internal sealed record OcpViolationItem
{
    public string TypeName { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
    public int BranchCount { get; init; }
    public string ViolationType { get; init; } = "";
}

internal sealed record LspThrowingOverrideItem
{
    public string TypeName { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string ExceptionType { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
}

internal sealed record LspContractNarrowingItem
{
    public string TypeName { get; init; } = "";
    public string MemberName { get; init; } = "";
    public string BaseType { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Line { get; init; }
}

internal sealed record LspBlock
{
    public IList<LspThrowingOverrideItem> ThrowingOverrides { get; init; } = [];
    public IList<LspContractNarrowingItem> ContractNarrowings { get; init; } = [];
}

internal sealed record SolidResult
{
    public IList<SrpViolationItem> SRP { get; init; } = [];
    public IspBlock ISP { get; init; } = new();
    public DipBlock DIP { get; init; } = new();
    public IList<OcpViolationItem> OCP { get; init; } = [];
    public LspBlock LSP { get; init; } = new();
}
