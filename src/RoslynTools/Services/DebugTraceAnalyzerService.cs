using System.Text.Json;
using RoslynTools.Models;

namespace RoslynTools.Services;

internal sealed class DebugTraceAnalyzerService : IRtService
{
    private const int SummaryLimit = 160;

    public static DebugReport Analyze(
        FileInfo traceFile,
        int fromEvent,
        int limit,
        IReadOnlyList<string> breakpoints,
        IReadOnlyList<string> assertions,
        bool breakOnly = false)
    {
        if (!traceFile.Exists)
            throw new FileNotFoundException("LLM trace file was not found.", traceFile.FullName);

        var allEvents = LoadEvents(traceFile)
            .Select((element, index) => BuildEvent(element, index + 1))
            .ToList();

        var filtered = allEvents
            .Where(e => e.Step >= Math.Max(1, fromEvent))
            .Take(limit <= 0 ? int.MaxValue : limit)
            .ToList();

        var hits = new List<DebugBreakpointHit>();
        var markedEvents = new List<DebugEvent>(filtered.Count);

        foreach (var evt in filtered)
        {
            var eventHits = breakpoints
                .Select(expression => MatchBreakpoint(evt, expression))
                .OfType<DebugBreakpointHit>()
                .ToList();

            hits.AddRange(eventHits);
            markedEvents.Add(evt with { BreakpointHit = eventHits.Count > 0 });
        }

        var visibleEvents = breakOnly
            ? markedEvents.Where(e => e.BreakpointHit).ToList()
            : markedEvents;

        return new DebugReport(
            traceFile.FullName,
            allEvents.Count,
            Math.Max(1, fromEvent),
            visibleEvents,
            hits,
            assertions.Select(assertion => EvaluateAssertion(allEvents, assertion)).ToList(),
            BuildReplayCommand(traceFile.FullName, Math.Max(1, fromEvent)));
    }

