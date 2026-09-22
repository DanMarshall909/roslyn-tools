using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Transport-neutral input for retrieving a symbol's source declaration.</summary>
internal sealed record SourceRequest(
    [property: JsonRequired] string Symbol,
    string? Solution = null,
    string? Kind = null,
    [property: CommandPropertyDescription(
        "Include one-hop source declarations for each listed related symbol")]
    bool IncludeRelated = false,
    [property: CommandPropertyDescription("Maximum related symbols to list (1-100)")]
    int RelatedLimit = 10,
    [property: CommandPropertyDescription(
        "Maximum UTF-8 bytes across returned source text (4-1048576)")]
    int MaxSourceBytes = SourceOperation.DefaultMaximumSourceBytes,
    [property: CommandPropertyDescription(
        "Opaque continuation from a prior truncated source response")]
    string? ContinuationToken = null);

/// <summary>One source declaration returned for a resolved symbol.</summary>
internal sealed record SourceDeclaration(
    [property: JsonPropertyName("fqn")] string FullyQualifiedName,
    [property: JsonPropertyName("file")] string FilePath,
    [property: JsonPropertyName("lineFrom")] int LineFrom,
    [property: JsonPropertyName("lineTo")] int LineTo,
    [property: JsonPropertyName("source")] string Text,
    [property: JsonPropertyName("totalBytes")] int TotalBytes = 0,
    [property: JsonPropertyName("returnedFromByte")] int ReturnedFromByte = 0,
    [property: JsonPropertyName("returnedToByteExclusive")] int ReturnedToByteExclusive = 0,
    [property: JsonPropertyName("truncated")] bool Truncated = false);

/// <summary>A symbol referenced by the requested declaration or body.</summary>
internal sealed record RelatedSourceSymbol(
    [property: JsonPropertyName("fqn")] string FullyQualifiedName,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("declarations")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SourceDeclaration>? Declarations);

