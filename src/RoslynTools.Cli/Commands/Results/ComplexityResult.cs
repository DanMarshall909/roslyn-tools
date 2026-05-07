using RoslynTools.Core.Output;

namespace RoslynTools.Commands.Results;

internal sealed record MethodComplexityItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "method")]
    public required string Method { get; init; }

    [OutputField(OutputLevel.Default, "type")]
    public required string Type { get; init; }

    [OutputField(OutputLevel.Default, "namespace")]
    public required string Namespace { get; init; }

    [OutputField(OutputLevel.Default, "file")]
    public required string File { get; init; }

    [OutputField(OutputLevel.Default, "line")]
    public required int Line { get; init; }

    [OutputField(OutputLevel.Default, "cyclomatic")]
    public required int Cyclomatic { get; init; }

    [OutputField(OutputLevel.Default, "cognitive")]
    public required int Cognitive { get; init; }

    [OutputField(OutputLevel.Default, "loc")]
    public required int Loc { get; init; }

    [OutputField(OutputLevel.Default, "params")]
    public required int Params { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write(
            $"{Fit(Method, 40),-40}{Fit(Type, 30),-30}{Cyclomatic,4}  {Cognitive,4}  {Loc,5}  {Params,3}  {File}:{Line}");
    }

    private static string Fit(string s, int width)
    {
        return s.Length <= width ? s : "…" + s[^(width - 1)..];
    }
}

internal sealed record TypeRollupItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "type")]
    public required string Type { get; init; }

    [OutputField(OutputLevel.Default, "namespace")]
    public required string Namespace { get; init; }

    [OutputField(OutputLevel.Default, "methodCount")]
    public required int MethodCount { get; init; }

    [OutputField(OutputLevel.Default, "cyclomaticTotal")]
    public required int CyclomaticTotal { get; init; }

    [OutputField(OutputLevel.Default, "cognitiveTotal")]
    public required int CognitiveTotal { get; init; }

    [OutputField(OutputLevel.Default, "locTotal")]
    public required int LocTotal { get; init; }

    [OutputField(OutputLevel.Default, "paramsTotal")]
    public required int ParamsTotal { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write(
            $"{Fit(Type, 40),-40}{Fit(Namespace, 40),-40}{MethodCount,4}  {CyclomaticTotal,6}  {CognitiveTotal,6}  {LocTotal,7}");
    }

    private static string Fit(string s, int width)
    {
        return s.Length <= width ? s : "…" + s[^(width - 1)..];
    }
}

internal sealed record NamespaceRollupItem : IRtOutput
{
    [OutputField(OutputLevel.Default, "namespace")]
    public required string Namespace { get; init; }

    [OutputField(OutputLevel.Default, "methodCount")]
    public required int MethodCount { get; init; }

    [OutputField(OutputLevel.Default, "cyclomaticTotal")]
    public required int CyclomaticTotal { get; init; }

    [OutputField(OutputLevel.Default, "cognitiveTotal")]
    public required int CognitiveTotal { get; init; }

    [OutputField(OutputLevel.Default, "locTotal")]
    public required int LocTotal { get; init; }

    [OutputField(OutputLevel.Default, "paramsTotal")]
    public required int ParamsTotal { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Fit(Namespace, 60),-60}{MethodCount,4}  {CyclomaticTotal,6}  {CognitiveTotal,6}  {LocTotal,7}");
    }

    private static string Fit(string s, int width)
    {
        return s.Length <= width ? s : "…" + s[^(width - 1)..];
    }
}

internal sealed record ComplexityResult : IRtOutput
{
    [OutputField(OutputLevel.Default, "methods")]
    public IReadOnlyList<MethodComplexityItem> Methods { get; init; } = [];

    [OutputField(OutputLevel.Default, "typeRollups")]
    public IReadOnlyList<TypeRollupItem> TypeRollups { get; init; } = [];

    [OutputField(OutputLevel.Default, "namespaceRollups")]
    public IReadOnlyList<NamespaceRollupItem> NamespaceRollups { get; init; } = [];

    public void RenderText(TextWriter writer)
    {
        if (Methods.Count == 0)
        {
            writer.WriteLine("No methods found.");
            return;
        }

        writer.WriteLine($"{"Method",-40}{"Type",-30}{"CC",4}  {"Cog",4}  {"LOC",5}  {"Par",3}  Location");
        writer.WriteLine(new string('-', 120));
        foreach (var m in Methods)
        {
            m.RenderText(writer);
            writer.WriteLine();
        }

        writer.WriteLine();
        writer.WriteLine("=== By Type ===");
        writer.WriteLine($"{"Type",-40}{"Namespace",-40}{"Mth",4}  {"CC",6}  {"Cog",6}  {"LOC",7}");
        writer.WriteLine(new string('-', 110));
        foreach (var t in TypeRollups)
        {
            t.RenderText(writer);
            writer.WriteLine();
        }

        writer.WriteLine();
        writer.WriteLine("=== By Namespace ===");
        writer.WriteLine($"{"Namespace",-60}{"Mth",4}  {"CC",6}  {"Cog",6}  {"LOC",7}");
        writer.WriteLine(new string('-', 90));
        foreach (var n in NamespaceRollups)
        {
            n.RenderText(writer);
            writer.WriteLine();
        }
    }
}
