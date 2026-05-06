using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

internal sealed record FindDuplicatesOptions(
    int MinStatements = 3,
    int MinOccurrences = 2,
    string? Scope = null,
    bool IncludeTests = false,
    // 40-statement cap: windows larger than this are rarely extractable as a unit
    // and significantly increase enumeration time on large solutions.
    int MaxStatements = 40);

internal sealed record DuplicateOccurrence(
    string FilePath,
    int StartLine,
    int EndLine,
    string ContainingType,
    string ContainingMember,
    IReadOnlyDictionary<string, string>? SubstitutionMap = null);

internal sealed record DuplicateGroup(
    string GroupHash,
    string GroupId,
    IReadOnlyList<DuplicateOccurrence> Occurrences,
    string Preview,
    string FullBlock,
    int StatementCount,
    double Score,
    bool PartiallyExpandable = false,
    string Extractability = "unknown",
    IReadOnlyList<ExtractabilityCheck>? Checks = null);
[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class FindDuplicatesService : IRtService
{
    private readonly ILogger<FindDuplicatesService> _logger;

    public FindDuplicatesService()
        : this(new RtConsoleLogger<FindDuplicatesService>())
    {
    }

    public FindDuplicatesService(ILogger<FindDuplicatesService> logger)
    {
        _logger = logger;
    }
    // Boilerplate patterns excluded from duplicate reporting:
    //   - single `return;` or `return <expr>;`
    //   - single `throw new NotImplementedException(...);`
    //   - empty blocks (zero statements — not reachable as window but kept for clarity)
    private bool IsBoilerplateWindow(IReadOnlyList<StatementSyntax> statements)
    {
        if (statements.Count != 1)
            return false;

        var stmt = statements[0];

        if (stmt is ReturnStatementSyntax)
            return true;

        if (stmt is ThrowStatementSyntax throwStmt
            && throwStmt.Expression is ObjectCreationExpressionSyntax creation)
        {
            var typeName = creation.Type.ToString();
            if (typeName.EndsWith("NotImplementedException", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    // Tracks a single occurrence of a window candidate during bottom-up expansion.
    private sealed class CandidateLoc
    {
        public required string FilePath { get; init; }
        public required List<StatementSyntax> Body { get; init; }
        public required int StartIdx { get; init; }
        public required string ContainingType { get; init; }
        public required string ContainingMember { get; init; }
        public required SemanticModel SemanticModel { get; init; }
        public int Length { get; set; } = 1;

        public IReadOnlyList<StatementSyntax> Window =>
            Body.Skip(StartIdx).Take(Length).ToList();

        public bool CanExtend => StartIdx + Length < Body.Count;
    }

    public async Task<IReadOnlyList<DuplicateGroup>> RunAsync(
        Solution solution,
        FindDuplicatesOptions options)
    {
        var projectCount = solution.Projects.Count();
        var methodCount = 0;

        // Phase 1: collect all single-statement seeds from every method body.
        // Group by hash of the length-1 window.
        var seeds = new Dictionary<string, List<CandidateLoc>>(StringComparer.Ordinal);
        var rtIgnore = RtIgnore.ForSolution(solution);

        foreach (var project in solution.Projects)
        {
            // Test-project detection: a project whose name ends with `.Tests` or `Test`
            // (case-insensitive) is considered a test project. When --include-tests is false
            // (the default), such projects are skipped entirely.
            if (!options.IncludeTests && IsTestProject(project))
                continue;

            // Scope filter — project name takes priority over namespace prefix.
            // If the scope matches a project name exactly, only that project is enumerated.
            // Otherwise, scope is treated as a namespace prefix and applied per-document.
            var scopeMatchesProject = options.Scope is not null
                && string.Equals(project.Name, options.Scope, StringComparison.OrdinalIgnoreCase);

            if (options.Scope is not null
                && !scopeMatchesProject
                && !ScopeCouldMatchProject(project, options.Scope))
                continue;

            foreach (var document in project.Documents)
            {
                if (document.FilePath is null || rtIgnore.IsIgnored(document.FilePath))
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                // Namespace-prefix scope filter (applied only when scope did not match project name).
                if (options.Scope is not null && !scopeMatchesProject)
                {
                    var ns = GetDocumentNamespace(root);
                    if (ns is null || !ns.StartsWith(options.Scope, StringComparison.Ordinal))
                        continue;
                }

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel is null)
                    continue;

                var methods = root.DescendantNodes()
                    .Where(n => n is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
                    .ToList();

                methodCount += methods.Count;

                foreach (var method in methods)
                {
                    var body = method switch
                    {
                        MethodDeclarationSyntax m => m.Body,
                        LocalFunctionStatementSyntax lf => lf.Body,
                        _ => null
                    };

                    if (body is null)
                        continue;

                    var stmts = body.Statements.ToList();
                    if (stmts.Count < options.MinStatements)
                        continue;

                    var containingType = GetContainingTypeName(method);
                    var containingMember = GetMemberName(method);

                    for (var i = 0; i < stmts.Count; i++)
                    {
                        var loc = new CandidateLoc
                        {
                            FilePath = document.FilePath,
                            Body = stmts,
                            StartIdx = i,
                            ContainingType = containingType,
                            ContainingMember = containingMember,
                            SemanticModel = semanticModel,
                            Length = 1,
                        };

                        // Filter single-statement boilerplate at seed time.
                        if (IsBoilerplateWindow(loc.Window))
                            continue;

                        var hash = ComputeCanonicalHash(loc.Window, semanticModel);
                        if (!seeds.TryGetValue(hash, out var list))
                            seeds[hash] = list = [];
                        list.Add(loc);
                    }
                }
            }
        }

        _logger.RtInfo("[find-duplicates] projects={ProjectCount} methods={MethodCount} seeds={SeedCount}", projectCount, methodCount, seeds.Count);

        // Phase 2: bottom-up expansion.
        // Start with seed groups that meet MinOccurrences.
        // At each round, try to extend every surviving candidate by one statement.
        //
        // Reporting rule:
        //   - A group is reported when it can no longer fully agree on an extension:
        //     some locations are terminal (body exhausted) or some diverge to a different hash.
        //   - When a split occurs, ALL current locations are reported at the current length
        //     (PartiallyExpandable=true), while the surviving subgroups continue expanding.
        //   - When all locations terminate together (no extension possible for any),
        //     they are reported as PartiallyExpandable=false.

        // candidates: hash -> List<CandidateLoc>, all at the same Length
        var candidates = seeds
            .Where(kv => kv.Value.Count >= options.MinOccurrences)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        var results = new List<DuplicateGroup>();

        while (candidates.Count > 0)
        {
            var nextCandidates = new Dictionary<string, List<CandidateLoc>>(StringComparer.Ordinal);

            foreach (var (currentHash, locations) in candidates)
            {
                var currentLength = locations[0].Length;

                // Hard cap on window size — treat locations at max as terminal.
                var extendable = locations.Where(l => l.CanExtend && l.Length < options.MaxStatements).ToList();
                var terminal = locations.Where(l => !l.CanExtend || l.Length >= options.MaxStatements).ToList();

                // Re-group extendable by the hash of window extended by one statement.
                var extGroups = extendable
                    .GroupBy(l => ComputeExtendedHash(l), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

                // Determine which extGroups have enough occurrences to continue.
                var continuingGroups = extGroups
                    .Where(eg => eg.Value.Count >= options.MinOccurrences)
                    .ToList();

                // A "split" occurs when:
                //   - some locations are terminal (can't extend), OR
                //   - extendable locations diverge across multiple hashes (more than one continuing group), OR
                //   - some extendable locations form groups below MinOccurrences (become orphans).
                var hasSplit = terminal.Count > 0
                    || continuingGroups.Count > 1
                    || extGroups.Any(eg => eg.Value.Count < options.MinOccurrences);

                // Snapshot occurrence data and window BEFORE mutating lengths.
                // CandidateLoc.Window is computed lazily from loc.Length, so we must
                // capture before the increment loop below.
                var currentWindow = locations[0].Window;
                var currentOccurrences = locations.Select(BuildOccurrence).ToList();
                // Capture raw windows + models for extractability analysis.
                var rawOccurrences = locations
                    .Select(l => (Window: l.Window, Model: l.SemanticModel, SubstitutionMap: (IReadOnlyDictionary<string, string>?)BuildOccurrence(l).SubstitutionMap))
                    .ToList();

                // Queue continuing subgroups for next round.
                foreach (var (extHash, extLocs) in continuingGroups)
                {
                    foreach (var loc in extLocs)
                        loc.Length++;
                    if (!nextCandidates.TryGetValue(extHash, out var existing))
                        nextCandidates[extHash] = existing = [];
                    existing.AddRange(extLocs);
                }

                // Decide what to report at the current length.
                if (currentLength >= options.MinStatements)
                {
                    if (hasSplit)
                    {
                        // Report ALL current locations (full group before split) as PartiallyExpandable.
                        if (currentOccurrences.Count >= options.MinOccurrences)
                        {
                            var stmtCount = currentWindow.Count;
                            var occCount = currentOccurrences.Count;
                            var (extractability, checks) = ExtractabilityAnalyzer.Analyze(currentWindow, rawOccurrences);
                            // Score: log(occurrences) * statementCount * extractabilityWeight
                            var score = Math.Log(occCount) * stmtCount * ExtractabilityWeight(extractability);
                            results.Add(new DuplicateGroup(
                                GroupHash: currentHash,
                                GroupId: ComputeGroupId(currentHash, stmtCount),
                                Occurrences: currentOccurrences,
                                Preview: BuildPreview(currentWindow),
                                FullBlock: BuildFullBlock(currentWindow),
                                StatementCount: stmtCount,
                                Score: score,
                                PartiallyExpandable: continuingGroups.Count > 0,
                                Extractability: extractability,
                                Checks: checks));
                        }
                    }
                    else if (terminal.Count == locations.Count)
                    {
                        // All locations are terminal — report the full group, not expandable.
                        // currentOccurrences == terminal occurrences since nothing was continuing.
                        var stmtCount = currentWindow.Count;
                        var occCount = currentOccurrences.Count;
                        var (extractability, checks) = ExtractabilityAnalyzer.Analyze(currentWindow, rawOccurrences);
                        // Score: log(occurrences) * statementCount * extractabilityWeight
                        var score = Math.Log(occCount) * stmtCount * ExtractabilityWeight(extractability);
                        results.Add(new DuplicateGroup(
                            GroupHash: currentHash,
                            GroupId: ComputeGroupId(currentHash, stmtCount),
                            Occurrences: currentOccurrences,
                            Preview: BuildPreview(currentWindow),
                            FullBlock: BuildFullBlock(currentWindow),
                            StatementCount: stmtCount,
                            Score: score,
                            PartiallyExpandable: false,
                            Extractability: extractability,
                            Checks: checks));
                    }
                    // Otherwise: all extendable and all continuing to a single group — don't report yet.
                }
            }

            candidates = nextCandidates;
        }

        // Phase 3: overlap suppression.
        // Group A is suppressed if ALL of A's occurrences are spatially covered by some
        // occurrence of a different group B (same file, B.StartLine ≤ A.StartLine AND
        // B.EndLine ≥ A.EndLine). This removes smaller sub-windows that are fully contained
        // within a larger reported group.
        var suppressed = new HashSet<int>();
        for (var a = 0; a < results.Count; a++)
        {
            var groupA = results[a];
            var allCovered = groupA.Occurrences.All(occA =>
                Enumerable.Range(0, results.Count)
                    .Where(b => b != a)
                    .Any(b =>
                    {
                        var groupB = results[b];
                        return groupB.Occurrences.Any(occB =>
                            string.Equals(occB.FilePath, occA.FilePath, StringComparison.OrdinalIgnoreCase)
                            && occB.StartLine <= occA.StartLine
                            && occB.EndLine >= occA.EndLine);
                    }));

            if (allCovered)
                suppressed.Add(a);
        }

        var filtered = results
            .Where((_, idx) => !suppressed.Contains(idx))
            .OrderByDescending(g => g.Score)
            .ToList();

        _logger.RtInfo("[find-duplicates] groups={GroupCount} after-suppression={FilteredCount}", results.Count, filtered.Count);

        return filtered;
    }

    /// <summary>
    /// Maps an extractability level to a score weight.
    /// extractable=1.0, likely_extractable=0.7, discovery_only=0.3, unknown=1.0 (unanalysed).
    /// </summary>
    private double ExtractabilityWeight(string extractability) => extractability switch
    {
        "extractable" => 1.0,
        "likely_extractable" => 0.7,
        "discovery_only" => 0.3,
        _ => 1.0,
    };

    /// <summary>
    /// Computes a short, stable, human-readable group identifier from the canonical hash
    /// and statement count. Format: "GRP-{stmtCount}-{first8hexOfHash}".
    /// The id is deterministic across runs for the same code shape.
    /// </summary>
    private string ComputeGroupId(string groupHash, int statementCount)
        => $"GRP-{statementCount}-{groupHash[..8].ToLowerInvariant()}";

    private string ComputeExtendedHash(CandidateLoc loc)
    {
        var extWindow = loc.Body.Skip(loc.StartIdx).Take(loc.Length + 1).ToList();
        return ComputeCanonicalHash(extWindow, loc.SemanticModel);
    }

    private DuplicateOccurrence BuildOccurrence(CandidateLoc loc)
    {
        var window = loc.Window;
        var startLine = window[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var endLine = window[^1].GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        var (_, subs) = BlockNormalizer.Normalize(window, loc.SemanticModel);
        return new DuplicateOccurrence(
            loc.FilePath,
            startLine,
            endLine,
            loc.ContainingType,
            loc.ContainingMember,
            SubstitutionMap: subs);
    }

    private string ComputeCanonicalHash(IReadOnlyList<StatementSyntax> statements, SemanticModel semanticModel)
    {
        var (canonicalText, _) = BlockNormalizer.Normalize(statements, semanticModel);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonicalText));
        return Convert.ToHexString(bytes);
    }

    private string BuildPreview(IReadOnlyList<StatementSyntax> statements)
    {
        var first = statements[0].NormalizeWhitespace().ToFullString().Trim();
        var newlineIdx = first.IndexOfAny(['\r', '\n']);
        if (newlineIdx >= 0)
            first = first[..newlineIdx].TrimEnd() + " …";
        return first.Length > 120 ? first[..120] + " …" : first;
    }

    private string BuildFullBlock(IReadOnlyList<StatementSyntax> statements) =>
        string.Join("\n", statements.Select(s => s.NormalizeWhitespace().ToFullString().Trim()));

    private string GetContainingTypeName(SyntaxNode node)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return typeDecl?.Identifier.Text ?? "<unknown>";
    }

    private string GetMemberName(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        LocalFunctionStatementSyntax lf => lf.Identifier.Text,
        _ => "<unknown>"
    };

    // Test-project detection: name ends with ".Tests" or "Test" (case-insensitive).
    // AdhocWorkspace tests use project names only — no metadata references to check.
    private bool IsTestProject(Project project)
    {
        var name = project.Name;
        return name.EndsWith(".Tests", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("Test", StringComparison.OrdinalIgnoreCase)
            || name.Contains("xunit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("nunit", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mstest", StringComparison.OrdinalIgnoreCase);
    }

    // Returns true if the project *might* contain documents in the given namespace scope.
    // Used to skip whole projects early when scope is a namespace prefix (not a project name).
    // Since we can't know without parsing, we always return true here — the per-document
    // check below is the real filter. This method exists so the logic is extensible.
    private bool ScopeCouldMatchProject(Project project, string scope) => true;

    // Returns the first namespace declaration in the document, preferring file-scoped.
    private string? GetDocumentNamespace(SyntaxNode root)
    {
        var fileScoped = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScoped is not null)
            return fileScoped.Name.ToString();

        var blockScoped = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        return blockScoped?.Name.ToString();
    }
}
