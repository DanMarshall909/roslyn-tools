using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using RoslynTools.Core;
using RoslynTools.Services.SemanticDiff.Git;

namespace RoslynTools.Services.Suggest;

/// <summary>
///     Host-agnostic command-suggestion service. CLI's <c>SuggestCommand</c>
///     and MCP's <c>SuggestHandler</c> are thin adapters around this — both
///     resolve it from DI and call <see cref="Suggest" /> with a parsed
///     <see cref="SuggestRequest" />, then format the
///     <see cref="SuggestResponse" /> for their respective surfaces.
/// </summary>
/// <remarks>
///     This is the reference implementation of the Core-service + thin-host-
///     adapters pattern documented in CLAUDE.md. Nothing in this file depends
///     on <c>System.CommandLine</c>, <c>Console</c>, or any host's catalog
///     discovery mechanism — it talks only to the injected
///     <see cref="ICommandCatalog" />.
/// </remarks>
internal sealed class SuggestService : IRtService
{
    private const string StaticMemberUseSiteNoun =
        @"(?:references?|usages?|uses?|"
        + @"(?:reads?\s*(?:and|or|/)\s*writes?|writes?\s*(?:and|or|/)\s*reads?)(?:\s+(?:member\s+)?access(?:es)?(?:\s+sites?)?|\s+sites?)?|"
        + @"(?:(?:member|read|write)\s+)?access(?:es)?(?:\s+sites?)?|"
        + @"(?:read|write)\s+sites?|reads?|writes?)";

    private readonly ICommandCatalog _catalog;
    private readonly bool _workingTreeReadsAvailable;

    public SuggestService(ICommandCatalog catalog) : this(catalog, WorkingTreeReadCapability.IsAvailable)
    {
    }

    internal SuggestService(ICommandCatalog catalog, bool workingTreeReadsAvailable)
    {
        _catalog = catalog;
        _workingTreeReadsAvailable = workingTreeReadsAvailable;
    }

    /// <summary>
    ///     Rank the catalog against the query and return up to
    ///     <see cref="SuggestRequest.Top" /> hits. The top result's see-also
    ///     neighbours (once that field lands on <see cref="RtCommandInfo" />)
    ///     are attached as <see cref="SuggestResponse.Related" />.
    /// </summary>
    public SuggestResponse Suggest(SuggestRequest request)
    {
        if (UnavailablePlanCapability(request.Query) is { } missingCapability)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, missingCapability);

