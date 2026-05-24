namespace RoslynTools.Commands.Results;

internal sealed record SuggestedFix
{
    public required string Tool { get; init; }
    public required string Ref { get; init; }
    public required string Description { get; init; }
    public string? CommandLine { get; init; }

    internal object AsJsonObject() => new
    {
        tool = Tool,
        @ref = Ref,
        description = Description,
        commandLine = (object?)CommandLine
    };

    internal void RenderText(TextWriter writer)
    {
        if (CommandLine is not null)
            writer.Write($"  → [{Tool}] {CommandLine}");
        else
            writer.Write($"  → [{Tool}] {Ref} — {Description}");
    }
}
