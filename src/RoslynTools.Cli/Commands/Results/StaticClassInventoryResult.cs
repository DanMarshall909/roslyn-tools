using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Models;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands.Results;

/// <summary>
/// A class representing the result of a static class inventory command.
/// </summary>
internal sealed record StaticClassInventoryResult : IRtOutput, IRtJsonOutput
{
    public required int TotalStaticClasses { get; init; }
    public required IReadOnlyList<StaticClassEntryOutput> Classes { get; init; }

    public void RenderJson(TextWriter writer)
    {
        var document = new
        {
            diagnostics = Array.Empty<string>(),
            results = new
            {
                totalStaticClasses = TotalStaticClasses,
                classes = Classes.Select(c => new
                {
                    fqn = c.Fqn,
                    file = c.File,
                    memberCount = c.MemberCount,
                    callSiteCount = c.CallSiteCount,
                    calledFromStaticClasses = c.CalledFromStaticClasses,
                    calledFromInstanceClasses = c.CalledFromInstanceClasses,
                    migrationDifficulty = c.MigrationDifficulty
                }).ToArray()
            }
        };

        writer.Write(JsonSerializer.Serialize(document, SymbolFormatter.DefaultJsonOptions));
    }

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"Total static classes: {TotalStaticClasses}");
        writer.WriteLine();

        foreach (var cls in Classes)
        {
            writer.WriteLine($"[{cls.MigrationDifficulty.ToUpperInvariant()}] {cls.Fqn}");
            writer.WriteLine($"  File:        {cls.File}");
            writer.WriteLine($"  Members:     {cls.MemberCount}");
            writer.WriteLine($"  Call sites:  {cls.CallSiteCount}");

            if (cls.CalledFromStaticClasses.Count > 0)
            {
                writer.WriteLine($"  Static callers ({cls.CalledFromStaticClasses.Count}):");
                foreach (var fqn in cls.CalledFromStaticClasses)
                    writer.WriteLine($"    - {fqn}");
            }

            if (cls.CalledFromInstanceClasses.Count > 0)
            {
                writer.WriteLine($"  Instance callers ({cls.CalledFromInstanceClasses.Count}):");
                foreach (var fqn in cls.CalledFromInstanceClasses)
                    writer.WriteLine($"    - {fqn}");
            }

            writer.WriteLine();
        }
    }

    internal static StaticClassInventoryResult FromReport(StaticClassInventoryReport report)
    {
        return new StaticClassInventoryResult
        {
            TotalStaticClasses = report.TotalStaticClasses,
            Classes = report.Classes.Select(c => new StaticClassEntryOutput
            {
                Fqn = c.Fqn,
                File = c.File,
                MemberCount = c.MemberCount,
                CallSiteCount = c.CallSiteCount,
                CalledFromStaticClasses = c.CalledFromStaticClasses,
                CalledFromInstanceClasses = c.CalledFromInstanceClasses,
                MigrationDifficulty = c.MigrationDifficulty
            }).ToList()
        };
    }

    internal static StaticClassInventoryResult FromAnalysis(StaticClassInventoryAnalysisResult result) => new()
    {
        TotalStaticClasses = result.TotalStaticClasses,
        Classes = result.Classes.Select(c => new StaticClassEntryOutput
        {
            Fqn = c.Fqn,
            File = c.File,
            MemberCount = c.MemberCount,
            CallSiteCount = c.CallSiteCount,
            CalledFromStaticClasses = c.CalledFromStaticClasses,
            CalledFromInstanceClasses = c.CalledFromInstanceClasses,
            MigrationDifficulty = c.MigrationDifficulty
        }).ToList()
    };
}

/// <summary>
/// A class representing the output of a static class entry command.
/// </summary>
internal sealed record StaticClassEntryOutput : IRtOutput
{
    public required string Fqn { get; init; }
    public required string File { get; init; }
    public required int MemberCount { get; init; }
    public required int CallSiteCount { get; init; }
    public required IReadOnlyList<string> CalledFromStaticClasses { get; init; }
    public required IReadOnlyList<string> CalledFromInstanceClasses { get; init; }
    public required string MigrationDifficulty { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.Write($"[{MigrationDifficulty}] {Fqn}");
    }
}
