using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class MoveCommand : IRtCommand
{
    private MoveCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "move",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: null,
        WhenToUse: "Moving a type to a different namespace — or batch-moving all types in a namespace — while updating the declaration and all reference sites (using directives and fully-qualified names). Use --move-file to also relocate the source file(s) to match the new namespace folder structure.",
        Examples:
        [
            "rt move --type ProjectX.Core.Services.BookingService --to-namespace ProjectX.Domain.Services --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt move --namespace ProjectX.Core.Models --to-namespace ProjectX.Domain.Entities --move-file --dry-run --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt move --type RoslynTools.Commands.RenameCommand --to-namespace RoslynTools.Commands.Rename --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var solutionOpt = new Option<string?>("--solution", "Path to the .sln/.slnx file");
        var typeOpt = new Option<string?>("--type", "Fully-qualified type name (e.g. ProjectX.Core.Services.BookingService)");
        var nsFromOpt = new Option<string?>("--namespace", "Source namespace — moves ALL types in this namespace (batch mode)");
        var nsToOpt = new Option<string>("--to-namespace", "Target namespace (e.g. ProjectX.Core.Domain.Services)") { IsRequired = true };
        var dryRunOpt = new Option<bool>("--dry-run", "Preview changes without writing to disk");
        var moveFileOpt = new Option<bool>("--move-file", "Move source file(s) to match the target namespace folder structure");

        var cmd = new Command("move", "Move a type to a different namespace, updating the declaration and all references")
        {
            solutionOpt, typeOpt, nsFromOpt, nsToOpt, dryRunOpt, moveFileOpt
        };

        cmd.SetHandler(async (solution, type, nsFrom, nsTo, dryRun, moveFile) =>
        {
            if (type == null && nsFrom == null)
            {
                Console.Error.WriteLine("Error: either --type or --namespace is required.");
                return;
            }
            if (type != null && nsFrom != null)
            {
                Console.Error.WriteLine("Error: --type and --namespace are mutually exclusive.");
                return;
            }

            var (workspace, sln) = await WorkspaceLoader.LoadAsync(solution);
            using (workspace)
            {
                if (nsFrom != null)
                    await ExecuteNamespaceMoveAsync(workspace, sln, nsFrom, nsTo, moveFile, dryRun);
                else
                    await ExecuteSingleTypeMoveAsync(workspace, sln, type!, nsTo, moveFile, dryRun);
            }
        },
        solutionOpt, typeOpt, nsFromOpt, nsToOpt, dryRunOpt, moveFileOpt);

        return cmd;
    }

    // ──────────────────────────────────────────────────────────────
    //  Batch mode: move every type in a namespace using a syntax rewriter
    // ──────────────────────────────────────────────────────────────

    private static async Task ExecuteNamespaceMoveAsync(
        Workspace workspace,
        Solution solution,
        string sourceNamespace,
        string targetNamespace,
        bool moveFile,
        bool dryRun)
    {
        var originalSolution = workspace.CurrentSolution;
        var declarationFiles = new List<string>();
        string? projectDir = null;
        string? rootNamespace = null;
        int changedFiles = 0;

        foreach (var project in solution.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath == null) continue;

                var root = await doc.GetSyntaxRootAsync();
                if (root == null) continue;

                var rewriter = new NamespaceMoveRewriter(sourceNamespace, targetNamespace);
                var newRoot = rewriter.Visit(root);

                if (rewriter.Changed)
                {
                    solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
                    changedFiles++;

                    if (rewriter.IsDeclarationFile)
                    {
                        declarationFiles.Add(doc.FilePath);
                        if (projectDir == null)
                        {
                            projectDir = Path.GetDirectoryName(project.FilePath);
                            rootNamespace = project.DefaultNamespace;
                            if (string.IsNullOrEmpty(rootNamespace))
                                rootNamespace = Path.GetFileNameWithoutExtension(project.FilePath ?? "");
                        }
                    }
                }
            }
        }

        if (changedFiles == 0)
        {
            Console.WriteLine($"No types found in namespace '{sourceNamespace}'.");
            return;
        }

        Console.Error.WriteLine(
            $"Rewriting '{sourceNamespace}' -> '{targetNamespace}': " +
            $"{declarationFiles.Count} declaration file(s), {changedFiles} total file(s) changed.");

        await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);

        if (moveFile && projectDir != null && rootNamespace != null)
        {
            MoveSourceFiles(declarationFiles, projectDir, rootNamespace, sourceNamespace, targetNamespace, dryRun);
        }
        else if (!moveFile && declarationFiles.Count > 0)
        {
            Console.WriteLine($"\nNOTE: File paths unchanged. Use --move-file to relocate declaration files.");
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Single-type mode: original SymbolFinder-based approach
    // ──────────────────────────────────────────────────────────────

    private static async Task ExecuteSingleTypeMoveAsync(
        Workspace workspace,
        Solution sln,
        string fqn,
        string targetNamespace,
        bool moveFile,
        bool dryRun)
    {
        var sym = await SymbolLocator.FindTypeByFqnAsync(sln, fqn);
        var oldNamespace = sym.ContainingNamespace.ToDisplayString();

        Console.Error.WriteLine($"Moving '{sym.ToDisplayString()}' from '{oldNamespace}' to '{targetNamespace}'...");

        // Capture declaration file paths before changes (needed for --move-file)
        var declarationFiles = new List<string>();
        string? projectDir = null;
        string? rootNamespace = null;
        foreach (var syntaxRef in sym.DeclaringSyntaxReferences)
        {
            var doc = sln.GetDocument(syntaxRef.SyntaxTree);
            if (doc?.FilePath != null)
            {
                declarationFiles.Add(doc.FilePath);
                if (projectDir == null)
                {
                    projectDir = Path.GetDirectoryName(doc.Project.FilePath);
                    rootNamespace = doc.Project.DefaultNamespace;
                    if (string.IsNullOrEmpty(rootNamespace))
                        rootNamespace = Path.GetFileNameWithoutExtension(doc.Project.FilePath ?? "");
                }
            }
        }

        // Step 1: Rewrite the namespace in ALL declaration files (handles partial classes)
        sln = await RewriteAllDeclarationsAsync(sln, sym, targetNamespace);

        // Step 2: Re-fetch symbol by new FQN
        var newFqn = targetNamespace + "." + sym.Name;
        var updatedSym = await SymbolLocator.FindTypeByFqnAsync(sln, newFqn);

        // Step 3: Update reference sites (using directives and fully-qualified names)
        sln = await UpdateReferenceUsingsAsync(sln, updatedSym, oldNamespace, targetNamespace);

        await SolutionWriter.ApplyChangesAsync(workspace.CurrentSolution, sln, dryRun);

        if (moveFile && projectDir != null && rootNamespace != null)
        {
            MoveSourceFiles(declarationFiles, projectDir, rootNamespace, oldNamespace, targetNamespace, dryRun);
        }
        else if (!moveFile)
        {
            Console.WriteLine($"\nNOTE: File path unchanged. Consider moving the source file to match the new namespace.");
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Rewrite helpers (used by single-type mode)
    // ──────────────────────────────────────────────────────────────

    private static async Task<Solution> RewriteAllDeclarationsAsync(
        Solution solution,
        ISymbol symbol,
        string targetNamespace)
    {
        // Handle ALL declaring syntax references (supports partial classes)
        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var doc = solution.GetDocument(syntaxRef.SyntaxTree);
            if (doc == null) continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            var node = await syntaxRef.GetSyntaxAsync();
            var nsDecl = node.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

            if (nsDecl == null) continue;

            // Skip if already in the target namespace
            if (nsDecl.Name.ToString() == targetNamespace) continue;

            var newNameNode = SyntaxFactory.ParseName(targetNamespace)
                .WithTriviaFrom(nsDecl.Name);

            var newNsDecl = nsDecl.WithName(newNameNode);
            var newRoot = root.ReplaceNode(nsDecl, newNsDecl);

            solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
        }

        return solution;
    }

    private static async Task<Solution> UpdateReferenceUsingsAsync(
        Solution solution,
        ISymbol symbol,
        string oldNamespace,
        string targetNamespace)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
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

            bool changed = false;
            var currentRoot = tracker;

            // Rewrite fully-qualified references that include the old namespace
            foreach (var loc in group)
            {
                var originalNode = root.FindNode(loc.Location.SourceSpan);
                var trackedNode = currentRoot.GetCurrentNode(originalNode);
                if (trackedNode == null) continue;

                var qualifiedRoot = FindOutermostQualifiedName(trackedNode);
                if (qualifiedRoot != null)
                {
                    var oldFqText = qualifiedRoot.ToFullString().Trim();
                    var newFqText = oldFqText.Replace(oldNamespace + ".", targetNamespace + ".");
                    if (oldFqText != newFqText)
                    {
                        var newNode = SyntaxFactory.ParseName(newFqText)
                            .WithTriviaFrom(qualifiedRoot);
                        currentRoot = currentRoot.ReplaceNode(qualifiedRoot, newNode);
                        changed = true;
                    }
                }
            }

            // Add a using for the target namespace if not already present
            var compilationUnit = currentRoot as CompilationUnitSyntax;
            if (compilationUnit != null)
            {
                var existingUsings = compilationUnit.Usings
                    .Select(u => u.Name?.ToFullString().Trim())
                    .ToHashSet(StringComparer.Ordinal);

                if (!existingUsings.Contains(targetNamespace))
                {
                    var newUsing = SyntaxFactory.UsingDirective(
                        SyntaxFactory.ParseName(" " + targetNamespace))
                        .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed);

                    compilationUnit = compilationUnit.AddUsings(newUsing);
                    currentRoot = compilationUnit;
                    changed = true;
                }
            }

            if (changed)
                solution = solution.WithDocumentSyntaxRoot(doc.Id, currentRoot);
        }

        return solution;
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

    private static void MoveSourceFiles(
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
                Console.WriteLine($"WARNING: '{fileRelPath}' does not start with '{oldRelDir}' -- file not moved.");
                continue;
            }

            var remainder = fileRelPath[oldRelDir.Length..];
            var newRelPath = newRelDir + remainder;
            var newFilePath = Path.Combine(projectDir, newRelPath);

            if (dryRun)
            {
                Console.WriteLine($"[dry-run] Would move: {filePath} -> {newFilePath}");
                continue;
            }

            if (File.Exists(newFilePath))
            {
                Console.WriteLine($"WARNING: Target already exists: {newFilePath} -- file not moved.");
                continue;
            }

            var newDir = Path.GetDirectoryName(newFilePath)!;
            if (!Directory.Exists(newDir))
                Directory.CreateDirectory(newDir);

            File.Move(filePath, newFilePath);
            Console.WriteLine($"Moved: {filePath} -> {newFilePath}");
        }
    }

    private static string NamespaceToRelativeDir(string ns, string rootNamespace)
    {
        if (ns == rootNamespace)
            return "";

        if (ns.StartsWith(rootNamespace + "."))
            return ns[(rootNamespace.Length + 1)..].Replace('.', Path.DirectorySeparatorChar);

        return ns.Replace('.', Path.DirectorySeparatorChar);
    }

    // ──────────────────────────────────────────────────────────────
    //  Syntax rewriter for batch namespace moves
    // ──────────────────────────────────────────────────────────────

    private class NamespaceMoveRewriter : CSharpSyntaxRewriter
    {
        private readonly string _oldNs;
        private readonly string _newNs;

        public bool Changed { get; private set; }
        public bool IsDeclarationFile { get; private set; }

        public NamespaceMoveRewriter(string oldNs, string newNs)
        {
            _oldNs = oldNs;
            _newNs = newNs;
        }

        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            // Check BEFORE visiting children (the name hasn't been rewritten yet)
            if (node.Name.ToString() == _oldNs)
                IsDeclarationFile = true;

            return base.VisitNamespaceDeclaration(node);
        }

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            if (node.Name.ToString() == _oldNs)
                IsDeclarationFile = true;

            return base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node)
        {
            // Visit children first so inner names are rewritten bottom-up
            var visited = (QualifiedNameSyntax)base.VisitQualifiedName(node)!;

            var text = visited.ToString();

            if (text == _oldNs)
            {
                Changed = true;
                return SyntaxFactory.ParseName(_newNs).WithTriviaFrom(visited);
            }

            if (text.StartsWith(_oldNs + "."))
            {
                Changed = true;
                var newText = _newNs + text[_oldNs.Length..];
                return SyntaxFactory.ParseName(newText).WithTriviaFrom(visited);
            }

            return visited;
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            // Handle FQ references in expression context (e.g. new TourStore.Models.User { })
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;

            var text = visited.ToString();

            if (text == _oldNs)
            {
                Changed = true;
                return SyntaxFactory.ParseExpression(_newNs).WithTriviaFrom(visited);
            }

            if (text.StartsWith(_oldNs + "."))
            {
                Changed = true;
                var newText = _newNs + text[_oldNs.Length..];
                return SyntaxFactory.ParseExpression(newText).WithTriviaFrom(visited);
            }

            return visited;
        }
    }
}
