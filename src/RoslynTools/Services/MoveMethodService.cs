using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class MoveMethodService : IRtService
{
    private readonly IDocumentLocatorService _documents;
    private readonly ILogger<MoveMethodService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly ISymbolResolutionService _symbols;

    public MoveMethodService()
        : this(new RtConsoleLogger<MoveMethodService>(), new SolutionChangeWriter())
    {
    }

    public MoveMethodService(ILogger<MoveMethodService> logger, ISolutionChangeWriter solutionChangeWriter,
        ISymbolResolutionService? symbols = null,
        IDocumentLocatorService? documents = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();
        _documents = documents ?? new DocumentLocatorService();

        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    ///     Move a static method from its current type to <paramref name="targetTypeFqn" />,
    ///     rewriting all call sites across the solution.
    ///     Returns <c>false</c> (and logs to stderr) on validation failure; throws on
    ///     unexpected lookup failure (method/type not found in index).
    /// </summary>
    internal async Task<bool> RunAsync(
        Solution sln,
        string methodFqn,
        string targetTypeFqn,
        bool createFile,
        bool dryRun)
    {
        var originalSolution = sln;

        // ── 1. Locate source method ───────────────────────────────────────────
        IMethodSymbol methodSymbol;
        try
        {
            methodSymbol = await _symbols.FindMethodBySpecAsync(sln, methodFqn);
        }
        catch (InvalidOperationException ex)
        {
            _logger.RtError("Error: {Message}", ex.Message);
            Environment.ExitCode = 1;
            return false;
        }

        // ── 2. Validate: must be static ───────────────────────────────────────
        if (!methodSymbol.IsStatic)
        {
            _logger.RtError(
                "Error: '{MethodFqn}' is not static. move-method only supports static methods.",
                methodFqn);
            Environment.ExitCode = 1;
            return false;
        }

        // ── 3. Locate target type ─────────────────────────────────────────────
        INamedTypeSymbol? targetType = null;
        try
        {
            targetType = (INamedTypeSymbol)await _symbols.FindTypeByFqnAsync(sln, targetTypeFqn);
        }
        catch (InvalidOperationException)
        {
            if (!createFile)
            {
                _logger.RtError(
                    "Error: Target type '{TargetType}' not found. Pass --create-file to create it.",
                    targetTypeFqn);
                Environment.ExitCode = 1;
                return false;
            }
            // createFile == true: proceed with targetType == null; new file will be created in step 9.
        }

        // ── 4. Validate: no name collision in target ──────────────────────────
        if (targetType != null)
        {
            var collision = targetType.GetMembers(methodSymbol.Name)
                .OfType<IMethodSymbol>()
                .Any();
            if (collision)
            {
                _logger.RtError(
                    "Error: Target type '{TargetType}' already has a method named '{MethodName}'.",
                    targetTypeFqn,
                    methodSymbol.Name);
                Environment.ExitCode = 1;
                return false;
            }
        }

        // ── 5. Extract method declaration node ────────────────────────────────
        var declLocation = methodSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (declLocation?.SourceTree == null)
        {
            _logger.RtError("Error: Cannot locate source for '{MethodFqn}'.", methodFqn);
            Environment.ExitCode = 1;
            return false;
        }

        var declDocId = _documents.FindDocumentId(sln, methodSymbol);
        if (declDocId == null)
        {
            _logger.RtError("Error: Cannot locate document for '{MethodFqn}'.", methodFqn);
            Environment.ExitCode = 1;
            return false;
        }

        var declDoc = sln.GetDocument(declDocId)!;
        var declRoot = (await declDoc.GetSyntaxRootAsync())!;
        var declNode = declRoot.FindNode(declLocation.SourceSpan);
        var methodDecl = declNode.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
        if (methodDecl == null)
        {
            _logger.RtError("Error: Cannot extract method syntax for '{MethodFqn}'.", methodFqn);
            Environment.ExitCode = 1;
            return false;
        }

        // ── 6. Collect all call-site reference locations ──────────────────────
        var refs = await SymbolFinder.FindReferencesAsync(methodSymbol, sln);
        var callSitesByDoc = refs
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id)
            .ToDictionary(g => g.Key, g => g.Select(l => l.Location.SourceSpan).ToList());

        // ── 7. Locate target type document ────────────────────────────────────
        DocumentId? targetDocId = null;
        if (targetType != null)
            targetDocId = _documents.FindDocumentId(sln, targetType);

        // ── 8. Rewrite: remove method from source, insert into target, update call sites ──
        var targetClassName = targetTypeFqn.Contains('.')
            ? targetTypeFqn[(targetTypeFqn.LastIndexOf('.') + 1)..]
            : targetTypeFqn;

        // Normalise the method declaration: strip leading trivia for insertion,
        // keep the method clean with a normalised newline.
        var movedMethodDecl = methodDecl
            .WithLeadingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed);

        // Collect all document IDs that need touching.
        var allDocIds = new HashSet<DocumentId>(callSitesByDoc.Keys);
        allDocIds.Add(declDocId);
        if (targetDocId != null) allDocIds.Add(targetDocId);

        foreach (var docId in allDocIds)
        {
            var doc = sln.GetDocument(docId)!;
            var root = (await doc.GetSyntaxRootAsync())!;

            var isSourceDoc = docId == declDocId;
            var isTargetDoc = targetDocId != null && docId == targetDocId;
            callSitesByDoc.TryGetValue(docId, out var callSpans);

            root = TransformDocument(
                root,
                isSourceDoc,
                isTargetDoc,
                methodDecl,
                movedMethodDecl,
                methodSymbol.Name,
                targetClassName,
                callSpans);

            sln = sln.WithDocumentSyntaxRoot(docId, root);
        }

        // ── 9. Handle --create-file (target type doesn't exist yet) ──────────
        if (targetType == null && createFile)
        {
            // Build a new file with a minimal class containing the method.
            var targetNs = targetTypeFqn.Contains('.')
                ? targetTypeFqn[..targetTypeFqn.LastIndexOf('.')]
                : string.Empty;

            var newFileContent = BuildNewTargetFile(targetNs, targetClassName, movedMethodDecl);

            // Place file alongside the source document.
            var sourceProject = sln.GetDocument(declDocId)!.Project;
            var sourceDir = Path.GetDirectoryName(sln.GetDocument(declDocId)!.FilePath) ?? "";
            var newFilePath = Path.Combine(sourceDir, targetClassName + ".cs");

            var newDocInfo = DocumentInfo.Create(
                DocumentId.CreateNewId(sourceProject.Id),
                targetClassName + ".cs",
                filePath: newFilePath,
                loader: TextLoader.From(
                    TextAndVersion.Create(
                        SourceText.From(newFileContent, Encoding.UTF8),
                        VersionStamp.Create(),
                        newFilePath)));

            sln = sln.AddDocument(newDocInfo);
        }

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        return true;
    }

    // ── Syntax transformation ─────────────────────────────────────────────────

    internal SyntaxNode TransformDocument(
        SyntaxNode root,
        bool isSourceDoc,
        bool isTargetDoc,
        MethodDeclarationSyntax originalDecl,
        MethodDeclarationSyntax movedDecl,
        string methodName,
        string targetClassName,
        IList<TextSpan>? callSpans)
    {
        // Collect invocation nodes for this document's call spans.
        var callInvocations = new List<(SyntaxNode Node, bool IsQualified)>();
        if (callSpans != null)
            foreach (var span in callSpans)
            {
                var refNode = root.FindNode(span);
                var invocation = refNode.AncestorsAndSelf()
                    .OfType<InvocationExpressionSyntax>()
                    .FirstOrDefault();
                if (invocation == null) continue;

                // Determine if call is already qualified: Source.Method(...)
                var isQualified = invocation.Expression is MemberAccessExpressionSyntax;
                callInvocations.Add((invocation, isQualified));
            }

        var nodesToReplace = new HashSet<SyntaxNode>(ReferenceEqualityComparer.Instance);
        foreach (var (node, _) in callInvocations) nodesToReplace.Add(node);

        MethodDeclarationSyntax? declToRemove = null;
        if (isSourceDoc)
        {
            // Find method by span identity rather than reference (root may differ from lookup root)
            declToRemove = root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(m =>
                    m.Identifier.Text == originalDecl.Identifier.Text &&
                    m.Span == originalDecl.Span);
            if (declToRemove != null)
                nodesToReplace.Add(declToRemove);
        }

        ClassDeclarationSyntax? targetClass = null;
        if (isTargetDoc)
            targetClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == targetClassName);

        if (nodesToReplace.Count == 0 && targetClass == null)
            return root;

        // Rewrite call sites and remove source declaration in one pass.
        var removeAnnotation = new SyntaxAnnotation("move-method-remove");

        root = root.ReplaceNodes(nodesToReplace, (original, rewritten) =>
        {
            // Rewrite call sites: Foo(args) or Source.Foo(args) → Target.Foo(args)
            if (original is InvocationExpressionSyntax && callInvocations.Any(c => ReferenceEquals(c.Node, original)))
            {
                var inv = (InvocationExpressionSyntax)rewritten;
                ExpressionSyntax newExpr;
                if (inv.Expression is MemberAccessExpressionSyntax memberAccess)
                    // Replace the receiver: Source → Target
                    newExpr = memberAccess.WithExpression(
                        SyntaxFactory.IdentifierName(targetClassName)
                            .WithTriviaFrom(memberAccess.Expression));
                else
                    // Unqualified call: Foo(args) → Target.Foo(args)
                    newExpr = SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        SyntaxFactory.IdentifierName(targetClassName),
                        SyntaxFactory.Token(SyntaxKind.DotToken),
                        SyntaxFactory.IdentifierName(methodName));
                return inv.WithExpression(newExpr);
            }

            // Mark method declaration for removal
            if (original is MethodDeclarationSyntax && declToRemove != null &&
                ReferenceEquals(original, declToRemove))
                return rewritten.WithAdditionalAnnotations(removeAnnotation);

            return rewritten;
        });

        // Remove the marked method declaration
        if (isSourceDoc && declToRemove != null)
        {
            var markedDecl = root.GetAnnotatedNodes(removeAnnotation)
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();
            if (markedDecl != null)
                root = root.RemoveNode(markedDecl, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        // Insert method into target class
        if (isTargetDoc && targetClass != null)
        {
            // Re-find targetClass after the ReplaceNodes pass
            var currentTargetClass = root.DescendantNodes()
                .OfType<ClassDeclarationSyntax>()
                .FirstOrDefault(c => c.Identifier.Text == targetClassName);
            if (currentTargetClass != null)
            {
                var updated = currentTargetClass.AddMembers(movedDecl);
                root = root.ReplaceNode(currentTargetClass, updated);
            }
        }

        return root;
    }

    // ── New-file scaffolding ──────────────────────────────────────────────────

    private string BuildNewTargetFile(
        string namespaceName,
        string className,
        MethodDeclarationSyntax method)
    {
        var methodText = method.NormalizeWhitespace().ToFullString();
        if (!string.IsNullOrEmpty(namespaceName))
            return $$"""
                     namespace {{namespaceName}};

                     public static class {{className}}
                     {
                     {{methodText}}
                     }
                     """;

        return $$"""
                 public static class {{className}}
                 {
                 {{methodText}}
                 }
                 """;
    }
}
