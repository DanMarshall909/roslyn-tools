using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

internal sealed record FindDuplicatesOptions(
    int MinStatements = 3,
    int MinOccurrences = 2);

internal sealed record DuplicateOccurrence(
    string FilePath,
    int StartLine,
    int EndLine,
    string ContainingType,
    string ContainingMember);

internal sealed record DuplicateGroup(
    string GroupHash,
    IReadOnlyList<DuplicateOccurrence> Occurrences,
    string Preview,
    int StatementCount,
    bool PartiallyExpandable = false);

internal static class FindDuplicatesCore
{
    // Boilerplate patterns excluded from duplicate reporting:
    //   - single `return;` or `return <expr>;`
    //   - single `throw new NotImplementedException(...);`
    //   - empty blocks (zero statements — not reachable as window but kept for clarity)
    private static bool IsBoilerplateWindow(IReadOnlyList<StatementSyntax> statements)
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
        public required IReadOnlyList<StatementSyntax> Body { get; init; }
        public required int StartIdx { get; init; }
        public required string ContainingType { get; init; }
        public required string ContainingMember { get; init; }
        public int Length { get; set; } = 1;

        public IReadOnlyList<StatementSyntax> Window =>
            Body.Skip(StartIdx).Take(Length).ToList();

        public bool CanExtend => StartIdx + Length < Body.Count;
    }

    public static async Task<IReadOnlyList<DuplicateGroup>> RunAsync(
        Solution solution,
        FindDuplicatesOptions options)
    {
        var projectCount = solution.Projects.Count();
        var methodCount = 0;

        // Phase 1: collect all single-statement seeds from every method body.
        // Group by hash of the length-1 window.
        var seeds = new Dictionary<string, List<CandidateLoc>>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath is null)
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root is null)
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
                            Length = 1,
                        };

                        // Filter single-statement boilerplate at seed time.
                        if (IsBoilerplateWindow(loc.Window))
                            continue;

                        var hash = ComputeWindowHash(loc.Window);
                        if (!seeds.TryGetValue(hash, out var list))
                            seeds[hash] = list = [];
                        list.Add(loc);
                    }
                }
            }
        }

        Console.Error.WriteLine($"[find-duplicates] projects={projectCount} methods={methodCount} seeds={seeds.Count}");

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

                var extendable = locations.Where(l => l.CanExtend).ToList();
                var terminal = locations.Where(l => !l.CanExtend).ToList();

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
                            results.Add(new DuplicateGroup(
                                GroupHash: currentHash,
                                Occurrences: currentOccurrences,
                                Preview: BuildPreview(currentWindow),
                                StatementCount: currentWindow.Count,
                                PartiallyExpandable: continuingGroups.Count > 0));
                        }
                    }
                    else if (terminal.Count == locations.Count)
                    {
                        // All locations are terminal — report the full group, not expandable.
                        // currentOccurrences == terminal occurrences since nothing was continuing.
                        results.Add(new DuplicateGroup(
                            GroupHash: currentHash,
                            Occurrences: currentOccurrences,
                            Preview: BuildPreview(currentWindow),
                            StatementCount: currentWindow.Count,
                            PartiallyExpandable: false));
                    }
                    // Otherwise: all extendable and all continuing to a single group — don't report yet.
                }
            }

            candidates = nextCandidates;
        }

        Console.Error.WriteLine($"[find-duplicates] groups={results.Count}");

        return results;
    }

    private static string ComputeExtendedHash(CandidateLoc loc)
    {
        var extWindow = loc.Body.Skip(loc.StartIdx).Take(loc.Length + 1).ToList();
        return ComputeWindowHash(extWindow);
    }

    private static DuplicateOccurrence BuildOccurrence(CandidateLoc loc)
    {
        var window = loc.Window;
        var startLine = window[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1;
        var endLine = window[^1].GetLocation().GetLineSpan().EndLinePosition.Line + 1;
        return new DuplicateOccurrence(
            loc.FilePath,
            startLine,
            endLine,
            loc.ContainingType,
            loc.ContainingMember);
    }

    private static string ComputeWindowHash(IReadOnlyList<StatementSyntax> statements)
    {
        var sb = new StringBuilder();
        foreach (var stmt in statements)
            sb.Append(stmt.NormalizeWhitespace().ToFullString());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string BuildPreview(IReadOnlyList<StatementSyntax> statements)
    {
        var first = statements[0].NormalizeWhitespace().ToFullString().Trim();
        var newlineIdx = first.IndexOfAny(['\r', '\n']);
        if (newlineIdx >= 0)
            first = first[..newlineIdx].TrimEnd() + " …";
        return first.Length > 120 ? first[..120] + " …" : first;
    }

    private static string GetContainingTypeName(SyntaxNode node)
    {
        var typeDecl = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return typeDecl?.Identifier.Text ?? "<unknown>";
    }

    private static string GetMemberName(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        LocalFunctionStatementSyntax lf => lf.Identifier.Text,
        _ => "<unknown>"
    };
}
