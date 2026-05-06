using RoslynTools.Core.Output;

namespace RoslynTools.Commands.Results;

internal sealed record MethodComplexityItem : IRtOutput
{
    public required string Method { get; init; }
    public required string Type { get; init; }
    public required string Namespace { get; init; }
    public required string File { get; init; }
    public required int Line { get; init; }
    public required int Cyclomatic { get; init; }
    public required int Cognitive { get; init; }
    public required int Loc { get; init; }
    public required int Params { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Method,-40}{Type,-30}{Cyclomatic,4}  {Cognitive,4}  {Loc,5}  {Params,3}  {File}:{Line}");
    }
}

internal sealed record TypeRollupItem : IRtOutput
{
    public required string Type { get; init; }
    public required string Namespace { get; init; }
    public required int MethodCount { get; init; }
    public required int CyclomaticTotal { get; init; }
    public required int CognitiveTotal { get; init; }
    public required int LocTotal { get; init; }
    public required int ParamsTotal { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Type,-40}{Namespace,-40}{MethodCount,4}  {CyclomaticTotal,6}  {CognitiveTotal,6}  {LocTotal,7}");
    }
}

internal sealed record NamespaceRollupItem : IRtOutput
{
    public required string Namespace { get; init; }
    public required int MethodCount { get; init; }
    public required int CyclomaticTotal { get; init; }
    public required int CognitiveTotal { get; init; }
    public required int LocTotal { get; init; }
    public required int ParamsTotal { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"{Namespace,-60}{MethodCount,4}  {CyclomaticTotal,6}  {CognitiveTotal,6}  {LocTotal,7}");
    }
}

internal sealed record ComplexityResult : IRtOutput
{
    public IReadOnlyList<MethodComplexityItem> Methods { get; init; } = [];
    public IReadOnlyList<TypeRollupItem> TypeRollups { get; init; } = [];
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