        if (IsDiagnosticDeltaRequest(request.Query))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported,
                "compiler-diagnostic-delta",
                UnsupportedReason: "rt cannot compare compiler or analyzer diagnostics between revisions yet. Build each explicit revision with the same SDK and analyzer configuration, capture diagnostic output, and compare identities, severities, and locations; semantic-diff compares C# declarations only.");

        if (UnsupportedDependencyRequest(request.Query) is { } unsupportedDependency)
            return unsupportedDependency;

        if (IsConfiguredArchitectureCheck(request.Query))
            return SuggestArchitectureCheck(request.Solution);

        if (IsProposedChangeImpactRequest(request.Query))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "change-impact",
                UnsupportedReason: "rt cannot calculate the impact of a proposed code or project change yet. Inspect references and project dependencies, review the proposed edits, then build and test the solution. Missing source edges cannot prove safety across reflection, generated code, or external consumers.");

        if (ImplementationSearchSymbol(request.Query) is { } symbol)
            return SuggestImplementations(symbol, request.Solution);

        if (ReferenceSearchTarget(request.Query) is { } referenceTarget)
            return SuggestReferences(referenceTarget.Symbol, referenceTarget.Kind, request.Solution);

        if (IsWorkingTreeReview(request.Query))
            return SuggestWorkingTreeReview(request.Query, request.Solution);

        if (MethodStaticConversion(request.Query) is { } conversion)
            return SuggestMethodConversion(conversion.CommandName, conversion.RequiresWorkflow,
                conversion.OtherTarget, request.Query);

        var staticMemberCapability = StaticMemberChangeCapability(request.Query)
                                     ?? (IsStaticMemberReferenceFirstEdit(request.Query)
                                         ? "member-reference-edit" : null);
        var memberStaticConversion = IsPropertyOrFieldStaticConversion(request.Query);
        if (staticMemberCapability == "member-reference-edit"
            && (!memberStaticConversion || IsLeadingStaticMemberReferenceEdit(request.Query)))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, staticMemberCapability,
                UnsupportedReason: "rt has no general workflow to edit references, reads, writes, or access sites of a static property or field. Resolve the exact member and inspect its references before a reviewed edit; delete-unreferenced prepares deletion of the declaration, not an individual use site.");

        if (memberStaticConversion)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "member-staticness-conversion",
                UnsupportedReason: "rt has no property or field staticness conversion recipe. Inspect the member kind and its callers before choosing a change; method staticness recipes do not apply.");

        if (IsUnparsedMethodStaticConversion(request.Query))
            return UnsupportedMethodStaticnessWorkflow();

        if (SuggestNegatedMethodConversion(request.Query, request.Solution) is { } negatedConversion)
            return negatedConversion;

        if (IsStaticClassMigrationRequest(request.Query))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "static-class-migration",
                UnsupportedReason: "rt has no complete static-class-to-instance or injection migration workflow. Use rt analyze static-class-inventory as preliminary read evidence, then inspect state, call sites, interface needs, and registration before planning the change; a method staticness recipe does not perform this migration.");

        if (staticMemberCapability is not null)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, staticMemberCapability,
                UnsupportedReason: staticMemberCapability == "static-member-creation"
                    ? "rt has no static property or field creation command. Specify the intended declaration and containing type before editing; using-static imports and method staticness recipes do not create members."
                    : "rt has no general static property or field deletion workflow. Inspect the member and its references first; delete-unreferenced requires complete reference evidence and does not establish that an ordinary deletion is safe.");

        if (IsStandaloneNegatedStaticMemberChange(request.Query))
            return new SuggestResponse([], [], SuggestOutcome.NoMatch);

        var scored = Bm25Scorer.Score(
            _catalog.All,
            request.Query,
            request.Top,
            Synonyms.NameSynonyms);

        var results = scored.Select(ToResult).ToList();
        var related = BuildRelated(results, _catalog.All);

        return new SuggestResponse(
            results,
            related,
            results.Count == 0 ? SuggestOutcome.NoMatch : SuggestOutcome.Recommendations);
    }

    private SuggestResponse SuggestMethodConversion(
        string commandName, bool requiresWorkflow, bool otherTarget, string query)
    {
        if (otherTarget)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "method-kind-conversion",
                UnsupportedReason: "rt has no method-to-property, field, or other member-kind conversion workflow. Inspect the source and target member kinds before choosing a refactoring; method staticness recipes do not perform this conversion.");

        if (requiresWorkflow)
            return UnsupportedMethodStaticnessWorkflow();

        var command = _catalog.All.FirstOrDefault(info => info.Name == commandName);
        if (command is null)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, commandName);

        // Word order determines the recipe; BM25 treats both directions as the same terms.
        var ranked = Bm25Scorer.Score([command], query, 1, Synonyms.NameSynonyms);
        var score = ranked.Count == 0 ? 0 : ranked[0].Score;
        return new SuggestResponse([ToResult(new ScoredCommand(command, score))], [],
            SuggestOutcome.Recommendations);
    }

    private static SuggestResponse UnsupportedMethodStaticnessWorkflow() =>
        new([], [], SuggestOutcome.Unsupported, "method-staticness-workflow",
            UnsupportedReason: "rt can suggest one method staticness recipe only for a recognized single-task request. Extra or unrecognized clauses or method syntax may require caller handling or another task. Make separate requests for caller/reference inspection and the selected conversion command; review its dry-run from a clean commit, then build and test before applying.");

    private SuggestResponse? SuggestNegatedMethodConversion(string query, string? solution)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var negation = Regex.Match(query,
            @"^\s*(?:(?:please|kindly)[,;]?\s+)?(?:i\s+)?(?:do\s+not|don['’]t|never|avoid|without)(?:\s+(?:want|wish)\s+to)?\s+(?<verb>convert(?:ing)?|turn(?:ing)?|chang(?:e|ing)|mak(?:e|ing)|remov(?:e|ing))\b",
            options);
        if (!negation.Success)
            return null;

        var verb = negation.Groups["verb"];
        var afterVerb = query[verb.Index..];
        var separator = NegatedClauseSeparator(afterVerb);
        var andTask = separator is not null
            ? Match.Empty
            : Regex.Match(afterVerb,
                @"\s+and\s+(?<task>find|list|show|inspect|convert|turn|change|make|remove)\b", options);
        var clauseEnd = separator?.Index ?? (andTask.Success ? andTask.Index : afterVerb.Length);
        var clause = afterVerb[..clauseEnd];
        var normalizedVerb = verb.Value.ToLowerInvariant() switch
        {
            "converting" => "convert",
            "turning" => "turn",
            "changing" => "change",
            "making" => "make",
            "removing" => "remove",
            _ => verb.Value
        };
        var normalizedClause = normalizedVerb + clause[verb.Length..];
        normalizedClause = Regex.Replace(normalizedClause, @"^make\s+this\b", "make the", options);
        if (MethodStaticConversion(normalizedClause) is null)
            return null;

        var next = separator is { } boundary
            ? afterVerb[(boundary.Index + boundary.Length)..].Trim()
            : andTask.Success ? afterVerb[andTask.Groups["task"].Index..].Trim() : "";
        if (next.Length == 0)
            return new SuggestResponse([], [], SuggestOutcome.NoMatch);

        if (ReferenceSearchTarget(next) is { } referenceTarget)
            return SuggestReferences(referenceTarget.Symbol, referenceTarget.Kind, solution);

        var namedTarget = Regex.Match(next,
            @"\b(?:references?\s+(?:to|of)|callers?\s+of|usages?\s+of)\s+(?:(?:the|an?|method)\s+)*(?:(?<explicitName>named|called)\s+)?(?<symbol>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b",
            options);
        if (namedTarget.Success && IsPlausibleReferenceSymbol(
                namedTarget.Groups["symbol"].Value, namedTarget.Groups["explicitName"].Success))
            return UnsupportedMethodStaticnessWorkflow();

        if (Regex.IsMatch(next, @"^(?:find|list|show|inspect)\b.*\b(?:callers?|usages?|references?)\b", options))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "method-relationship-target",
                UnsupportedReason: "Supply a named method to inspect, for example: find references to method OrderService.Validate. A generic caller or usage request has no exact method target, and rt will not recommend a conversion for a negated action.");

        return UnsupportedMethodStaticnessWorkflow();
    }

    private static (int Index, int Length)? NegatedClauseSeparator(string query)
    {
        var parentheses = 0;
        var typeArguments = 0;
        for (var index = 0; index < query.Length; index++)
        {
            switch (query[index])
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses = Math.Max(0, parentheses - 1);
                    break;
                case '<':
                    typeArguments++;
                    break;
                case '>':
                    typeArguments = Math.Max(0, typeArguments - 1);
                    break;
            }

            if (parentheses != 0 || typeArguments != 0)
                continue;

            if (query[index] == ';')
                return (index, 1);
            if (query[index] is not (',' or '.')
                || index + 1 >= query.Length
                || !char.IsWhiteSpace(query[index + 1]))
                continue;

            var next = index + 2;
            while (next < query.Length && char.IsWhiteSpace(query[next]))
                next++;
            return (index, next - index);
        }

        return null;
    }

    private static bool IsStaticClassMigrationRequest(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string classAction = @"\b(?<verb>make|convert|turn|change|refactor|migrate|replace)\s+"
                                   + @"(?:(?:(?:an?|the|this|that|these|those|another)\s+)?static\s+class(?:es)?"
                                   + @"|(?:(?:an?|the)\s+)?(?:(?:named|called)\s+)?[""']?@?\w+(?:\.@?\w+)*[""']?\s+from\s+(?:(?:an?|the)\s+)?static\s+class(?:es)?)\b";
        const string destination = @"(?:instance(?:\s+class(?:es)?|-based)?|regular\s+class|non[- ]?static|"
                                   + @"injectable(?:\s+service)?|instantiable|dependency[- ]injected\s+service|"
                                   + @"service\s+that\s+can\s+be\s+injected)";
        foreach (Match action in Regex.Matches(query, classAction, options))
        {
            var beforeAction = query[..action.Index];
            var previousClause = Math.Max(beforeAction.LastIndexOf(';'), beforeAction.LastIndexOf('.'));
            if (Regex.IsMatch(beforeAction[(previousClause + 1)..],
                    @"\b(?:do\s+not|don't|never|without)\s+(?:(?:ever|want\s+to)\s+)*$", options))
                continue;

            var afterClass = query[(action.Index + action.Length)..];
            var nextTask = Regex.Match(afterClass,
                @";|[.!?]\s+|\b(?:and|but|then|while)\s+(?:(?:also|then)\s+)*(?:make|convert|turn|change|refactor|migrate|replace|list|find|show|add|remove|delete|create|introduce)\b",
                options);
            var targetText = afterClass[..Math.Min(nextTask.Success ? nextTask.Index : afterClass.Length, 120)]
                .TrimStart();
            if (Regex.IsMatch(targetText,
                    @"\b(?:not|never|no\s+longer)\s+(?:injectable|instantiable|instance|non[- ]?static|usable|use|support)\b",
                    options))
                continue;

            // Bare type names need C# type casing and cannot be task words.
            // Quoted or explicitly named identifiers may use any casing or word.
            var className = Regex.Match(targetText,
                @"^(?:(?:named|called)\s+[""']?@?\w+(?:\.@?\w+)*[""']?|[""']@?\w+(?:\.@?\w+)*[""']|(?<bare>@?(?-i:[A-Z_])\w*(?:\.@?\w+)*))\s+",
                options);
            if (className.Success && !Regex.IsMatch(className.Groups["bare"].Value,
                    @"^(?:to|into|as|with|for|from|return|accept|work|compatible|callable|injectable|"
                    + @"instantiable|instance|regular|nonstatic|usable|use|support|dependency|di|an?|the|be|not|never|no)$",
                    options))
                targetText = targetText[className.Length..];

            var qualifier = Regex.Match(targetText,
                @"^with\s+(?:properties|fields|methods|members)(?:\s*(?:,\s*(?:and\s+)?|and\s+)(?:properties|fields|methods|members))*\s+",
                options);
            if (qualifier.Success)
                targetText = targetText[qualifier.Length..];

            var staticSource = Regex.Match(targetText, @"^from\s+static\s+", options);
            if (staticSource.Success)
                targetText = targetText[staticSource.Length..];

            var verb = action.Groups["verb"].Value;
            var directionWord = verb.Equals("replace", StringComparison.OrdinalIgnoreCase)
                ? @"(?:to|into|as|with)"
                : @"(?:to|into|as)";
            var directionalMatch = Regex.Match(targetText,
                @"^" + directionWord + @"\s+(?:be\s+)?(?:(?:an?|the)\s+)?" + destination + @"\b",
                options);
            var injectionPurpose = Regex.IsMatch(targetText,
                @"^(?:for|to\s+(?:support|use))\s+(?:dependency\s+injection|DI)\b",
                options);
            var directMatch = verb.Equals("make", StringComparison.OrdinalIgnoreCase)
                ? Regex.Match(targetText,
                    @"^(?:(?:an?|the)\s+)?(?:instance(?:\s+class(?:es)?|-based)?|"
                    + @"injectable|instantiable|non[- ]?static|usable\s+(?:with|for)\s+(?:dependency\s+injection|DI)|"
                    + @"(?:use|support)\s+(?:dependency\s+injection|DI))\b",
                    options)
                : Match.Empty;
            var directional = directionalMatch.Success && !Regex.IsMatch(targetText[directionalMatch.Length..],
                @"^\s+(?:methods?|fields?|propert(?:y|ies)|events?)\b", options);
            var directMake = directMatch.Success && !Regex.IsMatch(targetText[directMatch.Length..],
                @"^\s+(?:methods?|fields?|propert(?:y|ies)|events?)\b", options);
            if (directional || injectionPurpose || directMake)
                return true;
        }

        return false;
    }

    private static (string CommandName, bool RequiresWorkflow, bool OtherTarget)? MethodStaticConversion(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string convert = @"\b(?:convert|turn|change)\s+(?:(?:an?|the)\s+)?";
        const string identifier = @"@?\w+(?:<[^>]*>+)?";
        const string memberName = @"(?:(?:named|called)\s+)?" + identifier
            + @"(?:\." + identifier + @")*(?:\([^)]*\))?";
        const string qualifiedMember = identifier + @"(?:\." + identifier + @")+(?:\([^)]*\))?";
        var toStatic = FirstMatch(query, options,
            convert + @"instance\s+methods?\s+(?:to|into)\s+(?:an?\s+)?static(?:\s+methods?)?\b",
            convert + @"instance\s+methods?\s+" + memberName + @"\s+(?:to|into)\s+(?:an?\s+)?static(?:\s+methods?)?\b",
            convert + @"methods?\s+from\s+instance\s+(?:to|into)\s+(?:an?\s+)?static(?:\s+methods?)?\b",
            convert + @"methods?\s+" + memberName + @"\s+from\s+instance\s+(?:to|into)\s+(?:an?\s+)?static(?:\s+methods?)?\b",
            convert + @"named\s+methods?\s+" + memberName + @"\s+from\s+instance\s+(?:to|into)\s+(?:an?\s+)?static(?:\s+methods?)?\b",
            convert + qualifiedMember + @"\s+from\s+instance\s+(?:to|into)\s+(?:an?\s+)?static(?:\s+methods?)?\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?instance\s+methods?\s+static\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?instance\s+methods?\s+" + memberName + @"\s+static\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?named\s+methods?\s+" + memberName + @"\s+static\b",
            @"\bmake\s+" + qualifiedMember + @"\s+static\b");
        var toInstance = FirstMatch(query, options,
            convert + @"static\s+methods?\s+(?:to|into)\s+(?:an?\s+)?(?:instance(?:\s+methods?)?|non[- ]static)\b",
            convert + @"static\s+methods?\s+" + memberName + @"\s+(?:to|into)\s+(?:an?\s+)?(?:instance(?:\s+methods?)?|non[- ]static)\b",
            convert + @"methods?\s+from\s+static\s+(?:to|into)\s+(?:an?\s+)?(?:instance(?:\s+methods?)?|non[- ]static)\b",
            convert + @"methods?\s+" + memberName + @"\s+from\s+static\s+(?:to|into)\s+(?:an?\s+)?(?:instance(?:\s+methods?)?|non[- ]static)\b",
            convert + @"named\s+methods?\s+" + memberName + @"\s+from\s+static\s+(?:to|into)\s+(?:an?\s+)?(?:instance(?:\s+methods?)?|non[- ]static)\b",
            convert + qualifiedMember + @"\s+from\s+static\s+(?:to|into)\s+(?:an?\s+)?(?:instance(?:\s+methods?)?|non[- ]static)\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?static\s+methods?\s+(?:(?:an?\s+)?instance\s+methods?|non[- ]static)\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?static\s+methods?\s+" + memberName + @"\s+(?:(?:an?\s+)?instance\s+methods?|non[- ]static)\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?methods?\s+" + memberName + @"\s+non[- ]static\b",
            @"\bmake\s+(?:(?:an?|the)\s+)?named\s+methods?\s+" + memberName + @"\s+(?:(?:an?\s+)?instance\s+methods?|non[- ]static)\b",
            @"\bmake\s+" + qualifiedMember + @"\s+(?:(?:an?\s+)?instance\s+methods?|non[- ]static)\b",
            @"\bremove\s+(?:the\s+)?static(?:\s+modifier)?\s+from\s+(?:(?:an?|the)\s+)?(?:static\s+)?methods?\s+" + memberName,
            @"\bremove\s+(?:the\s+)?static(?:\s+modifier)?\s+from\s+(?:(?:an?|the)\s+)?named\s+methods?\s+" + memberName,
            @"\bremove\s+(?:the\s+)?static(?:\s+modifier)?\s+from\s+" + qualifiedMember,
            @"\bremove\s+(?:the\s+)?static(?:\s+modifier)?\s+from\s+(?:(?:an?|the)\s+)?methods?\b");
        if (toStatic is null && toInstance is null)
            return null;
        var conversion = toStatic ?? toInstance!;
        if (IsNegatedAction(query, conversion.Index, options)
            || Regex.IsMatch(query[(conversion.Index + conversion.Length)..],
                @"^\s+(?:class(?:es)?|imports?)\b", options))
            return null;
        return (toStatic is not null ? "make-static" : "make-instance",
            toStatic is not null && toInstance is not null
            || HasUnrepresentedMethodClause(query, conversion, options),
            HasDifferentTargetKind(query[(conversion.Index + conversion.Length)..], options));
    }

    private static bool IsUnparsedMethodStaticConversion(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string direction = @"\bfrom\s+(?:instance|static)\s+(?:to|into)\s+(?:an?\s+)?(?:static|instance|non[- ]static)\b"
                                 + @"|\b(?:instance|static)\s+methods?\b.{0,120}?\b(?:to|into)\s+(?:an?\s+)?(?:static|instance|non[- ]static)\b";
        foreach (Match conversion in Regex.Matches(query, direction, options))
        {
            var after = query[(conversion.Index + conversion.Length)..];
            if (Regex.IsMatch(after, @"^\s+(?:class(?:es)?|imports?)\b", options))
                continue;

            var before = query[..conversion.Index];
            Match? action = null;
            foreach (Match candidate in Regex.Matches(before,
                         @"\b(?:convert|turn|change|make|remove)\b", options))
                action = candidate;
            if (action is null || IsNegatedAction(query, action.Index, options))
                continue;

            var requestedSubject = query[action.Index..(conversion.Index + conversion.Length)];
            var method = Regex.Match(requestedSubject, @"\bmethods?\b", options);
            var classOrImport = Regex.Match(requestedSubject,
                @"\b(?:static|instance)\s+class(?:es)?\b|\bclass(?:es)?\s+from\s+(?:static|instance)\b"
                + @"|\b(?:static\s+imports?|using\s+static)\b", options);
            if (classOrImport.Success && !method.Success)
                continue;

            return true;
        }

        return false;
    }

    private static bool IsPropertyOrFieldStaticConversion(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string member = @"\b(?:property|properties|fields?)\b";
        foreach (var clause in Regex.Split(query, @"[.;]\s", options))
        {
            foreach (Match modifierAction in Regex.Matches(clause,
                         @"\b(?:remove|add|set|apply)\s+(?:the\s+)?static(?:\s+(?:modifier|keyword))?\s+(?:from|to|on|off)\b.{0,80}?"
                         + member, options))
            {
                if (!IsNegatedAction(clause, modifierAction.Index, options))
                    return true;
            }

            foreach (Match action in Regex.Matches(clause,
                         @"\b(?:convert|turn|change|make|mark|set|apply)\b", options))
            {
                if (IsNegatedAction(clause, action.Index, options))
                    continue;
                var afterAction = clause[(action.Index + action.Length)..];
                var source = Regex.Match(afterAction, member, options);
                if (!source.Success)
                    continue;

                var afterSource = afterAction[(source.Index + source.Length)..].TrimStart();
                if (action.Value.Equals("mark", StringComparison.OrdinalIgnoreCase)
                    && Regex.IsMatch(afterSource,
                        @"^with\s+(?:the\s+)?static\s+(?:modifier|keyword)\b", options))
                    return true;
                if (HasStaticnessTarget(afterSource, options))
                    return true;
            }
        }

        return false;
    }

    private static string? StaticMemberChangeCapability(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string member = @"(?:propert(?:y|ies)|fields?)\b";
        const string context = @"(?:getters?|setters?|accessors?|attributes?|initializers?|assignments?|"
                               + StaticMemberUseSiteNoun
                               + @"|values?|modifiers?|keywords?|backing\s+fields?)\b";
        const string name = @"(?:@?[\w.:<>]+|`[^`]+`|""[^""]+""|'[^']+')";
        foreach (Match action in Regex.Matches(query,
                     @"\b(?:add|adding|create|creating|introduce|introducing|declare|declaring|remove|removing|delete|deleting|edit|editing|update|updating|change|changing|replace|replacing|modify|modifying|rewrite|rewriting|convert|converting|turn|turning)\b", options))
        {
            if (IsNegatedAction(query, action.Index, options))
                continue;

            var afterAction = query[(action.Index + action.Length)..];
            var nextTask = Regex.Match(afterAction,
                @";|[.!]\s+|\band\s+(?:add|create|introduce|declare|remove|delete|make|convert|turn|change|list|find|show)\b",
                options);
            var task = afterAction[..(nextTask.Success ? nextTask.Index : afterAction.Length)];
            var suffix = StaticMemberSuffix(task, options);
            var contextMatch = suffix is null ? null : Regex.Match(suffix,
                @"^(?:['’]s?)?(?:\s+|/\s*)(?:(?:(?:or|and)\s+|[/&]\s*)?"
                + @"(?:the\s+)?(?:static\s+)?" + member + @"(?:['’]s?)?\s+)?(?:(?:(?:named|called)\s+)?"
                + name + @"(?:['’]s)?\s+)?(?<context>" + context + @")",
                options);
            var directMember = suffix is not null && contextMatch?.Success != true;
            if (suffix is not null && contextMatch?.Success == true
                && IsStaticMemberUseSiteContext(contextMatch.Groups["context"].Value,
                    suffix[contextMatch.Length..], options))
                return "member-reference-edit";

            if (!Regex.IsMatch(action.Value,
                    @"^(?:add|adding|create|creating|introduce|introducing|declare|declaring|remove|removing|delete|deleting)$",
                    options))
                continue;
            var qualified = Regex.IsMatch(task,
                @"^\s+(?:(?:an?|the|this|these|those)\s+)?" + member
                + @"(?:\s+(?:(?:named|called)\s+)?(?:@?[\w.]+|`[^`]+`|""[^""]+""|'[^']+'))?"
                + @"\s+(?:with\s+(?:the\s+)?static\s+(?:modifier|keyword)|as\s+static)\b",
                options);
            if (!directMember && !qualified)
                continue;

            return action.Value.StartsWith("remov", StringComparison.OrdinalIgnoreCase)
                   || action.Value.StartsWith("delet", StringComparison.OrdinalIgnoreCase)
                ? "static-member-deletion"
                : "static-member-creation";
        }

        return null;
    }

    private static bool IsStaticMemberUseSiteContext(string context, string after, RegexOptions options)
    {
        return Regex.IsMatch(context, "^" + StaticMemberUseSiteNoun + "$", options)
               && !IsDeclarationAccessTail(context, after, options);
    }

    private static bool IsDeclarationAccessTail(string site, string tail, RegexOptions options)
    {
        const string visibility = @"(?:public|private(?:\s+protected)?|protected(?:\s+internal)?|internal(?:\s+protected)?)";
        const string only = @"(?:(?:get(?:ter)?|set(?:ter)?|read|write|init)[-\s]?only|readonly|writeonly)";
        const string location = @"(?:\s+(?:in|within|on)\s+.+)?[.!?]?\s*$";
        if (Regex.IsMatch(tail,
                @"^\s+(?:accessors?|modifiers?|keywords?|levels?|rights?|permissions?|visibility|accessibility)\b|^[-‐‑]only\b",
                options))
            return true;

        // A named reference or access site can itself point to a private helper or public wrapper.
        if (!Regex.IsMatch(site, @"\baccess$", options))
            return false;

        foreach (Match destination in Regex.Matches(tail,
                     @"\b(?:to|as)\s+(?:(?:an?|the)\s+)?(?:" + visibility + "|" + only + @")\b",
                     options))
        {
            var remainder = tail[(destination.Index + destination.Length)..].TrimStart();
            if (remainder.Length == 0 || Regex.IsMatch(remainder,
                    @"^(?:to|from|as|for|so|because|in|within|on|please|when|while|that|which|now|getters?|setters?|levels?|modifiers?|accessors?|rights?|permissions?|visibility)\b|^[.!?]",
                    options))
                return true;
        }

        return Regex.IsMatch(tail,
                   @"\b(?:to|from|as)\s+(?:(?:an?|the)\s+)?" + only + @"\b" + location,
                   options)
               || Regex.IsMatch(tail,
                   @"\b(?:to|from|as)\s+(?:(?:an?|the)\s+)?" + visibility + @"\b"
                   + @"(?:\s+(?:(?:to|from|as)\s+(?:(?:an?|the)\s+)?" + visibility
                   + @"|getters?|setters?|levels?|modifiers?|accessors?))*" + location,
                   options);
    }

    private static string? StaticMemberSuffix(string task, RegexOptions options)
    {
        const string member = @"(?:propert(?:y|ies)|fields?)\b";
        const string modifiers = @"(?:(?:public|private|protected|internal|file|new|readonly|const|volatile|required|unsafe)\s+)*";
        var directPrefix = Regex.Match(task,
            @"^\s+(?:(?:an?|the|this|these|those|my|any|each|every|all|some)\s+|both\s+(?:the\s+)?)?(?:existing\s+)?" + modifiers
            + @"static\s+" + modifiers, options);
        if (!directPrefix.Success)
            return null;

        var declaration = task[directPrefix.Length..];
        foreach (Match candidate in Regex.Matches(declaration, member, options))
        {
            var type = declaration[..candidate.Index].Trim();
            if (type.Length > 0
                && (Regex.IsMatch(type,
                        @"^(?:from|to|into|on|off|in|of|for|with|as|using|imports?|classes?|methods?)$", options)
                    || SyntaxFactory.ParseTypeName(type, consumeFullText: true).ContainsDiagnostics))
                continue;

            return declaration[(candidate.Index + candidate.Length)..];
        }

        return null;
    }

    private static bool IsStaticMemberReferenceFirstEdit(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        foreach (Match action in Regex.Matches(query,
                     @"\b(?:add|adding|create|creating|delete|deleting|remove|removing|edit|editing|update|updating|change|changing|replace|replacing|modify|modifying|rewrite|rewriting|convert|converting|turn|turning)\b", options))
        {
            if (IsNegatedAction(query, action.Index, options))
                continue;

            var afterAction = query[(action.Index + action.Length)..];
            var nextTask = Regex.Match(afterAction,
                @";|[.!]\s+|\band\s+(?:add|create|delete|remove|update|change|replace|modify|rewrite|list|find|show)\b",
                options);
            var task = afterAction[..(nextTask.Success ? nextTask.Index : afterAction.Length)];
            var modifier = Regex.Match(task,
                @"^\s+(?:the\s+)?static(?:\s+(?:modifier|keyword))?\s+(?:from|to|on|off)\s+",
                options);
            var target = modifier.Success ? " " + task[modifier.Length..] : task;
            var references = Regex.Match(target,
                @"^\s+(?:(?:all|every|the|an?|some|these|those|any|each|existing|both)\s+)*(?<site>"
                + StaticMemberUseSiteNoun + @")\s+(?<relation>to|of|on|from|in|for)\s+",
                options);
            var memberSuffix = references.Success
                ? StaticMemberSuffix(" " + target[references.Length..].TrimStart(), options)
                : null;
            if (references.Success
                && (!references.Groups["site"].Value.Equals("access", StringComparison.OrdinalIgnoreCase)
                    || references.Groups["relation"].Value.Equals("to", StringComparison.OrdinalIgnoreCase))
                && memberSuffix is not null
                && !IsDeclarationAccessTail(references.Groups["site"].Value, memberSuffix, options))
                return true;

            if (modifier.Success
                && StaticMemberChangeCapability(action.Value + target)
                    == "member-reference-edit")
                return true;
        }

        return false;
    }

    private static bool IsLeadingStaticMemberReferenceEdit(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var nextTask = Regex.Match(query,
            @";|[.!]\s+|(?:,\s*)?\bthen\s+|\band\s+(?:add|adding|create|creating|introduce|introducing|declare|declaring|remove|removing|delete|deleting|edit|editing|update|updating|change|changing|replace|replacing|modify|modifying|rewrite|rewriting|convert|converting|turn|turning|make|making|mark|marking|set|setting|apply|applying)\b",
            options);
        var firstTask = query[..(nextTask.Success ? nextTask.Index : query.Length)];
        return StaticMemberChangeCapability(firstTask) == "member-reference-edit"
               || IsStaticMemberReferenceFirstEdit(firstTask);
    }

    private static bool IsStandaloneNegatedStaticMemberChange(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var negation = Regex.Match(query,
            @"^\s*(?:(?:please|kindly)[,;]?\s+)?(?:i\s+)?(?:do\s+not|don['’]t|never|avoid|without)\s+(?:(?:ever|(?:want|wish)\s+to)\s+)*",
            options);
        if (!negation.Success)
            return false;

        var task = query[negation.Length..];
        if (Regex.IsMatch(task,
                @"[;.!]\s+\S|\b(?:and|but|then|while)\s+(?:list|find|show|add|create|introduce|declare|remove|delete|make|convert|turn|change)\b",
                options))
            return false;
        foreach (Match question in Regex.Matches(task, @"\?\s+(?<next>[^\s?]+)", options))
        {
            if (IsInsideTypeDelimiters(task, question.Index))
                continue;
            if (!Regex.IsMatch(question.Groups["next"].Value,
                    @"^(?:propert(?:y|ies)|fields?)\b", options))
                return false;
        }

        return StaticMemberChangeCapability(task) is not null
               || IsStaticMemberReferenceFirstEdit(task);
    }

    private static bool IsInsideTypeDelimiters(string task, int position)
    {
        var depth = 0;
        for (var index = 0; index < position; index++)
        {
            if (task[index] is '(' or '<' or '[')
                depth++;
            else if (task[index] is ')' or '>' or ']')
                depth = Math.Max(0, depth - 1);
        }

        return depth > 0;
    }

    private static bool HasStaticnessTarget(string afterMember, RegexOptions options, bool allowName = true)
    {
        const string direction = @"(?:static|instance|non[- ]?static)\b";
        const string article = @"(?:(?:an?|the)\s+)?";
        var fromTo = Regex.Match(afterMember,
            @"^from\s+" + direction + @"\s+(?:to|into)\s+" + article + direction, options);
        if (fromTo.Success && !afterMember[fromTo.Length..].StartsWith('-'))
            return true;

        var target = Regex.Match(afterMember,
            @"^(?:(?:to|into|as)\s+)?" + article + direction, options);
        if (target.Success && !afterMember[target.Length..].StartsWith('-'))
            return !Regex.IsMatch(afterMember[target.Length..],
                @"^\s+(?:class(?:es)?|imports?|analysis)\b", options);

        var names = Regex.Match(afterMember,
            @"^@?[\w.:<>]+\s+and\s+@?[\w.:<>]+\s+", options);
        if (names.Success && HasStaticnessTarget(afterMember[names.Length..], options, false))
            return true;

        var memberList = Regex.Match(afterMember,
            @"^@?[\w.:<>]+\s+and\s+(?:(?:an?|the)\s+)?(?:property|properties|fields?)\s+@?[\w.:<>]+\s+",
            options);
        if (memberList.Success && HasStaticnessTarget(afterMember[memberList.Length..], options, false))
            return true;

        var name = Regex.Match(afterMember,
            @"^(?:(?:named|called)\s+)?(?:@?[\w.:<>]+(?:\([^)]*\))?|`[^`]+`|""[^""]+""|'[^']+')\s+",
            options);
        if (!allowName || !name.Success || Regex.IsMatch(name.Value,
                @"^(?:from|in|for|with|on|of|to|into|as)\b", options))
            return false;

        return HasStaticnessTarget(afterMember[name.Length..], options, false);
    }

    private static bool IsNegatedAction(string query, int actionIndex, RegexOptions options) =>
        Regex.IsMatch(query[..actionIndex],
            @"\b(?:not|never|avoid|without|don['’]t)(?:\s+(?:ever|(?:want|wish)\s+to))*\s*$", options);

    private static Match? FirstMatch(string query, RegexOptions options, params string[] patterns)
    {
        foreach (var pattern in patterns)
        {
            var match = Regex.Match(query, pattern, options);
            if (match.Success)
                return match;
        }
        return null;
    }

    private static bool HasDifferentTargetKind(string after, RegexOptions options) =>
        Regex.IsMatch(after,
            @"^\s+(?:property|properties|fields?|events?|structs?|records?|interfaces?|namespaces?|types?|constructors?|parameters?|operators?|indexers?|local\s+functions?)\b",
            options);

    private static bool HasUnrepresentedMethodClause(string query, Match conversion, RegexOptions options)
    {
        // A recipe is safe only when the whole request is one recognized conversion task.
        // Unknown surrounding clauses may hide another action or a caller constraint.
        const string quality = @"(?:performance|testability|readability|maintainability|clarity|safety|efficiency|speed)";
        const string qualityList = @"(?:(?:better|improved|greater)\s+)?" + quality
            + @"(?:\s+and\s+(?:(?:better|improved|greater)\s+)?" + quality + @")*";
        const string prefix = @"^\s*(?:(?:(?:please|kindly)[,;]?\s*)|(?:(?:can|could|would)\s+you\s+(?:(?:please|kindly)\s+)?)|(?:i\s+(?:need|want)\s+to\s+))?$";
        const string motive = @"(?:(?:for\s+|because\s+it\s+(?:improves?|helps?)\s+|to\s+improve\s+)"
            + qualityList + @"|in\s+(?:an?\s+)?test\s+project|safely)";
        if (!Regex.IsMatch(query[..conversion.Index], prefix, options))
            return true;
        var after = query[(conversion.Index + conversion.Length)..].Trim();
        if (Regex.IsMatch(after, @"^[,;.!?]*\s*(?:(?:please|thanks|thank\s+you)\b[.!?]*)?$", options))
            return false;
        after = after.TrimStart(',', ';', '.', '!', '?', ':').TrimStart();
        if (Regex.IsMatch(after,
                @"^and\s+(?:nothing\s+else|only\s+that|no\s+other\s+(?:changes|edits|updates|tasks?))(?:,\s*please)?[.!?]*$",
                options))
            return false;
        return !Regex.IsMatch(after, @"^" + motive + @"(?:,\s*please)?[.!?]*$", options);
    }

    private static bool IsProposedChangeImpactRequest(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string impactIntentPattern =
            @"\b(?:assess|estimate|evaluate|analy[sz]e|predict)\s+(?:(?:the|a|potential|likely|project|code|symbol|overall|possible)\s+){0,3}impact\b"
            + @"|\bimpact\s+(?:assessment|of|from|on|for)\b"
            + @"|\bwhat(?:'s|\s+(?:is|(?:would|will|could|might)\s+be))\s+(?:the\s+)?impact\b"
            + @"|\bwhat\s+impact\b"
            + @"|\bhow\s+(?:would|will|could)\s+(?:\S+\s+){1,8}(?:impact|affect)\b"
            + @"|\b(?:what|which|who|will|would|could|does|do)\b.{0,100}\bbreaks?\b"
            + @"|\bwhat\s+happens?\s+(?:if|when)\b";
        var impactIntent = Regex.IsMatch(query, impactIntentPattern, options);
        return impactIntent
               && Regex.IsMatch(query,
                   @"\b(?:mov(?:e|ing|ed)|renam(?:e|ing|ed)|chang(?:e|ing|ed)|modif(?:y|ying|ied)|refactor(?:ing|ed)?|add(?:ing|ed)?|remov(?:e|ing|ed)|delet(?:e|ing|ed)|replac(?:e|ing|ed)|extract(?:ing|ed)?)\b",
                   options);
    }

    private SuggestResponse SuggestArchitectureCheck(string? solution)
    {
        if (_catalog.All.All(info => info.Name != "check-architecture"))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "check-architecture",
                UnsupportedReason: "The configured architecture check is unavailable in this command catalogue.");

        var hasMcpSolution = !string.IsNullOrWhiteSpace(solution) && Path.IsPathFullyQualified(solution);
        IReadOnlyDictionary<string, string>? arguments = hasMcpSolution
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["config"] = ".rt/architecture.json",
                ["solution"] = solution!
            }
            : null;
        var cli = "rt analyze check-architecture --config .rt/architecture.json"
                  + (string.IsNullOrWhiteSpace(solution) ? "" : $" --solution {QuoteCliArgument(solution)}")
                  + " --json";
        var step = new SuggestWorkflowStep(
            cli,
            hasMcpSolution ? "check_architecture" : null,
            arguments,
            "Check configured project and layer dependency boundaries.",
            "Stop if .rt/architecture.json is missing, repository trust is required, or the check errors; zero violations do not prove solution completeness while workspace completeness is unreported.",
            hasMcpSolution ? null : "MCP requires an absolute solution path; call suggest_command again with one.");
        return new SuggestResponse([], [], SuggestOutcome.Recommendations,
            Workflow: new SuggestWorkflow([step]));
    }

    private static bool IsConfiguredArchitectureCheck(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var normalized = query.Replace('\\', '/');
        if (Regex.IsMatch(normalized, @"\b(?:refactor|change|modify|move|fix|apply|rewrite|edit|baseline|SOLID|principles?)\b", options))
            return false;
        if (Regex.IsMatch(normalized, @"--config\b", options))
        {
            var selectedConfig = Regex.Match(normalized, @"--config(?:=|\s+)(?<path>\S+)", options);
            if (!selectedConfig.Success
                || !IsDefaultArchitectureConfigPath(selectedConfig.Groups["path"].Value.Trim('`', '(', ')', '\'', '"')))
                return false;
        }
        foreach (Match path in Regex.Matches(normalized,
                     @"[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+\.[A-Za-z0-9]{2,10}\b|[A-Za-z0-9_.-]+\.(?:json|ya?ml|toml)\b|(?:config|configs|\.rt)/[A-Za-z0-9_.-]+\b", options))
        {
            if (!IsDefaultArchitectureConfigPath(path.Value))
                return false;
        }
        foreach (Match contextPath in Regex.Matches(normalized,
                     @"\b(?:in|with|using|from|under|at)\s+(?<path>[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+)", options))
        {
            if (!IsDefaultArchitectureConfigPath(contextPath.Groups["path"].Value))
                return false;
        }

        return Regex.IsMatch(normalized, @"\b(?:configured|configuration|config)\b|\.rt/architecture\.json\b", options)
               && (Regex.IsMatch(normalized, @"\b(?:check|validate|enforce)\b", options)
                   && Regex.IsMatch(normalized, @"\b(?:violations?|boundaries?|rules?)\b", options)
                   || Regex.IsMatch(normalized, @"\b(?:find|show|list|detect|report)\b", options)
                   && Regex.IsMatch(normalized, @"\bviolations?\b", options))
               && Regex.IsMatch(normalized,
                   @"\bproject\s+(?:layer(?:ing)?|dependenc(?:y|ies)|boundar(?:y|ies))\b|\bproject\s*/\s*layer(?:ing)?\b|\blayer(?:ing)?\s+(?:rules?|boundaries?|violations?|dependenc(?:y|ies))\b|\barchitecture\s+(?:rules?|boundaries?|violations?)\b",
                   options);
    }

    private static bool IsDefaultArchitectureConfigPath(string path)
    {
        while (path.StartsWith("./", StringComparison.Ordinal))
            path = path[2..];
        return path.Equals(".rt/architecture.json", StringComparison.OrdinalIgnoreCase);
    }

    private SuggestResponse SuggestWorkingTreeReview(string query, string? solution)
    {
        const string capability = "semantic-diff-working-tree";
        if (!_workingTreeReadsAvailable)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, capability,
                UnsupportedReason: "Working-tree C# semantic diff requires Linux x64; compare two Git refs or two .cs files instead.");
        if (Regex.IsMatch(query, @"\bunstaged\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "semantic-diff-unstaged",
                UnsupportedReason: "Semantic diff compares a Git baseline with current files and includes staged changes; an unstaged-only comparison is unavailable.");
        if (!TryGetWorkingTreeBaseline(query, out var baseline))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "semantic-diff-working-tree-baseline",
                UnsupportedReason: "The request has an ambiguous or unsupported baseline. Run semantic-diff --from <ref> directly after choosing one Git ref.");
        if (_catalog.All.All(info => info.Name != "semantic-diff"))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, capability,
                UnsupportedReason: "The semantic-diff command is unavailable in this command catalogue.");

        var hasMcpTarget = !string.IsNullOrWhiteSpace(solution) && Path.IsPathFullyQualified(solution);
        var directory = hasMcpTarget ? Path.GetDirectoryName(solution!) : null;
        Dictionary<string, string>? arguments = directory is not null
            ? new Dictionary<string, string>(StringComparer.Ordinal) { ["directory"] = directory }
            : null;
        if (arguments is not null && baseline is not null)
            arguments["from"] = baseline;
        var cli = "rt analyze semantic-diff"
                  + (directory is null ? "" : $" --directory {QuoteCliArgument(directory)}")
                  + (baseline is null ? "" : $" --from {QuoteCliArgument(baseline)}")
                  + " --json";
        var step = new SuggestWorkflowStep(
            cli,
            arguments is null ? null : "semantic_diff",
            arguments,
            baseline is null
                ? "Review surfaced C# declaration changes between HEAD and current files, including staged changes."
                : $"Review surfaced C# declaration changes since {baseline}, including committed, staged, unstaged, and untracked files.",
            "Stop on a Git or content-read error; an empty result covers surfaced declarations only and does not prove unchanged behavior.",
            arguments is null ? "MCP requires an absolute solution path; call suggest_command again with one." : null);
        return new SuggestResponse([], [], SuggestOutcome.Recommendations,
            Workflow: new SuggestWorkflow([step]));
    }

    private static bool IsWorkingTreeReview(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        return Regex.IsMatch(query, @"\b(?:working[ -]tree|uncommitted|unstaged)\b", options)
               && Regex.IsMatch(query, @"(?:C#|\bCSharp\b)", options)
               && Regex.IsMatch(query, @"\b(?:review|inspect|analy[sz]e|compare|diff)\b", options)
               && !Regex.IsMatch(query, @"^\s*(?:please\s+)?(?:apply|refactor|edit|rewrite|modify|commit|stage|rename)\b", options);
    }

    private static bool TryGetWorkingTreeBaseline(string query, out string? baseline)
    {
        baseline = null;
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        const string comparator = @"(?:\b(?:against|since|from|versus|compared\s+(?:with|to)|relative\s+to)\b|\bvs\b\.?)";
        var found = false;
        foreach (Match comparative in Regex.Matches(query, comparator, options))
        {
            var tail = query[(comparative.Index + comparative.Length)..];
            if (comparative.Value.Equals("from", StringComparison.OrdinalIgnoreCase)
                && Regex.IsMatch(tail,
                    @"^\s+(?:(?:my|the|our|your|this)\s+)?working[ -]tree\b", options))
                continue;
            if (found)
                return false;
            var target = Regex.Match(tail,
                @"^\s+(?<ref>[A-Za-z0-9][A-Za-z0-9._~^/-]*)", options);
            if (!target.Success)
                return false;
            var reference = target.Groups["ref"].Value;
            if (reference.EndsWith('.') && !reference.EndsWith("..", StringComparison.Ordinal))
                reference = reference[..^1];
            var remainder = tail[(target.Groups["ref"].Index + target.Groups["ref"].Length)..];
            if (reference.Contains("..", StringComparison.Ordinal)
                || Regex.IsMatch(reference, @"^(?:my|the|our|your|this)$", options)
                || !Regex.IsMatch(remainder, @"^\s*[!?]?\s*$", options))
                return false;
            baseline = reference.Equals("HEAD", StringComparison.Ordinal)
                ? null
                : reference;
            found = true;
        }
        return true;
    }

    private SuggestResponse SuggestImplementations(string symbol, string? solution)
    {
        var command = _catalog.All.FirstOrDefault(info => info.Name == "relationships");
        if (command is null)
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "relationships-implementations");

        var cli = $"rt inspect relationships implementations {symbol}"
                  + (string.IsNullOrWhiteSpace(solution) ? "" : $" --solution {QuoteCliArgument(solution)}")
                  + " --json";
        var hasMcpSolution = !string.IsNullOrWhiteSpace(solution)
                             && Path.IsPathFullyQualified(solution);
        IReadOnlyDictionary<string, string>? arguments = hasMcpSolution
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["symbol"] = symbol,
                ["mode"] = "Implementations",
                ["solution"] = solution!
            }
            : null;

        var step = new SuggestWorkflowStep(
            cli,
            hasMcpSolution ? "relationships" : null,
            arguments,
            "Read semantic implementations of the named type before planning a change.",
            "Inspect outcome and truncation before treating results as complete; Partial or truncated results cannot prove every implementation.",
            hasMcpSolution ? null : "MCP requires an absolute solution path; call suggest_command again with one.");
        return new SuggestResponse([], [], SuggestOutcome.Recommendations,
            Workflow: new SuggestWorkflow([step]));
    }

    private SuggestResponse SuggestReferences(string symbol, string? kind, string? solution)
    {
        if (_catalog.All.All(info => info.Name != "relationships"))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported, "relationships-references");

        var cli = $"rt inspect relationships references {symbol}"
                  + (kind is null ? "" : $" --kind {kind}")
                  + (string.IsNullOrWhiteSpace(solution) ? "" : $" --solution {QuoteCliArgument(solution)}")
                  + " --json";
        var hasMcpSolution = !string.IsNullOrWhiteSpace(solution)
                             && Path.IsPathFullyQualified(solution);
        Dictionary<string, string>? arguments = hasMcpSolution
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["symbol"] = symbol,
                ["mode"] = "References",
                ["solution"] = solution!
            }
            : null;
        if (kind is not null)
            arguments?.Add("kind", kind);
        var step = new SuggestWorkflowStep(
            cli,
            hasMcpSolution ? "relationships" : null,
            arguments,
            "Read semantic references of the named symbol before editing it.",
            "Stop on an unresolved or ambiguous symbol, trust or workspace error. Inspect outcome and truncation; Partial or truncated results cannot prove every reference, and source references cannot exclude reflection, generated code, or external consumers.",
            hasMcpSolution ? null : "MCP requires an absolute solution path; call suggest_command again with one.");
        return new SuggestResponse([], [], SuggestOutcome.Recommendations,
            Workflow: new SuggestWorkflow([step]));
    }

    private static string? ImplementationSearchSymbol(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var match = Regex.Match(query,
            @"\b(?:find|show|list)\s+(?:(?:all|every|the)\s+)?(?:implementations?|implementers?)\s+of\s+(?:(?:the|an?)\s+)?(?:interface\s+|type\s+)?(?<symbol>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b",
            options);
        if (!match.Success)
            return null;
        var next = match.Index + match.Length;
        if (next < query.Length && query[next] is '.' or '<' or '[' or ':' or '?')
            return null;
        if (Regex.IsMatch(query[next..],
                @"^\s*(?:,|\band\b|\bor\b)\s+[A-Za-z_][A-Za-z0-9_.]*", options))
            return null;
        var symbol = match.Groups["symbol"].Value;
        var leafName = symbol[(symbol.LastIndexOf('.') + 1)..];
        return char.IsUpper(leafName[0]) ? symbol : null;
    }

    private static (string Symbol, string? Kind)? ReferenceSearchTarget(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var match = Regex.Match(query,
            @"\b(?:find|show|list)\s+(?:(?:all|every|the)\s+)?references?\s+(?:to|of)\s+(?:(?:the|an?)\s+)?(?:(?<kind>method|field|property|event|type|symbol)\s+)?(?:(?<explicitName>named|called)\s+)?(?<symbol>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\b",
            options);
        if (!match.Success)
            return null;
        var prefix = query[..match.Index].Trim();
        if (!Regex.IsMatch(prefix,
                @"^(?:please|can you|could you|I need to|I'd like to)?$", options))
            return null;
        var next = match.Index + match.Length;
        var tail = query[next..].Trim();
        if (!Regex.IsMatch(tail,
                @"^(?:[.!?]?|,\s*please[.!?]?|before\s+(?:(?:I|we|you)\s+)?(?:review|edit|change|modify|rename|move|refactor|delete|reviewing|editing|changing|modifying|renaming|moving|refactoring|deleting)(?:\s+(?:it|the\s+(?:symbol|type|method|interface)))?[.!?]?)$",
                options))
            return null;
        var symbol = match.Groups["symbol"].Value;
        var kind = match.Groups["kind"].Value.ToLowerInvariant();
        return IsPlausibleReferenceSymbol(symbol, match.Groups["explicitName"].Success)
            ? (symbol, kind is "" or "symbol" ? null : kind)
            : null;
    }

    private static bool IsPlausibleReferenceSymbol(string symbol, bool explicitName)
    {
        var leafName = symbol[(symbol.LastIndexOf('.') + 1)..];
        return char.IsUpper(leafName[0])
               || leafName[0] == '_'
               || leafName.Any(char.IsUpper)
               || symbol.Contains('.', StringComparison.Ordinal)
               || explicitName;
    }

    private static string QuoteCliArgument(string value)
    {
        var safeCharacters = OperatingSystem.IsWindows()
            ? @"^[A-Za-z0-9_./:\-]+$"
            : @"^[A-Za-z0-9_./-]+$";
        if (Regex.IsMatch(value, safeCharacters, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking))
            return value;

        return OperatingSystem.IsWindows()
            ? $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"
            : $"'{value.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
    }

    private static string? UnavailablePlanCapability(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var namedCapability = query.Trim();
        if (namedCapability.Equals("verify-plan", StringComparison.OrdinalIgnoreCase))
            return "verify-plan";
        if (namedCapability.Equals("prepare-plan", StringComparison.OrdinalIgnoreCase))
            return "prepare-plan";

        // A symbol or source file named Plan is not a request for a change-plan artifact.
        var task = Regex.Replace(query,
            @"\b(?:named|called|class|struct|interface|type|method|file)\s+Plan\b|\bPlan\s+(?:class|struct|interface|type|method|file)\b|\bPlan\.(?:cs|csproj)\b",
            " ", options);
        if (!Regex.IsMatch(task, @"\bplans?\b", options))
            return null;
        if (Regex.IsMatch(task,
                @"\b(?:verify|validate|check|review)\s+(?:(?:a|an|the|my|our|existing|prepared|reviewed|immutable|revision-bound|change|stored|saved|proposed|pending|rename|signature|refactor|refactoring)\s+){0,6}plans?\b",
                options)
            || Regex.IsMatch(task, @"\bplans?\s+(?:verification|validation)\b", options)
            || Regex.IsMatch(task,
                @"\bplans?\s+(?:and|then)\s+(?:verify|validate|check|review)\s+(?:it|them)\b",
                options))
            return "verify-plan";
        return Regex.IsMatch(task,
                   @"\b(?:prepare|preparing|create|generate|produce|build)\b(?:\s+[\w-]+){0,8}\s+plans?\b",
                   options)
               || Regex.IsMatch(task,
                   @"\b(?:immutable|revision-bound)\b(?:\s+[\w-]+){0,8}\s+plans?\b",
                   options)
            ? "prepare-plan"
            : null;
    }

    private static SuggestResponse? UnsupportedDependencyRequest(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var dependency = Regex.IsMatch(query, @"\b(?:dependenc(?:y|ies)|references?|packages?)\b", options);
        var coreLayer = Regex.IsMatch(query, @"\b(?:core|domain)\s+(?:layers?|projects?)\b", options);
        var thirdParty = Regex.IsMatch(query,
            @"\b(?:non[- ]Microsoft|third[- ]party|external|other\s+than\s+Microsoft|excluding\s+Microsoft)\b",
            options);
        if (dependency && coreLayer && thirdParty
            && Regex.IsMatch(query, @"\b(?:extract(?:ed)?|remov(?:e|ed|ing)|mov(?:e|ed)|decoupl(?:e|ed)|isolat(?:e|ed))\b", options)
            && Regex.IsMatch(query, @"\b(?:all|every)\b", options))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported,
                "third-party-dependency-extraction",
                UnsupportedReason: "rt has no verified workflow to extract all non-Microsoft dependencies from core layers. Inventory package and project references and source usage first, then change and verify one dependency at a time.");

        if (Regex.IsMatch(query,
                @"\bNuGet\s+(?:packages?|dependenc(?:y|ies)|references?)\b|\b(?:third[- ]party|external)\s+packages?\b|\bpackages?\s+(?:dependenc(?:y|ies)|references?|inventory|versions?|licenses?|used|installed)\b|\bPackageReference\s+(?:entries|items)\b",
                options))
            return new SuggestResponse([], [], SuggestOutcome.Unsupported,
                "package-dependency-inventory",
                UnsupportedReason: "rt has no NuGet package inventory. Inspect PackageReference and Directory.Packages.props, then use the installed .NET SDK package listing for affected projects; dependency-graph covers C# symbols only.");

        return null;
    }

    private static bool IsDiagnosticDeltaRequest(string query)
    {
        const RegexOptions options = RegexOptions.IgnoreCase
                                     | RegexOptions.CultureInvariant
                                     | RegexOptions.NonBacktracking;
        var diagnosticKind = Regex.Match(query,
            @"\b(?:compiler|analy[sz]er)\s+(?:diagnostics?|errors?|warnings?)\b|\bCS[0-9]{4}\b",
            options);
        var sourceComparison = Regex.Match(query,
            @"\b(?:C#|CSharp|source|code)\s+(?:declarations?|symbols?)\b|^\s*(?:please\s+)?(?:compare|diff|review)\s+(?:C#|CSharp|source|code|declarations?|symbols?)(?:\b|\s)",
            options);
        var repairVerb = Regex.Match(query, @"\b(?:fix|repair|resolve|suppress)\b", options);
        var comparisonVerb = Regex.Match(query, @"\b(?:compare|diff)\b", options);
        var repairIsNoun = repairVerb.Success && Regex.IsMatch(query[..repairVerb.Index],
            @"\b(?:a|an|the|this|that|code)\s+$", options);
        var repairRequest = repairVerb.Success
                            && !repairIsNoun
                            && (!comparisonVerb.Success || repairVerb.Index < comparisonVerb.Index);
        var explicitDiagnosticComparison = Regex.IsMatch(query,
            @"\b(?:compare|diff)\s+(?:(?:the|all|current|new)\s+)*(?:(?:compiler|analy[sz]er)\s+(?:diagnostics?|errors?|warnings?)|CS[0-9]{4})\b",
            options);
        if (!diagnosticKind.Success
            || !explicitDiagnosticComparison
            && (sourceComparison.Success && sourceComparison.Index < diagnosticKind.Index
                || repairRequest
                || Regex.IsMatch(query,
                    @"^\s*(?:please\s+)?(?:edit|change|apply|refactor)\b",
                    options)))
            return false;

        var beforeAndAfter = Regex.IsMatch(query, @"\bbefore\b", options)
                             && Regex.IsMatch(query, @"\bafter\b", options);
        const string revisionRef = @"(?:HEAD|main|(?:release|feature|hotfix|bugfix)/[\w.-]+)";
        var againstBranch = Regex.IsMatch(query, $@"\bagainst\s+{revisionRef}\b", options)
                            && !Regex.IsMatch(query,
                                @"\bagainst\s+main\s+(?:project|application|assembly)\b",
                                options);
        var pairedRevisions = Regex.IsMatch(query,
            $@"\bbetween\s+{revisionRef}\s+and\s+{revisionRef}\b|\bfrom\s+{revisionRef}\s+to\s+{revisionRef}\b",
            options);
        var revisionCue = againstBranch
                          || pairedRevisions
                          || Regex.IsMatch(query, @"\b(?:since|relative\s+to|(?:Git\s+)?(?:branches?|revisions?|commits?))\b", options)
                          || Regex.IsMatch(query,
                              @"\b(?:previous|prior|last)\s+(?:build|commit|revision|branch)\b|\b(?:current|this|last|previous)\s+(?:change|commit|branch|revision)\b|\b(?:after|before)\s+(?:this\s+)?(?:change|commit|revision)\b",
                              options)
                          || Regex.Count(query,
                              @"\b(?:release|feature|hotfix|bugfix)/[\w.-]+\b",
                              options) >= 2;
        var deltaIntent = Regex.IsMatch(query,
            @"\b(?:compare|comparison|diff|difference|delta|introduc(?:e|ed|ing)|appeared|removed|resolved|regressions?|since)\b",
            options);
        return beforeAndAfter || revisionCue && deltaIntent;
    }

    private static SuggestResult ToResult(ScoredCommand s) => new(
        Name: s.Command.Name,
        Score: s.Score,
        Category: s.Command.Category,
        WhenToUse: s.Command.WhenToUse,
        PrimaryExample: s.Command.Examples.Length > 0 ? s.Command.Examples[0] : "",
        AllExamples: s.Command.Examples);

    private static List<SuggestResult> BuildRelated(
        List<SuggestResult> results,
        IReadOnlyList<RtCommandInfo> catalog)
    {
        // See-also lookup. Activates once RtCommandInfo carries SeeAlso; until
        // then this is a no-op so the service ships before the schema change.
        if (results.Count == 0)
            return [];
        var top = catalog.FirstOrDefault(c => c.Name == results[0].Name);
        if (top is null || top.SeeAlso is null || top.SeeAlso.Length == 0)
            return [];
        var lookup = catalog.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var related = new List<SuggestResult>(top.SeeAlso.Length);
        foreach (var name in top.SeeAlso)
        {
            if (!lookup.TryGetValue(name, out var info))
                continue;
            related.Add(new SuggestResult(
                Name: info.Name,
                Score: 0,
                Category: info.Category,
                WhenToUse: info.WhenToUse,
                PrimaryExample: info.Examples.Length > 0 ? info.Examples[0] : "",
                AllExamples: info.Examples));
        }
        return related;
    }
}
