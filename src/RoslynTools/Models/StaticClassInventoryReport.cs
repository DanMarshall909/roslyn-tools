namespace RoslynTools.Models;

/// <summary>
/// Represents a static class entry in the code analysis process.
/// </summary>
internal sealed record StaticClassEntry(
    string Fqn,
    string File,
    int MemberCount,
    int CallSiteCount,
    IReadOnlyList<string> CalledFromStaticClasses,
    IReadOnlyList<string> CalledFromInstanceClasses,
    string MigrationDifficulty);

/// <summary>
/// A class for managing static class inventory reports.
/// </summary>
internal sealed record StaticClassInventoryReport(
    int TotalStaticClasses,
    IReadOnlyList<StaticClassEntry> Classes);
