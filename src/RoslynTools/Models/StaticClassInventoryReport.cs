namespace RoslynTools.Models;

internal sealed record StaticClassEntry(
    string Fqn,
    string File,
    int MemberCount,
    int CallSiteCount,
    IReadOnlyList<string> CalledFromStaticClasses,
    IReadOnlyList<string> CalledFromInstanceClasses,
    string MigrationDifficulty);

internal sealed record StaticClassInventoryReport(
    int TotalStaticClasses,
    IReadOnlyList<StaticClassEntry> Classes);
