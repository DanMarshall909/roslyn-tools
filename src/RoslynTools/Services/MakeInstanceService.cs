using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides services for creating and transforming instances
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class MakeInstanceService : IRtService
{
    private readonly ILogger<MakeInstanceService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public MakeInstanceService()
        : this(new RtConsoleLogger<MakeInstanceService>(), new SolutionChangeWriter())
    {
    }

    public MakeInstanceService(ILogger<MakeInstanceService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Runs asynchronous instance transformation operations
    /// </summary>
    internal async Task RunAsync(Solution sln, string methodSpec, bool dryRun)
    {
        var originalSolution = sln;

        var (typeName, methodName) = ParseMethodSpec(methodSpec);

        var methodSymbol = await FindStaticMethodAsync(sln, typeName, methodName)
                           ?? throw new InvalidOperationException(
                               $"Static method '{methodSpec}' not found in solution.");

        _logger.RtInfo("Found method '{Method}'. Collecting call sites...", methodSymbol.ToDisplayString());

        var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);

        // Group reference locations by document (from the original solution)
        var locationsByDoc = refs
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

        var total = locationsByDoc.Values.Sum(l => l.Count);
        _logger.RtInfo("Found {Total} call site(s). Analysing reachability...", total);

        // Find the document containing the method declaration
        var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource)
                           ?? throw new InvalidOperationException("No source location for method declaration.");
        var declDocId = FindDocumentIdForLocation(sln, declLocation)
                        ?? throw new InvalidOperationException("Cannot locate source document for method declaration.");

        // Process all documents that have call sites plus the declaration document
        var allDocIds = new HashSet<DocumentId>(locationsByDoc.Keys) { declDocId };
        var skipped = new List<(string file, int line, string reason)>();

        foreach (var docId in allDocIds)
        {
            var doc = sln.GetDocument(docId)!;
            var root = (await doc.GetSyntaxRootAsync())!;
            var semanticModel = await doc.GetSemanticModelAsync();
            if (semanticModel is null) continue;

            locationsByDoc.TryGetValue(docId, out var callSpans);
            var isDeclarationDoc = docId == declDocId;

            var (newRoot, docSkipped) = TransformDocument(
                root, semanticModel, methodSymbol, methodSymbol.ContainingType,
                callSpans ?? [], isDeclarationDoc, doc);

            foreach (var s in docSkipped) skipped.Add(s);

            sln = sln.WithDocumentSyntaxRoot(docId, newRoot);
        }

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, sln, dryRun);

        PrintSummary(total, skipped);
    }

    // ── Document transformation ──────────────────────────────────────────────

    /// <summary>
    ///     Transforms one document in a single ReplaceNodes pass:
    ///     • Removes the `static` modifier from the method declaration (if this is the declaration doc)
    ///     • Rewrites qualifying call sites from Type.Method(...) to instance.Method(...)
    ///     • Warns and skips call sites where no instance is reachable
    /// </summary>
    private (SyntaxNode newRoot, List<(string file, int line, string reason)> skipped)
        TransformDocument(
            SyntaxNode root,
            SemanticModel semanticModel,
            IMethodSymbol methodSymbol,
            INamedTypeSymbol targetType,
            List<TextSpan> callSpans,
            bool isDeclarationDoc,
            Document doc)
    {
        var skipped = new List<(string file, int line, string reason)>();
        var replacements = new Dictionary<SyntaxNode, SyntaxNode>(ReferenceEqualityComparer.Instance);

        // ── 1. Remove `static` from the declaration ──────────────────────────
        if (isDeclarationDoc)
        {
            var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (declLocation is not null)
            {
                var declNode = root.FindNode(declLocation.SourceSpan);
                var methodDecl = declNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                if (methodDecl is not null)
                {
                    var newModifiers = methodDecl.Modifiers
                        .Where(t => !t.IsKind(SyntaxKind.StaticKeyword))
                        .ToArray();
                    var newDecl = methodDecl.WithModifiers(SyntaxFactory.TokenList(newModifiers));
                    replacements[methodDecl] = newDecl;
                }
            }
        }

        // ── 2. Rewrite call sites ────────────────────────────────────────────
        var fileText = doc.GetTextAsync().GetAwaiter().GetResult();
        var filePath = doc.FilePath ?? doc.Name;

        foreach (var span in callSpans)
        {
            var refNode = root.FindNode(span);
            var invocation = refNode.AncestorsAndSelf()
                .OfType<InvocationExpressionSyntax>()
                .FirstOrDefault();

            if (invocation is null) continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess) continue;

            var lineNum = fileText.Lines.GetLineFromPosition(span.Start).LineNumber + 1;

            var reachability =
                InstanceReachabilityAnalyzer.FindReachableInstance(invocation, semanticModel, targetType);

            if (reachability.Kind == ReachableInstanceKind.None)
            {
                _logger.RtWarning("{FilePath}:{LineNum}: no reachable instance — skipped", filePath, lineNum);
                skipped.Add((filePath, lineNum, "no reachable instance"));
                continue;
            }

            if (reachability.Kind == ReachableInstanceKind.Ambiguous)
            {
                _logger.RtWarning("{FilePath}:{LineNum}: ambiguous reachable instance — skipped", filePath, lineNum);
                skipped.Add((filePath, lineNum, "ambiguous reachable instance"));
                continue;
            }

            var instanceExpr = reachability.Expression
                               ?? throw new InvalidOperationException(
                                   "Reachability analysis returned no expression for a matched instance.");

            var newMemberAccess = memberAccess
                .WithExpression(instanceExpr.WithTriviaFrom(memberAccess.Expression))
                .WithTriviaFrom(memberAccess);

            if (!replacements.ContainsKey(invocation.Expression))
                replacements[invocation.Expression] = newMemberAccess;
        }

        if (replacements.Count == 0)
            return (root, skipped);

        var newRoot = root.ReplaceNodes(
            replacements.Keys,
            (original, _) => replacements[original]);

        return (newRoot, skipped);
    }

    // ── Symbol finding ───────────────────────────────────────────────────────

    private async Task<IMethodSymbol?> FindStaticMethodAsync(Solution sln, string typeName, string methodName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var sym in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
            {
                if (sym is not INamedTypeSymbol typeSymbol) continue;

                var method = typeSymbol.GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.IsStatic);

                if (method != null)
                    return method;
            }
        }

        return null;
    }

    // ── Parsing ──────────────────────────────────────────────────────────────

    private (string TypeName, string MethodName) ParseMethodSpec(string spec)
    {
        var lastDot = spec.LastIndexOf('.');
        if (lastDot <= 0 || lastDot == spec.Length - 1)
            throw new InvalidOperationException(
                $"--method must be in the form Type.Method, got: '{spec}'");

        return (spec[..lastDot], spec[(lastDot + 1)..]);
    }

    // ── Document/location utilities ──────────────────────────────────────────

    private DocumentId? FindDocumentIdForLocation(Solution sln, Location location)
    {
        foreach (var project in sln.Projects)
            foreach (var doc in project.Documents)
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                    return doc.Id;

        return null;
    }

    // ── Output ───────────────────────────────────────────────────────────────

    private void PrintSummary(int total, List<(string file, int line, string reason)> skipped)
    {
        var rewritten = total - skipped.Count;
        RtConsole.WriteLine(RtOutputContext.Result,
            $"\nSummary: {total} call site(s) found, {rewritten} rewritten, {skipped.Count} skipped.");

        foreach (var (file, line, reason) in skipped)
            RtConsole.WriteLine(RtOutputContext.Result, $"  SKIPPED {file}:{line} — {reason}");
    }
}
