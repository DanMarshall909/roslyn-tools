namespace RoslynTools.Services.Results;

internal sealed record ProbeDiFinding
{
    internal const string UnprobeableReason = "The registration uses an implementation factory and has no ImplementationType.";
    internal const string InvisibleReason = "The implementation is registered at runtime but has no source reference or reflective-use annotation.";
    public required string ServiceType { get; init; }
    public string? ImplementationType { get; init; }
    public required string Status { get; init; }
    public required string Reason { get; init; }
    public SuggestedFix? SuggestedFix { get; init; }

    internal static ProbeDiFinding From(ProbeDiRegistration registration, string? root, string? solutionPath) =>
        new()
        {
            ServiceType = registration.ServiceType,
            ImplementationType = registration.ImplementationType,
            Status = registration.Status,
            Reason = registration.Status == ProbeDiStatus.Invisible ? InvisibleReason : UnprobeableReason,
            SuggestedFix = registration.Status == ProbeDiStatus.Invisible
                ? FixCatalog.ForDynamicDependency(registration.ImplementationType, root, solutionPath)
                : null
        };
}

internal sealed record ProbeDiResult
{
    public required IReadOnlyList<ProbeDiFinding> Findings { get; init; }

    internal static ProbeDiResult From(IEnumerable<ProbeDiRegistration> registrations, string? root, string? solutionPath) =>
        new() { Findings = registrations.Select(r => ProbeDiFinding.From(r, root, solutionPath)).ToList() };

}
