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

internal sealed record SolidResult : IRtOutput
{
    [OutputField(OutputLevel.Default, "SRP")]
    public IList<SrpViolationItem> SRP { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        if (SRP.Count == 0)
        {
            writer.WriteLine("No SRP violations found.");
            return;
        }

        foreach (var item in SRP)
        {
            item.RenderText(writer);
            writer.WriteLine();
        }
    }
}
