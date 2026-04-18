using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// A single structural match of a pattern against a location in the solution.
/// </summary>
public record PatternMatch(
    string File,
    int Line,
    int EndLine,
    Dictionary<string, string> Substitutions,
    string Preview);

/// <summary>
/// Walks a Roslyn solution looking for statement sequences whose SyntaxKind tree
/// matches a given pattern block, modulo identifier and literal text.
/// </summary>
public static class PatternMatcher
{
    /// <summary>
    /// Find every location in <paramref name="solution"/> that structurally matches the
    /// statements in <paramref name="filePath"/> between <paramref name="startLine"/> and
    /// <paramref name="endLine"/> (1-based, inclusive).
    /// </summary>
    public static async Task<List<PatternMatch>> FindMatchesAsync(
        Solution solution,
        string filePath,
        int startLine,
        int endLine,
        string? scope)
    {
        var absPath = Path.GetFullPath(filePath);
        var patternStatements = await ExtractPatternStatementsAsync(solution, absPath, startLine, endLine);

        var results = new List<PatternMatch>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                if (document.FilePath == null)
                    continue;

                if (scope != null && !IsInScope(document, project, scope))
                    continue;

                var root = await document.GetSyntaxRootAsync();
                if (root == null)
                    continue;

                var matches = FindMatchesInDocument(document.FilePath, root, patternStatements, absPath, startLine, endLine);
                results.AddRange(matches);
            }
        }

        return results;
    }

    // ── Extract pattern statements from the given file and line range ────────────

    private static async Task<List<StatementSyntax>> ExtractPatternStatementsAsync(
        Solution solution,
        string absPath,
        int startLine,
        int endLine)
    {
        var doc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(d.FilePath, absPath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"File not found in solution: {absPath}");

        var root = await doc.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("Failed to get syntax root.");

        var statements = SelectStatementsInRange(root, startLine, endLine);

        if (statements.Count == 0)
            throw new InvalidOperationException(
                $"No statements found in lines {startLine}–{endLine}. " +
                "The range must contain a sequence of statements inside a BlockSyntax.");

        // Verify all statements share the same parent BlockSyntax
        var parents = statements.Select(s => s.Parent).Distinct().ToList();
        if (parents.Count != 1 || parents[0] is not BlockSyntax)
            throw new InvalidOperationException(
                "Selected statements must all be direct children of the same BlockSyntax.");

        return statements;
    }

    // ── Walk a document looking for matching statement sequences ─────────────────

    private static List<PatternMatch> FindMatchesInDocument(
        string docPath,
        SyntaxNode root,
        List<StatementSyntax> pattern,
        string patternFilePath,
        int patternStartLine,
        int patternEndLine)
    {
        var results = new List<PatternMatch>();

        foreach (var block in root.DescendantNodes().OfType<BlockSyntax>())
        {
            var stmts = block.Statements;
            if (stmts.Count < pattern.Count)
                continue;

            for (var i = 0; i <= stmts.Count - pattern.Count; i++)
            {
                var candidate = stmts.Skip(i).Take(pattern.Count).ToList();
                if (TryMatch(pattern, candidate, out var substitutions))
                {
                    // Skip the pattern source itself
                    var candidateStart = candidate[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                    var candidateEnd = candidate[^1].GetLocation().GetLineSpan().EndLinePosition.Line + 1;

                    if (string.Equals(docPath, patternFilePath, StringComparison.OrdinalIgnoreCase)
                        && LinesOverlap(candidateStart, candidateEnd, patternStartLine, patternEndLine))
                    {
                        continue;
                    }

                    var preview = BuildPreview(candidate);
                    results.Add(new PatternMatch(docPath, candidateStart, candidateEnd, substitutions, preview));
                }
            }
        }

        return results;
    }

    // ── Structural comparison ─────────────────────────────────────────────────────

    private static bool TryMatch(
        List<StatementSyntax> pattern,
        List<StatementSyntax> candidate,
        out Dictionary<string, string> substitutions)
    {
        substitutions = new Dictionary<string, string>(StringComparer.Ordinal);

        for (var i = 0; i < pattern.Count; i++)
        {
            if (!NodesMatch(pattern[i], candidate[i], substitutions))
            {
                substitutions.Clear();
                return false;
            }
        }

        return true;
    }

    private static bool NodesMatch(SyntaxNode pattern, SyntaxNode candidate, Dictionary<string, string> subs)
    {
        if (pattern.RawKind != candidate.RawKind)
            return false;

        // Identifier: record the substitution, check consistency
        if (pattern is IdentifierNameSyntax patId && candidate is IdentifierNameSyntax candId)
        {
            var pName = patId.Identifier.Text;
            var cName = candId.Identifier.Text;
            if (subs.TryGetValue(pName, out var existing))
                return existing == cName;
            subs[pName] = cName;
            return true;
        }

        // Literal: values may differ — just record them if the pattern key is a literal token
        if (pattern is LiteralExpressionSyntax patLit && candidate is LiteralExpressionSyntax candLit)
        {
            var pText = patLit.Token.Text;
            var cText = candLit.Token.Text;
            if (subs.TryGetValue(pText, out var existing))
                return existing == cText;
            subs[pText] = cText;
            return true;
        }

        // Token nodes (leaf): must match exactly
        var patChildren = pattern.ChildNodesAndTokens().ToList();
        var candChildren = candidate.ChildNodesAndTokens().ToList();

        if (patChildren.Count != candChildren.Count)
            return false;

        for (var i = 0; i < patChildren.Count; i++)
        {
            var pc = patChildren[i];
            var cc = candChildren[i];

            if (pc.IsNode && cc.IsNode)
            {
                if (!NodesMatch(pc.AsNode()!, cc.AsNode()!, subs))
                    return false;
            }
            else if (pc.IsToken && cc.IsToken)
            {
                var pt = pc.AsToken();
                var ct = cc.AsToken();
                if (pt.RawKind != ct.RawKind)
                    return false;

                // For value-bearing tokens (string, number, etc.) already handled above as
                // LiteralExpressionSyntax; for identifiers, handled above as IdentifierNameSyntax.
                // Remaining tokens (keywords, operators, punctuation) must match textually.
                if (!pt.IsKind(SyntaxKind.IdentifierToken) && pt.Text != ct.Text)
                    return false;

                // Bare identifier tokens (e.g. in variable declarations) — check substitution map
                if (pt.IsKind(SyntaxKind.IdentifierToken))
                {
                    var pText = pt.Text;
                    var cText = ct.Text;
                    if (subs.TryGetValue(pText, out var existing))
                    {
                        if (existing != cText) return false;
                    }
                    else
                    {
                        subs[pText] = cText;
                    }
                }
            }
            else
            {
                // One is a node, the other is a token — can't match
                return false;
            }
        }

        return true;
    }

    // ── Scope filtering ───────────────────────────────────────────────────────────

    private static bool IsInScope(Document document, Project project, string scope)
    {
        // Check project name first
        if (project.Name.StartsWith(scope, StringComparison.OrdinalIgnoreCase))
            return true;

        // Check folder path relative to project
        var projectDir = Path.GetDirectoryName(project.FilePath ?? "");
        var docPath = document.FilePath ?? "";
        if (projectDir != null && docPath.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
        {
            var relative = docPath[projectDir.Length..].Replace('\\', '/').TrimStart('/');
            // Convert path-like scope (e.g. "RoslynTools.Core") to path prefix
            var scopePath = scope.Replace('.', '/');
            if (relative.StartsWith(scopePath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        // Check namespace in the file: see if any namespace declaration matches
        // (quick heuristic via file path / project name)
        if (document.FilePath != null)
        {
            var folders = document.Folders;
            var ns = string.Join(".", new[] { project.DefaultNamespace ?? project.Name }.Concat(folders));
            if (ns.StartsWith(scope, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static List<StatementSyntax> SelectStatementsInRange(SyntaxNode root, int startLine, int endLine)
        => root.DescendantNodes()
               .OfType<StatementSyntax>()
               .Where(s => s.Parent is BlockSyntax)
               .Where(s =>
               {
                   var span = s.GetLocation().GetLineSpan();
                   return span.StartLinePosition.Line >= startLine - 1
                       && span.EndLinePosition.Line <= endLine - 1;
               })
               .ToList();

    private static bool LinesOverlap(int aStart, int aEnd, int bStart, int bEnd)
        => aStart <= bEnd && bStart <= aEnd;

    private static string BuildPreview(List<StatementSyntax> statements)
    {
        var first = statements[0].ToFullString().Trim();
        // Trim to one line for a concise preview
        var newlineIdx = first.IndexOfAny(['\r', '\n']);
        if (newlineIdx >= 0)
            first = first[..newlineIdx].TrimEnd() + " …";
        return first.Length > 120 ? first[..120] + " …" : first;
    }
}
