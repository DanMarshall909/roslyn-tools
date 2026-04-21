using System.Text.Json;
using RoslynTools.Core;
using RoslynTools.Core.Output;
using RoslynTools.Models;

namespace RoslynTools.Commands.Results;

internal sealed record AnalyzeInterfaceMemberOutput : IRtOutput
{
    [OutputField(OutputLevel.Llm, "name")]
    public required string Name { get; init; }

    [OutputField(OutputLevel.Default, "kind")]
    public required string Kind { get; init; }

    [OutputField(OutputLevel.Default, "displayName")]
    public required string DisplayName { get; init; }

    public void RenderText(TextWriter writer) => writer.Write(DisplayName);
}

internal sealed record AnalyzeInterfaceConsumerOutput : IRtOutput
{
    [OutputField(OutputLevel.Llm, "consumer")]
    public required string ConsumerDisplayName { get; init; }

    [OutputField(OutputLevel.Default, "kind")]
    public required string ConsumerKind { get; init; }

    [OutputField(OutputLevel.Default, "usageKind")]
    public required string UsageKind { get; init; }

    [OutputField(OutputLevel.Default, "project")]
    public required string ProjectName { get; init; }

    [OutputField(OutputLevel.Default, "documents")]
    public required IReadOnlyList<string> Documents { get; init; }

    [OutputField(OutputLevel.Llm, "usedMembers")]
    public required IReadOnlyList<AnalyzeInterfaceMemberOutput> UsedMembers { get; init; }

    public void RenderText(TextWriter writer) => writer.Write(ConsumerDisplayName);
}

internal sealed record AnalyzeInterfaceResult : IRtOutput, IRtJsonOutput, IRtMarkdownOutput
{
    [OutputField(OutputLevel.Llm, "interface")]
    public required string InterfaceDisplayName { get; init; }

    [OutputField(OutputLevel.Default, "consumerCount")]
    public required int ConsumerCount { get; init; }

    [OutputField(OutputLevel.Default, "scope")]
    public string? Scope { get; init; }

    [OutputField(OutputLevel.Default, "includeTests")]
    public required bool IncludeTests { get; init; }

    [OutputField(OutputLevel.Llm, "consumers")]
    public required IReadOnlyList<AnalyzeInterfaceConsumerOutput> Consumers { get; init; }

    public void RenderText(TextWriter writer)
    {
        writer.WriteLine($"Interface {InterfaceDisplayName}");
        writer.WriteLine($"Consumers: {ConsumerCount}");

        if (Consumers.Count == 0)
            return;

        writer.WriteLine();
        foreach (var consumer in Consumers)
        {
            writer.WriteLine($"{consumer.ConsumerDisplayName} [{consumer.UsageKind}]");
            if (consumer.UsedMembers.Count == 0)
            {
                writer.WriteLine("  - no interface members used");
            }
            else
            {
                foreach (var member in consumer.UsedMembers)
                {
                    writer.WriteLine($"  - {member.DisplayName}");
                }
            }

            writer.WriteLine();
        }
    }

    public void RenderMarkdown(TextWriter writer)
    {
        writer.WriteLine($"# Interface Usage: {InterfaceDisplayName}");
        writer.WriteLine();
        writer.WriteLine($"Consumers: **{ConsumerCount}**  ");
        writer.WriteLine($"Include tests: **{IncludeTests}**  ");

        if (!string.IsNullOrEmpty(Scope))
            writer.WriteLine($"Scope: **{Scope}**  ");

        writer.WriteLine();

        foreach (var consumer in Consumers)
        {
            writer.WriteLine($"## {consumer.ConsumerDisplayName}");
            writer.WriteLine();
            writer.WriteLine($"Usage: **{consumer.UsageKind}**  ");
            writer.WriteLine($"Project: **{consumer.ProjectName}**  ");
            writer.WriteLine($"Documents: {string.Join(", ", consumer.Documents)}");
            writer.WriteLine();

            if (consumer.UsedMembers.Count == 0)
            {
                writer.WriteLine("No interface members used directly.");
                writer.WriteLine();
                continue;
            }

            foreach (var member in consumer.UsedMembers)
            {
                writer.WriteLine($"- `{member.DisplayName}`");
            }

            writer.WriteLine();
        }
    }

    public void RenderJson(TextWriter writer)
    {
        var document = new
        {
            @interface = new
            {
                displayName = InterfaceDisplayName,
            },
            consumerCount = ConsumerCount,
            scope = Scope,
            includeTests = IncludeTests,
            consumers = Consumers.Select(consumer => new
            {
                consumer = new
                {
                    displayName = consumer.ConsumerDisplayName,
                    kind = consumer.ConsumerKind,
                    project = consumer.ProjectName,
                    documents = consumer.Documents,
                },
                usageKind = consumer.UsageKind,
                usedMembers = consumer.UsedMembers.Select(member => new
                {
                    name = member.Name,
                    kind = member.Kind,
                    displayName = member.DisplayName,
                }).ToArray(),
            }).ToArray(),
        };

        writer.Write(JsonSerializer.Serialize(document, SymbolFormatter.DefaultJsonOptions));
    }

    internal static AnalyzeInterfaceResult FromReport(AnalyzeInterfaceReport report)
    {
        return new AnalyzeInterfaceResult
        {
            InterfaceDisplayName = report.InterfaceDisplayName,
            ConsumerCount = report.Consumers.Count,
            Scope = report.Scope,
            IncludeTests = report.IncludeTests,
            Consumers = report.Consumers.Select(consumer => new AnalyzeInterfaceConsumerOutput
            {
                ConsumerDisplayName = consumer.ConsumerDisplayName,
                ConsumerKind = consumer.ConsumerKind,
                UsageKind = consumer.UsageKind,
                ProjectName = consumer.ProjectName,
                Documents = consumer.Documents,
                UsedMembers = consumer.UsedMembers.Select(member => new AnalyzeInterfaceMemberOutput
                {
                    Name = member.Name,
                    Kind = member.Kind,
                    DisplayName = member.DisplayName,
                }).ToList(),
            }).ToList(),
        };
    }
}