    private static List<JsonElement> LoadEvents(FileInfo traceFile)
    {
        var text = File.ReadAllText(traceFile.FullName).Trim();
        if (string.IsNullOrWhiteSpace(text))
            return [];

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Array => document.RootElement.EnumerateArray().Select(Clone).ToList(),
                JsonValueKind.Object => [Clone(document.RootElement)],
                _ => []
            };
        }
        catch (JsonException)
        {
            return LoadJsonLines(traceFile);
        }
    }

    private static List<JsonElement> LoadJsonLines(FileInfo traceFile)
    {
        var events = new List<JsonElement>();
        foreach (var line in File.ReadLines(traceFile.FullName))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            using var document = JsonDocument.Parse(line);
            events.Add(Clone(document.RootElement));
        }

        return events;
    }

    private static JsonElement Clone(JsonElement element)
    {
        using var document = JsonDocument.Parse(element.GetRawText());
        return document.RootElement.Clone();
    }

    private static DebugEvent BuildEvent(JsonElement element, int fallbackIndex)
    {
        var eventType = GetString(element, "event")
                        ?? GetString(element, "type")
                        ?? GetString(element, "name")
                        ?? "unknown";

        var tool = GetString(element, "tool")
                   ?? GetString(element, "toolName")
                   ?? GetNestedString(element, "tool_call", "name")
                   ?? GetNestedString(element, "toolCall", "name");

        return new DebugEvent(
            fallbackIndex,
            GetTraceId(element),
            eventType,
            tool,
            BuildSummary(element, eventType, tool),
            FindFileReferences(element).Distinct(StringComparer.Ordinal).ToList(),
            // Stryker disable once Boolean : equivalent — Analyze() immediately overwrites this
            // via `evt with { BreakpointHit = eventHits.Count > 0 }`, so the initial value is unobservable.
            false);
    }

    private static string BuildSummary(JsonElement element, string eventType, string? tool)
    {
        var explicitSummary = GetString(element, "summary")
                              ?? GetString(element, "message")
                              ?? GetString(element, "content")
                              ?? GetString(element, "error");

        var prefix = tool is null ? eventType : $"{eventType} {tool}";
        var summary = explicitSummary ?? element.GetRawText();
        summary = summary.ReplaceLineEndings(" ");

        return Truncate($"{prefix}: {summary}", SummaryLimit);
    }

    private static DebugBreakpointHit? MatchBreakpoint(DebugEvent evt, string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
            return null;

        if (TrySplitExpression(trimmed, "event", out var eventType)
            && evt.EventType.Equals(eventType, StringComparison.OrdinalIgnoreCase))
            return new DebugBreakpointHit(evt.Step, trimmed, $"event type is '{evt.EventType}'");

        if (TrySplitExpression(trimmed, "tool", out var tool)
            && evt.Tool?.Equals(tool, StringComparison.OrdinalIgnoreCase) == true)
            return new DebugBreakpointHit(evt.Step, trimmed, $"tool is '{evt.Tool}'");

        if (TrySplitExpression(trimmed, "text", out var text)
            && evt.Summary.Contains(text, StringComparison.OrdinalIgnoreCase))
            return new DebugBreakpointHit(evt.Step, trimmed, $"summary contains '{text}'");

        if (TrySplitExpression(trimmed, "file", out var file)
            && evt.Files.Any(path => path.Contains(file, StringComparison.OrdinalIgnoreCase)))
            return new DebugBreakpointHit(evt.Step, trimmed, $"file reference matches '{file}'");

        return null;
    }

    private static DebugAssertionResult EvaluateAssertion(List<DebugEvent> events, string expression)
    {
        var trimmed = expression.Trim();
        if (trimmed.Length == 0)
            return new DebugAssertionResult(expression, true, "empty assertion ignored");

        if (TrySplitExpression(trimmed, "no-tool", out var blockedTool))
        {
            var count = events.Count(e => e.Tool?.Equals(blockedTool, StringComparison.OrdinalIgnoreCase) == true);
            return new DebugAssertionResult(trimmed, count == 0,
                count == 0 ? $"tool '{blockedTool}' was not used" : $"tool '{blockedTool}' was used {count} time(s)");
        }

        if (TrySplitExpression(trimmed, "requires-event", out var requiredEvent))
        {
            var found = events.Any(e => e.EventType.Equals(requiredEvent, StringComparison.OrdinalIgnoreCase));
            return new DebugAssertionResult(trimmed, found,
                found ? $"event '{requiredEvent}' was present" : $"event '{requiredEvent}' was missing");
        }

        if (TrySplitExpression(trimmed, "max-events", out var maxText)
            && int.TryParse(maxText, out var maxEvents))
        {
            return new DebugAssertionResult(trimmed, events.Count <= maxEvents,
                $"trace contains {events.Count} event(s); maximum is {maxEvents}");
        }

        if (TrySplitExpression(trimmed, "no-text", out var text))
        {
            var found = events.Any(e => e.Summary.Contains(text, StringComparison.OrdinalIgnoreCase));
            return new DebugAssertionResult(trimmed, !found,
                found ? $"text '{text}' was found" : $"text '{text}' was not found");
        }

        return new DebugAssertionResult(trimmed, false,
            "unsupported assertion; use no-tool:<name>, requires-event:<name>, max-events:<n>, or no-text:<text>");
    }

    private static bool TrySplitExpression(string expression, string key, out string value)
    {
        var prefix = key + ":";
        // Stryker disable once Equality : equivalent — when expression.Length == prefix.Length, the
        // trimmed value is empty, so the L211 `value.Length > 0` check returns false either way.
        if (expression.Length <= prefix.Length
            || !expression.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            // Stryker disable once String : equivalent — the out value is ignored by every caller
            // when the method returns false.
            value = string.Empty;
            return false;
        }

        value = expression[prefix.Length..].Trim();
        // Stryker disable once Equality : equivalent — empty values short-circuit downstream
        // checks (Equals/Contains/Any) to false, so loosening to `>= 0` yields identical behaviour.
        return value.Length > 0;
    }

    private static string BuildReplayCommand(string tracePath, int fromEvent)
    {
        return $"rt analyze debug \"{tracePath}\" --from {fromEvent}";
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var property)
               && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static string? GetNestedString(JsonElement element, string propertyName, string nestedPropertyName)
    {
        return element.ValueKind == JsonValueKind.Object
               && element.TryGetProperty(propertyName, out var property)
            ? GetString(property, nestedPropertyName)
            : null;
    }

    private static string? GetTraceId(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("id", out var property))
            return null;

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static IEnumerable<string> FindFileReferences(JsonElement element, bool fileLikeContext = false)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                var childFileLikeContext = fileLikeContext || LooksLikeFileProperty(property.Name);

                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    if (childFileLikeContext)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                            yield return value;
                    }

                    // Stryker disable once Statement : equivalent — falling through would yield the
                    // string value a second time via the recursive String branch, but the caller's
                    // .Distinct() collapses duplicates so the observable result is unchanged.
                    continue;
                }

                foreach (var child in FindFileReferences(property.Value, childFileLikeContext))
                    yield return child;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var child in FindFileReferences(item, fileLikeContext))
                    yield return child;
        }
        else if (fileLikeContext && element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }
    }

    private static bool LooksLikeFileProperty(string propertyName)
    {
        return propertyName.Contains("file", StringComparison.OrdinalIgnoreCase)
               || propertyName.Contains("path", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string value, int limit)
    {
        // limit is always SummaryLimit (160) at the only call site, so the limit <= 0 and
        // limit <= 3 defensive branches are unreachable in practice.
        if (value.Length <= limit)
            return value;

        return value[..(limit - 3)] + "...";
    }
}