/// <summary>The requested symbol content followed by its bounded related-symbol list.</summary>
internal sealed record SourceResult(
    [property: JsonPropertyName("subject")] string Subject,
    [property: JsonPropertyName("sourceRevision")] string SourceRevision,
    [property: JsonPropertyName("sourceBytesTotal")] long SourceBytesTotal,
    [property: JsonPropertyName("sourceBytesReturned")] int SourceBytesReturned,
    [property: JsonPropertyName("truncated")] bool Truncated,
    [property: JsonPropertyName("continuationToken")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContinuationToken,
    [property: JsonPropertyName("declarations")] IReadOnlyList<SourceDeclaration> Declarations,
    [property: JsonPropertyName("relatedSymbolsTruncated")] bool RelatedSymbolsTruncated,
    [property: JsonPropertyName("relatedSymbols")] IReadOnlyList<RelatedSourceSymbol> RelatedSymbols);

/// <summary>Shared source operation used by host adapters.</summary>
internal sealed class SourceOperation
    : CommandApplicationPort<SourceRequest, SourceResult>, IRtService
{
    internal const int MinimumSourceBytes = 4;
    internal const int DefaultMaximumSourceBytes = 65_536;
    private const int MaximumContinuationTokenCharacters = 16_384;

    private readonly IReadOnlyWorkspaceSessions _sessions;
    private readonly InspectService _inspect;
    private readonly ISymbolCandidateService _symbols;

    public SourceOperation(
        IReadOnlyWorkspaceSessions sessions,
        InspectService inspect,
        ISymbolCandidateService symbols)
    {
        _sessions = sessions;
        _inspect = inspect;
        _symbols = symbols ?? throw new ArgumentNullException(nameof(symbols));
    }

    public override ApplicationCommandDefinition Definition => SourceApplicationDefinition.Definition;

    public override async Task<Result<SourceResult, RtError>> ExecuteAsync(
        SourceRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Symbol))
            return Result<SourceResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A symbol identity or name is required."));
        if (request.RelatedLimit <= 0 || request.RelatedLimit > Definition.Product.Limits.MaximumItems)
            return Result<SourceResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Related limit must be between 1 and {Definition.Product.Limits.MaximumItems}."));
        if (request.MaxSourceBytes < MinimumSourceBytes
            || request.MaxSourceBytes > Definition.Product.Limits.MaximumOutputBytes)
        {
            return Result<SourceResult, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Maximum source bytes must be between {MinimumSourceBytes} and "
                + $"{Definition.Product.Limits.MaximumOutputBytes}."));
        }

        var continuationResult = ParseContinuation(request);
        if (continuationResult.IsErr)
            return continuationResult.Map(_ => default(SourceResult)!);
        var continuation = continuationResult.Match(value => value, _ => default);

        using (var lease = await _sessions.AcquireAsync(request.Solution, cancellationToken))
        {
            var solution = lease.Solution;
            cancellationToken.ThrowIfCancellationRequested();
            var sourceRevision = lease.SourceRevision;
            if (continuation is not null
                && !string.Equals(
                    continuation.SourceRevision,
                    sourceRevision,
                    StringComparison.Ordinal))
            {
                return Result<SourceResult, RtError>.Err(new RtError(
                    RtErrorKind.Stale,
                    "The source continuation belongs to an older or different source revision. "
                    + "Request the symbol again before continuing.",
                    new Dictionary<string, string>
                    {
                        ["continuationRevision"] = continuation.SourceRevision,
                        ["currentRevision"] = sourceRevision
                    }));
            }

            var resolved = await _symbols.ResolveAsync(
                solution,
                request.Symbol,
                request.Kind,
                sourceRevision,
                cancellationToken);
            if (resolved.IsErr)
                return resolved.Map(_ => default(SourceResult)!);

            var symbol = resolved.Match(value => value, _ => default!);
            cancellationToken.ThrowIfCancellationRequested();
            var declarations = NormalizeDeclarations(await _inspect.GetSourceAsync(symbol));
            var related = await _inspect.GetRelatedSymbolsAsync(
                solution,
                symbol,
                request.RelatedLimit + 1,
                cancellationToken);
            var relatedSymbolsTruncated = related.Count > request.RelatedLimit;
            var selectedRelated = related.Take(request.RelatedLimit).ToArray();
            var relatedDeclarations = new List<IReadOnlyList<SourceDeclaration>>(selectedRelated.Length);
            foreach (var relatedSymbol in selectedRelated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (request.IncludeRelated)
                {
                    relatedDeclarations.Add(NormalizeDeclarations(
                        await _inspect.GetSourceAsync(relatedSymbol)));
                }
                else
                {
                    relatedDeclarations.Add([]);
                }
            }

            var pageResult = CreatePage(
                declarations,
                relatedDeclarations,
                continuation,
                request.MaxSourceBytes);
            if (pageResult.IsErr)
                return pageResult.Map(_ => default(SourceResult)!);
            var page = pageResult.Match(value => value, _ => default!);
            var nextContinuation = page.Next is null
                ? null
                : EncodeContinuation(new SourceContinuation(
                    SourceContinuation.Schema,
                    request.Symbol,
                    request.Kind,
                    request.IncludeRelated,
                    request.RelatedLimit,
                    sourceRevision,
                    page.Next.EntryIndex,
                    page.Next.ByteOffset));
            var relatedResults = selectedRelated
                .Select((relatedSymbol, index) => new RelatedSourceSymbol(
                    relatedSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                    relatedSymbol.Kind.ToString(),
                    request.IncludeRelated
                        ? page.RelatedDeclarations.GetValueOrDefault(index, [])
                        : null))
                .ToArray();
            return Result<SourceResult, RtError>.Ok(new SourceResult(
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                sourceRevision,
                page.TotalBytes,
                page.ReturnedBytes,
                nextContinuation is not null,
                nextContinuation,
                page.SubjectDeclarations,
                relatedSymbolsTruncated,
                relatedResults));
        }
    }

    private static List<SourceDeclaration> NormalizeDeclarations(
        IEnumerable<SourceDeclaration> declarations) => declarations
        .Select(source =>
        {
            var text = RemoveDeclarationIndentation(source.Text);
            var totalBytes = Encoding.UTF8.GetByteCount(text);
            return source with
            {
                Text = text,
                TotalBytes = totalBytes,
                ReturnedFromByte = 0,
                ReturnedToByteExclusive = totalBytes,
                Truncated = false
            };
        })
        .OrderBy(source => source.FilePath, StringComparer.Ordinal)
        .ThenBy(source => source.LineFrom)
        .ToList();

    private static Result<SourceContinuation?, RtError> ParseContinuation(SourceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContinuationToken))
            return Result<SourceContinuation?, RtError>.Ok(null);
        if (request.ContinuationToken.Length > MaximumContinuationTokenCharacters)
            return InvalidContinuation("The source continuation token is too large.");

        SourceContinuation? continuation;
        try
        {
            var encoded = request.ContinuationToken
                .Replace('-', '+')
                .Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + (4 - encoded.Length % 4) % 4, '=');
            continuation = JsonSerializer.Deserialize<SourceContinuation>(
                Convert.FromBase64String(encoded));
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return InvalidContinuation("The source continuation token is malformed.");
        }

        if (continuation is null
            || !string.Equals(continuation.SchemaVersion, SourceContinuation.Schema, StringComparison.Ordinal))
        {
            return InvalidContinuation("The source continuation token uses an unsupported schema.");
        }

        if (!string.Equals(continuation.Symbol, request.Symbol, StringComparison.Ordinal)
            || !string.Equals(continuation.Kind, request.Kind, StringComparison.Ordinal)
            || continuation.IncludeRelated != request.IncludeRelated
            || continuation.RelatedLimit != request.RelatedLimit)
        {
            return InvalidContinuation(
                "The source continuation token does not match the symbol or related-source selection.");
        }

        return Result<SourceContinuation?, RtError>.Ok(continuation);
    }

    private static Result<SourceContinuation?, RtError> InvalidContinuation(string message) =>
        Result<SourceContinuation?, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message));

    private static Result<SourcePage, RtError> CreatePage(
        IReadOnlyList<SourceDeclaration> subjectDeclarations,
        IReadOnlyList<IReadOnlyList<SourceDeclaration>> relatedDeclarations,
        SourceContinuation? continuation,
        int maximumBytes)
    {
        var entries = subjectDeclarations
            .Select(declaration => new SourceContentEntry(null, declaration))
            .Concat(relatedDeclarations.SelectMany((declarations, relatedIndex) =>
                declarations.Select(declaration => new SourceContentEntry(relatedIndex, declaration))))
            .ToArray();
        var totalBytes = entries.Sum(entry => (long)entry.Declaration.TotalBytes);
        if (entries.Length == 0)
            return Result<SourcePage, RtError>.Ok(new SourcePage(
                [],
                new Dictionary<int, IReadOnlyList<SourceDeclaration>>(),
                0,
                0,
                null));

        var entryIndex = continuation?.EntryIndex ?? 0;
        var byteOffset = continuation?.ByteOffset ?? 0;
        if (entryIndex < 0 || entryIndex >= entries.Length)
            return InvalidPage("The source continuation points outside the available declarations.");
        if (!IsUtf8Boundary(entries[entryIndex].Declaration.Text, byteOffset))
            return InvalidPage("The source continuation byte offset is not a valid UTF-8 boundary.");

        var subjectPage = new List<SourceDeclaration>();
        var relatedPage = new Dictionary<int, IReadOnlyList<SourceDeclaration>>();
        var mutableRelatedPage = new Dictionary<int, List<SourceDeclaration>>();
        var remainingBytes = maximumBytes;
        SourcePagePosition? next = null;
        var returnedBytes = 0;
        for (var index = entryIndex; index < entries.Length; index++)
        {
            var entry = entries[index];
            var start = index == entryIndex ? byteOffset : 0;
            var chunk = SliceUtf8(entry.Declaration.Text, start, remainingBytes);
            if (chunk.Bytes == 0 && start < entry.Declaration.TotalBytes)
            {
                next = new SourcePagePosition(index, start);
                break;
            }

            var declaration = entry.Declaration with
            {
                Text = chunk.Text,
                ReturnedFromByte = start,
                ReturnedToByteExclusive = start + chunk.Bytes,
                Truncated = start > 0 || start + chunk.Bytes < entry.Declaration.TotalBytes
            };
            if (entry.RelatedIndex is { } relatedIndex)
            {
                if (!mutableRelatedPage.TryGetValue(relatedIndex, out var declarations))
                {
                    declarations = [];
                    mutableRelatedPage.Add(relatedIndex, declarations);
                }

                declarations.Add(declaration);
            }
            else
            {
                subjectPage.Add(declaration);
            }

            returnedBytes += chunk.Bytes;
            remainingBytes -= chunk.Bytes;
            var end = start + chunk.Bytes;
            if (end < entry.Declaration.TotalBytes)
            {
                next = new SourcePagePosition(index, end);
                break;
            }

            if (remainingBytes == 0 && index + 1 < entries.Length)
            {
                next = new SourcePagePosition(index + 1, 0);
                break;
            }
        }

        foreach (var pair in mutableRelatedPage)
            relatedPage.Add(pair.Key, pair.Value);
        return Result<SourcePage, RtError>.Ok(new SourcePage(
            subjectPage,
            relatedPage,
            totalBytes,
            returnedBytes,
            next));
    }

    private static Result<SourcePage, RtError> InvalidPage(string message) =>
        Result<SourcePage, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message));

    private static bool IsUtf8Boundary(string value, int byteOffset)
    {
        if (byteOffset < 0)
            return false;
        var current = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (current == byteOffset)
                return true;
            current += rune.Utf8SequenceLength;
        }

        return current == byteOffset;
    }

    private static SourceChunk SliceUtf8(string value, int byteOffset, int maximumBytes)
    {
        var currentBytes = 0;
        var startCharacter = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (currentBytes == byteOffset)
                break;
            currentBytes += rune.Utf8SequenceLength;
            startCharacter += rune.Utf16SequenceLength;
        }

        var returnedBytes = 0;
        var returnedCharacters = 0;
        foreach (var rune in value.AsSpan(startCharacter).EnumerateRunes())
        {
            if (returnedBytes + rune.Utf8SequenceLength > maximumBytes)
                break;
            returnedBytes += rune.Utf8SequenceLength;
            returnedCharacters += rune.Utf16SequenceLength;
        }

        return new SourceChunk(
            value.Substring(startCharacter, returnedCharacters),
            returnedBytes);
    }

    private static string EncodeContinuation(SourceContinuation continuation)
    {
        var encoded = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(continuation));
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string RemoveDeclarationIndentation(string source)
    {
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var lines = normalized.Split('\n');
        var firstContentLine = lines.FirstOrDefault(line => line.Length > 0);
        if (firstContentLine is null)
            return normalized;

        var indentationLength = firstContentLine.TakeWhile(char.IsWhiteSpace).Count();
        if (indentationLength == 0)
            return normalized;

        var indentation = firstContentLine[..indentationLength];
        return string.Join(
            Environment.NewLine,
            lines.Select(line => line.StartsWith(indentation, StringComparison.Ordinal)
                ? line[indentation.Length..]
                : line));
    }

    private sealed record SourceContinuation(
        string SchemaVersion,
        string Symbol,
        string? Kind,
        bool IncludeRelated,
        int RelatedLimit,
        string SourceRevision,
        int EntryIndex,
        int ByteOffset)
    {
        internal const string Schema = "rt.source.continuation.v1";
    }

    private sealed record SourceContentEntry(int? RelatedIndex, SourceDeclaration Declaration);

    private sealed record SourcePagePosition(int EntryIndex, int ByteOffset);

    private sealed record SourceChunk(string Text, int Bytes);

    private sealed record SourcePage(
        IReadOnlyList<SourceDeclaration> SubjectDeclarations,
        IReadOnlyDictionary<int, IReadOnlyList<SourceDeclaration>> RelatedDeclarations,
        long TotalBytes,
        int ReturnedBytes,
        SourcePagePosition? Next);
}
