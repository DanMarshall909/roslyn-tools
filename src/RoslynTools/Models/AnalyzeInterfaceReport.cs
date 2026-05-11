namespace RoslynTools.Models;

/// <summary>
/// Stores options for analyzing interfaces.
/// </summary>
internal sealed record AnalyzeInterfaceOptions(
    string? Scope = null,
    bool IncludeTests = false,
    bool Suggest = false);

/// <summary>
/// Provides a report on interface member analysis.
/// </summary>
internal sealed record AnalyzeInterfaceMemberReport(
    string Name,
    string Kind,
    string DisplayName);

/// <summary>
///     Advisory suggestion for a minimal replacement interface shape for a single consumer.
///     Derived solely from observed used-member data — does not imply a named type exists.
/// </summary>
internal sealed record AnalyzeInterfaceSuggestion(
    bool HasNarrowingBenefit,
    string SharedGroupId,
    IReadOnlyList<AnalyzeInterfaceMemberReport> SuggestedMembers);

/// <summary>
/// Provides a report on interface consumer analysis.
/// </summary>
internal sealed record AnalyzeInterfaceConsumerReport(
    string ConsumerName,
    string ConsumerDisplayName,
    string ConsumerKind,
    string Namespace,
    string ProjectName,
    string UsageKind,
    IReadOnlyList<string> Documents,
    IReadOnlyList<AnalyzeInterfaceMemberReport> UsedMembers,
    AnalyzeInterfaceSuggestion? Suggestion = null);

/// <summary>
/// Combines member and consumer reports for interface analysis.
/// </summary>
internal sealed record AnalyzeInterfaceReport(
    string InterfaceName,
    string InterfaceDisplayName,
    int InterfaceMemberCount,
    string? Scope,
    bool IncludeTests,
    IReadOnlyList<AnalyzeInterfaceConsumerReport> Consumers);
