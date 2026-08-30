using System.Text.Json.Nodes;
using RoslynTools.Services.Results;

namespace RoslynTools.Commands.Results;

internal static class SuggestedFixRendering
{
    internal static JsonObject AsJsonObject(this SuggestedFix fix) => new()
    {
        ["tool"] = fix.Tool,
        ["ref"] = fix.Ref,
        ["description"] = fix.Description,
        ["commandLine"] = fix.CommandLine()
    };

    internal static void RenderText(this SuggestedFix fix, TextWriter writer)
    {
        var commandLine = fix.CommandLine();
        if (commandLine is not null)
            writer.Write($"  → [{fix.Tool}] {commandLine}");
        else
            writer.Write($"  → [{fix.Tool}] {fix.Ref} — {fix.Description}");
    }

    internal static string? CommandLine(this SuggestedFix fix)
    {
        if (fix.Command is null)
            return null;

        return $"rt {fix.Command} {string.Join(' ', fix.Arguments.Select(a => a.Quote ? $"\"{a.Value}\"" : a.Value))}".TrimEnd();
    }
}
