using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;

namespace RoslynTools.Core;

internal static class MoveCore
{
    // ──────────────────────────────────────────────────────────────
    //  Entry points
    // ──────────────────────────────────────────────────────────────

    internal static async Task ExecuteNamespaceMoveAsync(
        Workspace workspace,
        Solution solution,
        string sourceNamespace,
        string targetNamespace,
        bool moveFile,
        bool dryRun)
    {
        var originalSolution = workspace.CurrentSolution;

        var (updated, declarationFiles, projectDir, rootNamespace, changedFiles) =
            await ScanSolutionForNamespaceAsync(solution, sourceNamespace, targetNamespace);

        if (changedFiles == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"No types found in namespace '{sourceNamespace}'.");
            return;
        }

        RtLog.Info(
            "Rewriting '{SourceNamespace}' -> '{TargetNamespace}': {DeclarationFileCount} declaration file(s), {ChangedFileCount} total file(s) changed.",
            sourceNamespace,
            targetNamespace,
            declarationFiles.Count,
            changedFiles);

        await SolutionWriter.ApplyChangesAsync(originalSolution, updated, dryRun);

        ApplyFileMoveOrNote(declarationFiles, projectDir, rootNamespace, sourceNamespace, targetNamespace, moveFile, dryRun);
    }

    internal static async Task ExecuteSingleTypeMoveAsync(
        Workspace workspace,
        Solution sln,
        string fqn,
        string targetNamespace,
        bool moveFile,
        bool dryRun)
    {
        var sym = await SymbolLocator.FindTypeByFqnAsync(sln, fqn);
        var oldNamespace = sym.ContainingNamespace.ToDisplayString();

        RtLog.Info("Moving '{TypeName}' from '{OldNamespace}' to '{TargetNamespace}'...", sym.ToDisplayString(), oldNamespace, targetNamespace);

        var (declarationFiles, projectDir, rootNamespace) = CollectDeclarationPaths(sln, sym);

        // Capture references BEFORE the rewrite. After RewriteAllDeclarationsAsync, consumer
        // references that resolved through `using <oldNamespace>;` become unbound, and
        // SymbolFinder against the post-rewrite compilation can no longer find them.
        var references = await SymbolFinder.FindReferencesAsync(sym, sln);

        sln = await RewriteAllDeclarationsAsync(sln, sym, targetNamespace);

        sln = await ApplyReferenceUsingsAsync(sln, references, oldNamespace, targetNamespace);

        await SolutionWriter.ApplyChangesAsync(workspace.CurrentSolution, sln, dryRun);

        if (moveFile && projectDir != null && rootNamespace != null)
            MoveSourceFiles(declarationFiles, projectDir, rootNamespace, oldNamespace, targetNamespace, dryRun);
        else if (!moveFile)
            RtConsole.WriteLine(RtOutputContext.Result, "\nNOTE: File path unchanged. Consider moving the source file to match the new namespace.");
    }

    // ──────────────────────────────────────────────────────────────
    //  Namespace-move helpers
    // ──────────────────────────────────────────────────────────────

    private static async Task<(Solution Solution, List<string> DeclarationFiles, string? ProjectDir, string? RootNamespace, int ChangedFiles)>
        ScanSolutionForNamespaceAsync(Solution solution, string sourceNamespace, string targetNamespace)
    {
        var declarationFiles = new List<string>();
        string? projectDir = null;
        string? rootNamespace = null;
        int changedFiles = 0;

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath == null) continue;

                var result = await RewriteDocumentNamespaceAsync(solution, doc, sourceNamespace, targetNamespace);
                if (!result.Changed) continue;

                solution = result.Solution;
                changedFiles++;

                if (result.IsDeclarationFile)
                {
                    declarationFiles.Add(doc.FilePath);
                    if (projectDir == null)
                        (projectDir, rootNamespace) = result.ProjectMeta ?? (null, null);
                }
            }
        }

        return (solution, declarationFiles, projectDir, rootNamespace, changedFiles);
    }

    private record DocumentRewriteResult(
        bool Changed,
        Solution Solution,
        bool IsDeclarationFile,
        (string? ProjectDir, string? RootNamespace)? ProjectMeta);

    private static async Task<DocumentRewriteResult> RewriteDocumentNamespaceAsync(
        Solution solution,
        Document doc,
        string sourceNamespace,
        string targetNamespace)
    {
        var root = await doc.GetSyntaxRootAsync();
        if (root == null) return new DocumentRewriteResult(false, solution, false, null);

        var rewriter = new NamespaceMoveRewriter(sourceNamespace, targetNamespace);
        var newRoot = rewriter.Visit(root);

        if (!rewriter.Changed) return new DocumentRewriteResult(false, solution, false, null);

        solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);

        var meta = rewriter.IsDeclarationFile ? ResolveProjectMeta(doc.Project) : ((string?, string?)?)null;
        return new DocumentRewriteResult(true, solution, rewriter.IsDeclarationFile, meta);
    }

    private static (string? ProjectDir, string? RootNamespace) ResolveProjectMeta(Project project)
    {
        var dir = Path.GetDirectoryName(project.FilePath);
        var ns = project.DefaultNamespace;
        if (string.IsNullOrEmpty(ns))
            ns = Path.GetFileNameWithoutExtension(project.FilePath ?? "");
        return (dir, ns);
    }

    private static void ApplyFileMoveOrNote(
        List<string> declarationFiles,
        string? projectDir,
        string? rootNamespace,
        string sourceNamespace,
        string targetNamespace,
        bool moveFile,
        bool dryRun)
    {
        if (moveFile && projectDir != null && rootNamespace != null)
            MoveSourceFiles(declarationFiles, projectDir, rootNamespace, sourceNamespace, targetNamespace, dryRun);
        else if (!moveFile && declarationFiles.Count > 0)
            RtConsole.WriteLine(RtOutputContext.Result, "\nNOTE: File paths unchanged. Use --move-file to relocate declaration files.");
    }

    // ──────────────────────────────────────────────────────────────
    //  Single-type helpers
    // ──────────────────────────────────────────────────────────────

    private static (List<string> Paths, string? ProjectDir, string? RootNamespace) CollectDeclarationPaths(
        Solution sln, ISymbol symbol)
    {
        var paths = new List<string>();
        string? projectDir = null;
        string? rootNamespace = null;

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var doc = sln.GetDocument(syntaxRef.SyntaxTree);
            if (doc?.FilePath == null) continue;

            paths.Add(doc.FilePath);
            if (projectDir == null)
                (projectDir, rootNamespace) = ResolveProjectMeta(doc.Project);
        }

        return (paths, projectDir, rootNamespace);
    }

    private static async Task<Solution> RewriteAllDeclarationsAsync(
        Solution solution,
        ISymbol symbol,
        string targetNamespace)
    {
        var oldNamespace = symbol.ContainingNamespace.ToDisplayString();

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var doc = solution.GetDocument(syntaxRef.SyntaxTree);
            if (doc == null) continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            var node = await syntaxRef.GetSyntaxAsync();
            var nsDecl = node.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().FirstOrDefault();
            if (nsDecl == null) continue;
            if (nsDecl.Name.ToString() == targetNamespace) continue;

            var needsOldNsUsing = await DocumentReferencesNamespaceAsync(doc, symbol, oldNamespace);

            var newNameNode = SyntaxFactory.ParseName(targetNamespace).WithTriviaFrom(nsDecl.Name);
            var newRoot = root.ReplaceNode(nsDecl, nsDecl.WithName(newNameNode));

            if (needsOldNsUsing)
            {
                bool _ = false;
                newRoot = EnsureTargetNamespaceUsing(newRoot, oldNamespace, ref _);
            }

            solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
        }

        return solution;
    }

    private static async Task<bool> DocumentReferencesNamespaceAsync(
        Document doc,
        ISymbol movedSymbol,
        string namespaceName)
    {
        var semanticModel = await doc.GetSemanticModelAsync();
        if (semanticModel == null) return false;

        var root = await doc.GetSyntaxRootAsync();
        if (root == null) return false;

        foreach (var nameNode in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var sym = semanticModel.GetSymbolInfo(nameNode).Symbol;
            if (sym is not INamedTypeSymbol typeSymbol) continue;
            if (SymbolEqualityComparer.Default.Equals(typeSymbol, movedSymbol)) continue;
            if (typeSymbol.ContainingNamespace?.ToDisplayString() == namespaceName)
                return true;
        }

        return false;
    }

    private static async Task<Solution> ApplyReferenceUsingsAsync(
        Solution solution,
        IEnumerable<ReferencedSymbol> references,
        string oldNamespace,
        string targetNamespace)
    {
        var locationsByDoc = references
            .SelectMany(r => r.Locations)
            .GroupBy(loc => loc.Document.Id);

        foreach (var group in locationsByDoc)
        {
            var doc = solution.GetDocument(group.Key);
            if (doc == null) continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            var tracker = root.TrackNodes(
                group.Select(loc => root.FindNode(loc.Location.SourceSpan)).ToArray());

            var (updatedRoot, changed) = RewriteFullyQualifiedReferences(
                root, tracker, group, oldNamespace, targetNamespace);

            updatedRoot = EnsureTargetNamespaceUsing(updatedRoot, targetNamespace, ref changed);

            if (changed)
                solution = solution.WithDocumentSyntaxRoot(doc.Id, updatedRoot);
        }

        return solution;
    }

    private static (SyntaxNode Root, bool Changed) RewriteFullyQualifiedReferences(
        SyntaxNode root,
        SyntaxNode tracker,
        IGrouping<DocumentId, ReferenceLocation> group,
        string oldNamespace,
        string targetNamespace)
    {
        bool changed = false;
        var currentRoot = tracker;

        foreach (var loc in group)
        {
            var originalNode = root.FindNode(loc.Location.SourceSpan);
            var trackedNode = currentRoot.GetCurrentNode(originalNode);
            if (trackedNode == null) continue;

            var qualifiedRoot = FindOutermostQualifiedName(trackedNode);
            if (qualifiedRoot == null) continue;

            var oldFqText = qualifiedRoot.ToFullString().Trim();
            var newFqText = oldFqText.Replace(oldNamespace + ".", targetNamespace + ".");
            if (oldFqText == newFqText) continue;

            var newNode = SyntaxFactory.ParseName(newFqText).WithTriviaFrom(qualifiedRoot);
            currentRoot = currentRoot.ReplaceNode(qualifiedRoot, newNode);
            changed = true;
        }

        return (currentRoot, changed);
    }

    private static SyntaxNode EnsureTargetNamespaceUsing(
        SyntaxNode root,
        string targetNamespace,
        ref bool changed)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        var existingUsings = compilationUnit.Usings
            .Select(u => u.Name?.ToFullString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existingUsings.Contains(targetNamespace))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(" " + targetNamespace))
            .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

        changed = true;
        return compilationUnit.AddUsings(newUsing);
    }

    private static SyntaxNode? FindOutermostQualifiedName(SyntaxNode node)
    {
        SyntaxNode current = node;
        while (current.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax)
            current = current.Parent;
        return current != node ? current : null;
    }

    // ──────────────────────────────────────────────────────────────
    //  File movement helpers
    // ──────────────────────────────────────────────────────────────

    internal static void MoveSourceFiles(
        List<string> filePaths,
        string projectDir,
        string rootNamespace,
        string oldNamespace,
        string targetNamespace,
        bool dryRun)
    {
        var oldRelDir = NamespaceToRelativeDir(oldNamespace, rootNamespace);
        var newRelDir = NamespaceToRelativeDir(targetNamespace, rootNamespace);

        foreach (var filePath in filePaths)
        {
            var fileRelPath = Path.GetRelativePath(projectDir, filePath);

            if (!fileRelPath.StartsWith(oldRelDir, StringComparison.OrdinalIgnoreCase))
            {
                RtConsole.WriteLine(RtOutputContext.Result, $"WARNING: '{fileRelPath}' does not start with '{oldRelDir}' -- file not moved.");
                continue;
            }

            var remainder = fileRelPath[oldRelDir.Length..];
            var newRelPath = newRelDir + remainder;
            var newFilePath = Path.Combine(projectDir, newRelPath);

            if (dryRun)
            {
                RtConsole.WriteLine(RtOutputContext.Result, $"[dry-run] Would move: {filePath} -> {newFilePath}");
                continue;
            }

            if (File.Exists(newFilePath))
            {
                RtConsole.WriteLine(RtOutputContext.Result, $"WARNING: Target already exists: {newFilePath} -- file not moved.");
                continue;
            }

            var newDir = Path.GetDirectoryName(newFilePath)!;
            if (!Directory.Exists(newDir))
                Directory.CreateDirectory(newDir);

            File.Move(filePath, newFilePath);
            RtConsole.WriteLine(RtOutputContext.Result, $"Moved: {filePath} -> {newFilePath}");
        }
    }

    internal static string NamespaceToRelativeDir(string ns, string rootNamespace)
    {
        if (ns == rootNamespace)
            return "";

        if (ns.StartsWithOrdinal(rootNamespace + "."))
            return ns[(rootNamespace.Length + 1)..].Replace('.', Path.DirectorySeparatorChar);

        return ns.Replace('.', Path.DirectorySeparatorChar);
    }

}
