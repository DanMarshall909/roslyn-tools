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
    int StatementCount);

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

    public static async Task<IReadOnlyList<DuplicateGroup>> RunAsync(
        Solution solution,
        FindDuplicatesOptions options)
    {
        var projectCount = solution.Projects.Count();
        var methodCount = 0;
        var candidateCount = 0;

        // hash -> list of occurrences
        var groups = new Dictionary<string, (List<DuplicateOccurrence> Occurrences, string Preview, int StatementCount)>(StringComparer.Ordinal);

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

                    var stmts = body.Statements;
                    if (stmts.Count < options.MinStatements)
                        continue;

                    var containingType = GetContainingTypeName(method);
                    var containingMember = GetMemberName(method);

                    foreach (var window in EnumerateWindows(stmts, options.MinStatements))
                    {
                        if (IsBoilerplateWindow(window))
                            continue;

                        candidateCount++;
                        var hash = ComputeWindowHash(window);

                        var startLine = window[0].GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                        var endLine = window[^1].GetLocation().GetLineSpan().EndLinePosition.Line + 1;

                        var occurrence = new DuplicateOccurrence(
                            document.FilePath,
                            startLine,
                            endLine,
                            containingType,
                            containingMember);

                        if (!groups.TryGetValue(hash, out var entry))
                        {
                            var preview = BuildPreview(window);
                            groups[hash] = ([occurrence], preview, window.Count);
                        }
                        else
                        {
                            entry.Occurrences.Add(occurrence);
                        }
                    }
                }
            }
        }

        Console.Error.WriteLine($"[find-duplicates] projects={projectCount} methods={methodCount} candidates={candidateCount}");

        return groups.Values
            .Where(e => e.Occurrences.Count >= options.MinOccurrences)
            .Select(e => new DuplicateGroup(
                GroupHash: string.Empty,
                Occurrences: e.Occurrences,
                Preview: e.Preview,
                StatementCount: e.StatementCount))
            .ToList();
    }

    private static IEnumerable<List<StatementSyntax>> EnumerateWindows(
        SyntaxList<StatementSyntax> statements,
        int minLength)
    {
        var stmtList = statements.ToList();
        var count = stmtList.Count;

        for (var windowLen = minLength; windowLen <= count; windowLen++)
        {
            for (var start = 0; start + windowLen <= count; start++)
            {
                yield return stmtList.GetRange(start, windowLen);
            }
        }
    }

    private static string ComputeWindowHash(List<StatementSyntax> statements)
    {
        var sb = new StringBuilder();
        foreach (var stmt in statements)
            sb.Append(stmt.NormalizeWhitespace().ToFullString());

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes);
    }

    private static string BuildPreview(List<StatementSyntax> statements)
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
