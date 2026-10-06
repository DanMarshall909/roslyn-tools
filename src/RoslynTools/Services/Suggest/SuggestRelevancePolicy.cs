using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     Conservative task admission, separate from BM25 ranking and command-name lookup.
///     Every substantive term must be represented by the candidate's declared capability.
/// </summary>
internal sealed partial class SuggestRelevancePolicy
{
    private readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "a", "an", "the", "is", "are", "of", "to", "in", "for", "this", "that",
        "all", "every", "my", "our", "your", "its", "it", "me", "us", "i", "we", "you",
        "can", "could", "would", "please", "kindly", "want", "need", "how", "what",
        "which", "and", "or", "from", "into", "on", "at", "by", "as", "with",
        "find", "show", "list", "get", "inspect", "read", "also", "instead", "they"
    };

    private SuggestRelevancePolicy()
    {
    }

    internal static IReadOnlyList<ScoredCommand> Admit(
        IReadOnlyList<ScoredCommand> ranked,
        string query)
    {
        var policy = new SuggestRelevancePolicy();
        var capabilities = ranked.Select(candidate => (Candidate: candidate, Terms: policy.CapabilityTerms(candidate.Command)))
            .ToList();
        var vocabulary = capabilities.SelectMany(candidate => candidate.Terms).ToHashSet(StringComparer.Ordinal);
        var taskTerms = policy.QueryTerms(query, vocabulary).Where(term => !policy.Filler.Contains(term)).ToList();
        var requested = taskTerms.ToHashSet(StringComparer.Ordinal);
        if (requested.Count == 0)
            return [];

        var reads = policy.IsReadTask(query, taskTerms);
        return capabilities.Where(candidate =>
            (!reads || !candidate.Candidate.Command.Mutates)
            && requested.IsSubsetOf(candidate.Terms)
            && (requested.Count != 1
                || requested.IsSubsetOf(policy.Terms(string.Join(' ', Names(candidate.Candidate.Command))).ToHashSet(StringComparer.Ordinal))))
            .Select(candidate => candidate.Candidate)
            .ToList();
    }

    private bool IsReadTask(string query, IReadOnlyList<string> taskTerms)
    {
        // Filename punctuation and words cannot establish a task clause.
        var withoutPaths = QueryWords().Replace(query, match => match.Groups["path"].Success ? " " : match.Value);
        if (ReadClauseBoundary().Split(withoutPaths).Any(IsReadClause))
            return true;

        if (!NounRequestPrefix().IsMatch(query))
            return false;

        var subject = string.Join(' ', taskTerms);
        return NounRequestSubject().IsMatch(subject);
    }

    private bool IsReadClause(string clause)
    {
        foreach (Match word in QueryWords().Matches(clause))
        {
            if (ReadTask().IsMatch(clause[word.Index..]))
                return true;

            if (!Filler.Contains(Normalize(word.Value)))
                return false;
        }

        return false;
    }

    private HashSet<string> CapabilityTerms(RtCommandInfo command)
    {
        var primaryCapability = SentenceBoundary().Split(command.WhenToUse, 2)[0];
        return Terms(string.Join(' ', Names(command).Append(primaryCapability)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> Names(RtCommandInfo command) =>
        new[] { command.Name }.Concat(Synonyms.NameSynonyms.GetValueOrDefault(command.Name) ?? [])
            .Concat(command.Aliases ?? []);

    private IEnumerable<string> Terms(string text) =>
        Words().Matches(text).Select(match => Normalize(match.Value));

    private IEnumerable<string> QueryTerms(string query, HashSet<string> vocabulary)
    {
        var renameTargets = RenameTargets().Match(query).Groups["target"].Captures;
        foreach (Match match in QueryWords().Matches(query))
        {
            if (match.Groups["path"].Success)
                continue;

            var value = match.Value;
            var prefix = query[..match.Index].TrimEnd().TrimEnd('"', '\'').TrimEnd();
            var suffix = query[(match.Index + match.Length)..];
            var next = Words().Match(suffix);
            var endsTarget = !next.Success || Filler.Contains(Normalize(next.Value));
            var postfixedKind = DefiniteArticle().IsMatch(prefix) && TargetKind().IsMatch(suffix);
            var lastSegment = value[(value.LastIndexOf('.') + 1)..];
            var qualifiedTarget = value.Contains('.') && lastSegment.Any(char.IsUpper)
                                  && !ForeignLanguageExtension().IsMatch(value);
            var renameTarget = renameTargets.Any(target => target.Index == match.Index);
            var explicitTarget = TargetPrefix().IsMatch(prefix)
                                 && (!vocabulary.Contains(Normalize(value)) || NamedTargetPrefix().IsMatch(prefix));
            var validIdentifier = SyntaxFacts.IsValidIdentifier(lastSegment)
                                  || lastSegment.StartsWith('@') && SyntaxFacts.IsValidIdentifier(lastSegment[1..]);
            if (validIdentifier
                && !Filler.Contains(Normalize(value))
                && (renameTarget || postfixedKind
                    || endsTarget && (explicitTarget
                                      || qualifiedTarget && QualifiedTargetPrefix().IsMatch(prefix))))
                continue;

            foreach (var term in Terms(value))
                yield return term;
        }
    }

    private string Normalize(string term)
    {
        var lower = term.ToLowerInvariant();
        return lower switch
        {
            "c#" => "csharp",
            "renaming" => "rename",
            "updating" => "update",
            "removing" => "remove",
            "replacing" => "replace",
            "deleting" => "delete",
            "generating" => "generate",
            "injecting" => "inject",
            "measuring" => "measure",
            "finding" => "find",
            "comparing" => "compare",
            "converting" => "convert",
            "moving" => "move",
            "splitting" => "split",
            "checking" => "check",
            "enforcing" => "enforce",
            "recording" => "record",
            "used" => "use",
            _ when Filler.Contains(lower) => lower,
            _ when lower.Length > 4 && lower.EndsWith("ies", StringComparison.Ordinal) => lower[..^3] + "y",
            _ when lower.EndsWith("sses", StringComparison.Ordinal) => lower[..^2],
            _ when lower.Length > 3 && lower.EndsWith('s') && !lower.EndsWith("ss", StringComparison.Ordinal) => lower[..^1],
            _ => lower
        };
    }

    [GeneratedRegex(@"[\p{N}]+(?:\.[\p{N}]+)+|[\p{L}\p{N}_]+#?", RegexOptions.CultureInvariant)]
    private static partial Regex Words();

    [GeneratedRegex(@"[.!?]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundary();

    [GeneratedRegex(
        """(?<path>"[^"\r\n]*\.(?:cs|csproj|slnx?)"|'[^'\r\n]*\.(?:cs|csproj|slnx?)'|[\p{L}\p{N}_./:\\-]+\.(?:cs|csproj|slnx?)\b)|@?[\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*)*#?|[\p{N}]+(?:\.[\p{N}]+)*""",
        RegexOptions.CultureInvariant)]
    private static partial Regex QueryWords();

    [GeneratedRegex(@"\.(?:sql|[cm]?js|jsx|tsx?|py[wi]?|java)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ForeignLanguageExtension();

    [GeneratedRegex(
        @"\b(?:named|symbol|type|class|interface|method|field|property|member)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetPrefix();

    [GeneratedRegex(@"\bnamed\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NamedTargetPrefix();

    [GeneratedRegex(@"\b(?:source|of|to|from)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QualifiedTargetPrefix();

    [GeneratedRegex(
        """^\s*(?:(?:please|kindly)\s+)?rename\s+(?:(?:symbol|type|class|interface|method|field|property|member)\s+)?["']?(?<target>@?[\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*)*)["']?\s+to\s+["']?(?<target>@?[\p{L}_][\p{L}\p{N}_]*(?:\.[\p{L}_][\p{L}\p{N}_]*)*)\b["']?""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RenameTargets();

    [GeneratedRegex(
        @"^\s*(?:find|finding|show|list|get|inspect|read|measure|measuring)\b(?!(?:\s+(?:me|us))?\s+how\s+to\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadTask();

    [GeneratedRegex(@"[.!?;]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex ReadClauseBoundary();

    [GeneratedRegex(
        @"^\s*(?:(?:please|kindly)\s+)?(?:what|which|(?:i|we|you|they)\s+(?:want|need))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NounRequestPrefix();

    [GeneratedRegex(
        @"^(?:(?:public|protected|private|internal|static|abstract|sealed|partial|csharp)\s+)*(?:class|interface|type|method|field|property|member|symbol|namespace)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex NounRequestSubject();

    [GeneratedRegex(@"\bthe\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefiniteArticle();

    [GeneratedRegex(@"^\s+(?:class|interface|type|method|field|property|member)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TargetKind();
}
