using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class MoveService : IRtService
{
    private readonly ILogger<MoveService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly ISymbolResolutionService _symbols;

    public MoveService()
        : this(new RtConsoleLogger<MoveService>(), new SolutionChangeWriter())
    {
    }

    public MoveService(ILogger<MoveService> logger, ISolutionChangeWriter solutionChangeWriter,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }
    // ──────────────────────────────────────────────────────────────
    //  Entry points
    // ──────────────────────────────────────────────────────────────

    internal async Task ExecuteNamespaceMoveAsync(
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

        _logger.RtInfo(
            "Rewriting '{SourceNamespace}' -> '{TargetNamespace}': {DeclarationFileCount} declaration file(s), {ChangedFileCount} total file(s) changed.",
            sourceNamespace,
            targetNamespace,
            declarationFiles.Count,
            changedFiles);

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, updated, dryRun);

        ApplyFileMoveOrNote(declarationFiles, projectDir, rootNamespace, sourceNamespace, targetNamespace, moveFile,
            dryRun);
    }

    internal async Task ExecuteCrossProjectMoveAsync(
        Workspace workspace,
        Solution solution,
        string fqn,
        string targetProjectName,
        string? targetNamespace,
        bool dryRun)
    {
        var targetProject = solution.Projects.FirstOrDefault(p =>
            string.Equals(p.Name, targetProjectName, StringComparison.OrdinalIgnoreCase));

        if (targetProject == null)
            throw new InvalidOperationException(
                $"Project '{targetProjectName}' not found in the solution. " +
                $"Available projects: {string.Join(", ", solution.Projects.Select(p => p.Name))}");

        var sym = await _symbols.FindTypeByFqnAsync(solution, fqn);
        var (declarationFiles, sourceProjectDir, _) = CollectDeclarationPaths(solution, sym);

        // Same-project no-op
        var sourceProject = solution.GetDocument(sym.DeclaringSyntaxReferences.First().SyntaxTree)?.Project;
        if (sourceProject != null &&
            string.Equals(sourceProject.Name, targetProjectName, StringComparison.OrdinalIgnoreCase))
        {
            RtConsole.WriteLine(RtOutputContext.Result, "nothing to move");
            return;
        }

        if (sourceProjectDir != null)
        {
            foreach (var filePath in declarationFiles)
            {
                if (!filePath.StartsWith(sourceProjectDir, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Source file for '{sym.Name}' is at '{filePath}', which is outside the source " +
                        $"project directory '{sourceProjectDir}'. Cannot determine destination path.");
            }
        }

        throw new NotImplementedException("Cross-project move body not yet implemented.");
    }

    internal async Task ExecuteSingleTypeMoveAsync(
        Workspace workspace,
        Solution sln,
        string fqn,
        string targetNamespace,
        bool moveFile,
        bool dryRun)
    {
        var sym = await _symbols.FindTypeByFqnAsync(sln, fqn);
        var oldNamespace = sym.ContainingNamespace.ToDisplayString();

        _logger.RtInfo("Moving '{TypeName}' from '{OldNamespace}' to '{TargetNamespace}'...", sym.ToDisplayString(),
            oldNamespace, targetNamespace);

        var (declarationFiles, projectDir, rootNamespace) = CollectDeclarationPaths(sln, sym);

        // Capture references BEFORE the rewrite. After RewriteAllDeclarationsAsync, consumer
        // references that resolved through `using <oldNamespace>;` become unbound, and
        // SymbolFinder against the post-rewrite compilation can no longer find them.
        // Co-located types in the same file move with the namespace declaration, so capture
        // references to all of them — not just the named target.
        var movedTypes = await CollectCoLocatedTypesAsync(sln, (INamedTypeSymbol)sym);
        var references = (await Task.WhenAll(movedTypes.Select(t => SymbolFinder.FindReferencesAsync(t, sln))))
            .SelectMany(r => r)
            .ToList();

        sln = await RewriteAllDeclarationsAsync(sln, sym, targetNamespace);

        sln = await ApplyReferenceUsingsAsync(sln, references, oldNamespace, targetNamespace);

        await _solutionChangeWriter.ApplyChangesAsync(workspace.CurrentSolution, sln, dryRun);

        if (moveFile && projectDir != null && rootNamespace != null)
            MoveSourceFiles(declarationFiles, projectDir, rootNamespace, oldNamespace, targetNamespace, dryRun);
        else if (!moveFile)
            RtConsole.WriteLine(RtOutputContext.Result,
                "\nNOTE: File path unchanged. Consider moving the source file to match the new namespace.");
    }

    // ──────────────────────────────────────────────────────────────
    //  Namespace-move helpers
    // ──────────────────────────────────────────────────────────────

    private async Task<(Solution Solution, List<string> DeclarationFiles, string? ProjectDir, string? RootNamespace, int
            ChangedFiles)>
        ScanSolutionForNamespaceAsync(Solution solution, string sourceNamespace, string targetNamespace)
    {
        var declarationFiles = new List<string>();
        string? projectDir = null;
        string? rootNamespace = null;
        var changedFiles = 0;

        foreach (var project in solution.Projects)
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

        return (solution, declarationFiles, projectDir, rootNamespace, changedFiles);
    }

    private async Task<DocumentRewriteResult> RewriteDocumentNamespaceAsync(
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

    private (string? ProjectDir, string? RootNamespace) ResolveProjectMeta(Project project)
    {
        var dir = Path.GetDirectoryName(project.FilePath);
        var ns = project.DefaultNamespace;
        if (string.IsNullOrEmpty(ns))
            ns = Path.GetFileNameWithoutExtension(project.FilePath ?? "");
        return (dir, ns);
    }

    private void ApplyFileMoveOrNote(
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
            RtConsole.WriteLine(RtOutputContext.Result,
                "\nNOTE: File paths unchanged. Use --move-file to relocate declaration files.");
    }

    // ──────────────────────────────────────────────────────────────
    //  Single-type helpers
    // ──────────────────────────────────────────────────────────────

    private async Task<List<INamedTypeSymbol>> CollectCoLocatedTypesAsync(
        Solution sln, INamedTypeSymbol primary)
    {
        var trees = primary.DeclaringSyntaxReferences
            .Select(r => r.SyntaxTree)
            .ToHashSet();

        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default) { primary };
        var result = new List<INamedTypeSymbol> { primary };

        foreach (var tree in trees)
        {
            var doc = sln.GetDocument(tree);
            if (doc == null) continue;

            var semanticModel = await doc.GetSemanticModelAsync();
            if (semanticModel == null) continue;

            var root = await tree.GetRootAsync();
            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
                if (semanticModel.GetDeclaredSymbol(typeDecl) is INamedTypeSymbol named && seen.Add(named))
                    result.Add(named);
        }

        return result;
    }

    private (List<string> Paths, string? ProjectDir, string? RootNamespace) CollectDeclarationPaths(
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

    private async Task<Solution> RewriteAllDeclarationsAsync(
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
                var _ = false;
                newRoot = EnsureTargetNamespaceUsing(newRoot, oldNamespace, ref _);
            }

            solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
        }

        return solution;
    }

    private async Task<bool> DocumentReferencesNamespaceAsync(
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

    private async Task<Solution> ApplyReferenceUsingsAsync(
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

    private (SyntaxNode Root, bool Changed) RewriteFullyQualifiedReferences(
        SyntaxNode root,
        SyntaxNode tracker,
        IGrouping<DocumentId, ReferenceLocation> group,
        string oldNamespace,
        string targetNamespace)
    {
        var changed = false;
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

    private SyntaxNode EnsureTargetNamespaceUsing(
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

    private SyntaxNode? FindOutermostQualifiedName(SyntaxNode node)
    {
        var current = node;
        while (current.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax)
            current = current.Parent;
        return current != node ? current : null;
    }

    // ──────────────────────────────────────────────────────────────
    //  File movement helpers
    // ──────────────────────────────────────────────────────────────

    internal void MoveSourceFiles(
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
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"WARNING: '{fileRelPath}' does not start with '{oldRelDir}' -- file not moved.");
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
                RtConsole.WriteLine(RtOutputContext.Result,
                    $"WARNING: Target already exists: {newFilePath} -- file not moved.");
                continue;
            }

            var newDir = Path.GetDirectoryName(newFilePath)!;
            if (!Directory.Exists(newDir))
                Directory.CreateDirectory(newDir);

            File.Move(filePath, newFilePath);
            RtConsole.WriteLine(RtOutputContext.Result, $"Moved: {filePath} -> {newFilePath}");
        }
    }

    internal string NamespaceToRelativeDir(string ns, string rootNamespace)
    {
        if (ns == rootNamespace)
            return "";

        if (ns.StartsWithOrdinal(rootNamespace + "."))
            return ns[(rootNamespace.Length + 1)..].Replace('.', Path.DirectorySeparatorChar);

        return ns.Replace('.', Path.DirectorySeparatorChar);
    }

    private record DocumentRewriteResult(
        bool Changed,
        Solution Solution,
        bool IsDeclarationFile,
        (string? ProjectDir, string? RootNamespace)? ProjectMeta);
}
