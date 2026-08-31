using System.Text.Json;

namespace RoslynTools.Core;

/// <summary>
///     Extracts a target symbol from a piped JSON payload (e.g. the stdout of
///     <c>rt inspect relationships references --json</c>) so commands can be chained:
///     <c>rt inspect relationships references … --json | rt refactor rename --new-name …</c>.
/// </summary>
internal static class PipedSymbolReader
{
    /// <summary>
    ///     Resolves the symbol for a chained command: the positional argument wins when present;
    ///     otherwise, only when input is redirected (piped, not a TTY), the JSON payload is read
    ///     and its legacy <c>fullyQualifiedName</c> or envelope <c>data.resolvedSubject</c>
    ///     used. Returns <paramref name="positionalSymbol"/>
    ///     unchanged when there is nothing to read or the payload yields no FQN.
    /// </summary>
    internal static async Task<string?> ResolveAsync(
        string? positionalSymbol, bool inputRedirected, TextReader input)
    {
        if (!string.IsNullOrWhiteSpace(positionalSymbol) || !inputRedirected)
            return positionalSymbol;

        var payload = await input.ReadToEndAsync();
        return TryReadFullyQualifiedName(payload, out var fqn) ? fqn : positionalSymbol;
    }

    /// <summary>
    ///     Reads the <c>fullyQualifiedName</c> field from a JSON object payload. Returns
    ///     <see langword="false"/> (and a null out value) for null/empty input, malformed JSON,
    ///     a non-object root, a missing field, or an empty field value. Never throws on bad input.
    /// </summary>
    internal static bool TryReadFullyQualifiedName(string? payload, out string? fqn)
    {
        fqn = null;
        if (string.IsNullOrWhiteSpace(payload))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return false;

            JsonElement prop;
            if (!doc.RootElement.TryGetProperty("fullyQualifiedName", out prop))
            {
                if (!doc.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Object
                    || !data.TryGetProperty("resolvedSubject", out prop))
                    return false;
            }

            if (prop.ValueKind != JsonValueKind.String)
                return false;

            var value = prop.GetString();
            if (string.IsNullOrWhiteSpace(value))
                return false;

            fqn = value;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
