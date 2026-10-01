namespace RoslynTools.Services;

internal sealed record MethodComplexityItem
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
}

internal sealed record TypeRollupItem
{
    public required string Type { get; init; }
    public required string Namespace { get; init; }
    public required int MethodCount { get; init; }
    public required int CyclomaticTotal { get; init; }
    public required int CognitiveTotal { get; init; }
    public required int LocTotal { get; init; }
    public required int ParamsTotal { get; init; }
}

internal sealed record NamespaceRollupItem
{
    public required string Namespace { get; init; }
    public required int MethodCount { get; init; }
    public required int CyclomaticTotal { get; init; }
    public required int CognitiveTotal { get; init; }
    public required int LocTotal { get; init; }
    public required int ParamsTotal { get; init; }
}

internal sealed record ComplexityResult
{
    public required string RollupScope { get; init; }
    public required int MatchingMethodCount { get; init; }
    public required int TotalTypeRollupCount { get; init; }
    public required int TotalNamespaceRollupCount { get; init; }
    public IReadOnlyList<MethodComplexityItem> Methods { get; init; } = [];
    public IReadOnlyList<TypeRollupItem> TypeRollups { get; init; } = [];
    public IReadOnlyList<NamespaceRollupItem> NamespaceRollups { get; init; } = [];
}
