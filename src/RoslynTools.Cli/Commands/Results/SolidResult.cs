using RoslynTools.Core.Output;

namespace RoslynTools.Commands.Results;

internal sealed record SrpViolationItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "typeName")]
    public string TypeName { get; init; } = "";

    [OutputField(OutputLevel.Default, "filePath")]
    public string FilePath { get; init; } = "";

    [OutputField(OutputLevel.Default, "line")]
    public int Line { get; init; }

    [OutputField(OutputLevel.Default, "publicMethodCount")]
    public int PublicMethodCount { get; init; }

    [OutputField(OutputLevel.Default, "dependencyCount")]
    public int DependencyCount { get; init; }

    [OutputField(OutputLevel.Default, "detail")]
    public string Detail { get; init; } = "";

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{TypeName,-50} {PublicMethodCount,5} methods  {DependencyCount,3} deps  {FilePath}:{Line}");
    }
}

internal sealed record IspFatInterfaceItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "interfaceName")]
    public string InterfaceName { get; init; } = "";

    [OutputField(OutputLevel.Default, "filePath")]
    public string FilePath { get; init; } = "";

    [OutputField(OutputLevel.Default, "line")]
    public int Line { get; init; }

    [OutputField(OutputLevel.Default, "memberCount")]
    public int MemberCount { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{InterfaceName,-50} {MemberCount,3} members  {FilePath}:{Line}");
    }
}

internal sealed record IspUnimplementedMemberItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "typeName")]
    public string TypeName { get; init; } = "";

    [OutputField(OutputLevel.Default, "memberName")]
    public string MemberName { get; init; } = "";

    [OutputField(OutputLevel.Default, "interfaceName")]
    public string InterfaceName { get; init; } = "";

    [OutputField(OutputLevel.Default, "filePath")]
    public string FilePath { get; init; } = "";

    [OutputField(OutputLevel.Default, "line")]
    public int Line { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{TypeName}.{MemberName} (from {InterfaceName})  {FilePath}:{Line}");
    }
}

internal sealed record IspBlock : IRtOutput
{
    [OutputField(OutputLevel.Default, "FatInterfaces")]
    public IList<IspFatInterfaceItem> FatInterfaces { get; init; } = [];

    [OutputField(OutputLevel.Default, "UnimplementedMembers")]
    public IList<IspUnimplementedMemberItem> UnimplementedMembers { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        if (FatInterfaces.Count == 0 && UnimplementedMembers.Count == 0)
        {
            writer.WriteLine("No ISP violations found.");
            return;
        }

        foreach (var item in FatInterfaces)
        {
            item.RenderText(writer);
            writer.WriteLine();
        }
        foreach (var item in UnimplementedMembers)
        {
            item.RenderText(writer);
            writer.WriteLine();
        }
    }
}

internal sealed record DipConcreteCtorParamItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "typeName")]
    public string TypeName { get; init; } = "";

    [OutputField(OutputLevel.Default, "paramName")]
    public string ParamName { get; init; } = "";

    [OutputField(OutputLevel.Default, "concreteType")]
    public string ConcreteType { get; init; } = "";

    [OutputField(OutputLevel.Default, "filePath")]
    public string FilePath { get; init; } = "";

    [OutputField(OutputLevel.Default, "line")]
    public int Line { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{TypeName}({ParamName}: {ConcreteType})  {FilePath}:{Line}");
    }
}

internal sealed record DipInlineNewCallItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "typeName")]
    public string TypeName { get; init; } = "";

    [OutputField(OutputLevel.Default, "memberName")]
    public string MemberName { get; init; } = "";

    [OutputField(OutputLevel.Default, "concreteType")]
    public string ConcreteType { get; init; } = "";

    [OutputField(OutputLevel.Default, "filePath")]
    public string FilePath { get; init; } = "";

    [OutputField(OutputLevel.Default, "line")]
    public int Line { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{TypeName}.{MemberName} new {ConcreteType}()  {FilePath}:{Line}");
    }
}

internal sealed record DipBlock : IRtOutput
{
    [OutputField(OutputLevel.Default, "ConcreteConstructorParams")]
    public IList<DipConcreteCtorParamItem> ConcreteConstructorParams { get; init; } = [];

    [OutputField(OutputLevel.Default, "InlineNewCalls")]
    public IList<DipInlineNewCallItem> InlineNewCalls { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        if (ConcreteConstructorParams.Count == 0 && InlineNewCalls.Count == 0)
        {
            writer.WriteLine("No DIP violations found.");
            return;
        }

        foreach (var item in ConcreteConstructorParams)
        {
            item.RenderText(writer);
            writer.WriteLine();
        }
        foreach (var item in InlineNewCalls)
        {
            item.RenderText(writer);
            writer.WriteLine();
        }
    }
}

internal sealed record SolidResult : IRtOutput
{
    [OutputField(OutputLevel.Default, "SRP")]
    public IList<SrpViolationItem> SRP { get; init; } = [];

    [OutputField(OutputLevel.Default, "ISP")]
    public IspBlock ISP { get; init; } = new();

    [OutputField(OutputLevel.Default, "DIP")]
    public DipBlock DIP { get; init; } = new();

    public void RenderText(TextWriter writer)
    {
        if (SRP.Count == 0
            && ISP.FatInterfaces.Count == 0 && ISP.UnimplementedMembers.Count == 0
            && DIP.ConcreteConstructorParams.Count == 0 && DIP.InlineNewCalls.Count == 0)
        {
            writer.WriteLine("No SOLID violations found.");
            return;
        }

        foreach (var item in SRP)
        {
            item.RenderText(writer);
            writer.WriteLine();
        }
        ISP.RenderText(writer);
        DIP.RenderText(writer);
    }
}
