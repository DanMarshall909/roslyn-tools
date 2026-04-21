namespace RoslynTools.Models;

internal sealed record AnalyzeInterfaceOptions(
    string? Scope = null,
    bool IncludeTests = false);

internal sealed record AnalyzeInterfaceMemberReport(
    string Name,
    string Kind,
    string DisplayName);

internal sealed record AnalyzeInterfaceConsumerReport(
    string ConsumerName,
    string ConsumerDisplayName,
    string ConsumerKind,
    string Namespace,
    string ProjectName,
    string UsageKind,
    IReadOnlyList<string> Documents,
    IReadOnlyList<AnalyzeInterfaceMemberReport> UsedMembers);

internal sealed record AnalyzeInterfaceReport(
    string InterfaceName,
    string InterfaceDisplayName,
    int InterfaceMemberCount,
    string? Scope,
    bool IncludeTests,
    IReadOnlyList<AnalyzeInterfaceConsumerReport> Consumers);
