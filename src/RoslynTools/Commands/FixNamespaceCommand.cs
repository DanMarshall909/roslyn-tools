using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class FixNamespaceCommand : IRtCommand
{
    private FixNamespaceCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "fix-namespace",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["fns"],
        WhenToUse: "Correcting a file's namespace declaration to match its folder path after moving files, or when a file was created in the wrong location. Also updates any using directives in other files that imported the old namespace.",
        Examples:
        [
            "rt fix-namespace --file src/Services/BookingService.cs --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt fns --file src/RoslynTools/Commands/RenameCommand.cs --dry-run",
            "rt fix-namespace --file src/Domain/Order.cs --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
    {
        var fileOpt = new Option<string>("--file", "Path to the .cs file to fix") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("fix-namespace", "Align a file's namespace declaration to its folder path");
        cmd.AddAlias("fns");
        cmd.AddAll(fileOpt, solutionOpt, dryRunOpt);

        cmd.SetHandler(async (file, solution, dryRun) =>
        {
            await ExecuteAsync(file, solution, dryRun);
        }, fileOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string filePath, string? solutionPath, bool dryRun)
    {
        filePath = Path.GetFullPath(filePath);

        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = solution;

            // Find the document matching the given file path
            var targetDoc = solution.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(
                    d.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"File '{filePath}' not found in solution.");

            var project = solution.GetProject(targetDoc.Project.Id)
                ?? throw new InvalidOperationException("Cannot resolve project for document.");

            // Determine the project root directory (directory containing the .csproj)
            var projectFilePath = project.FilePath
                ?? throw new InvalidOperationException("Project has no file path.");
            var projectDir = Path.GetDirectoryName(projectFilePath)!;

            // Compute the expected namespace
            var expectedNamespace = ComputeExpectedNamespace(filePath, projectDir, project.AssemblyName);

            // Read the current namespace from the file
            var syntaxRoot = await targetDoc.GetSyntaxRootAsync()
                ?? throw new InvalidOperationException("Cannot get syntax root.");

            var currentNamespace = GetDeclaredNamespace(syntaxRoot);
            if (currentNamespace == null)
            {
                Console.Error.WriteLine($"No namespace declaration found in '{filePath}'. Nothing to fix.");
                return;
            }

            if (currentNamespace == expectedNamespace)
            {
                Console.WriteLine($"Namespace already correct: {expectedNamespace}");
                return;
            }

            Console.Error.WriteLine(
                $"Fixing namespace in '{Path.GetFileName(filePath)}': " +
                $"'{currentNamespace}' -> '{expectedNamespace}'");

            // Rewrite the namespace declaration in the target file
            var nsRewriter = new NamespaceDeclarationRenamer(currentNamespace, expectedNamespace);
            var newRoot = nsRewriter.Visit(syntaxRoot);
            solution = solution.WithDocumentSyntaxRoot(targetDoc.Id, newRoot);

            // Rewrite all files that import the old namespace via a using directive
            foreach (var proj in solution.Projects)
            {
                foreach (var doc in proj.Documents)
                {
                    if (doc.Id == targetDoc.Id) continue;
                    if (doc.FilePath == null) continue;

                    var root = await doc.GetSyntaxRootAsync();
                    if (root == null) continue;

                    var usingRewriter = new UsingDirectiveRenamer(currentNamespace, expectedNamespace);
                    var updatedRoot = usingRewriter.Visit(root);

                    if (usingRewriter.Changed)
                        solution = solution.WithDocumentSyntaxRoot(doc.Id, updatedRoot);
                }
            }

            await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
        }
    }

    // ──────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────

    private static string ComputeExpectedNamespace(string filePath, string projectDir, string assemblyName)
    {
        // Get the directory containing the file, relative to the project root
        var fileDir = Path.GetDirectoryName(filePath)!;
        var relativeDir = Path.GetRelativePath(projectDir, fileDir);

        // If the file is directly in the project root, namespace = assemblyName
        if (relativeDir == "." || relativeDir == string.Empty)
            return assemblyName;

        // Convert path separators to dots
        var subNamespace = relativeDir
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace('/', '.')
            .Trim('.');

        return assemblyName + "." + subNamespace;
    }

    private static string? GetDeclaredNamespace(SyntaxNode root)
    {
        var fileScoped = root.DescendantNodes()
            .OfType<FileScopedNamespaceDeclarationSyntax>()
            .FirstOrDefault();
        if (fileScoped != null)
            return fileScoped.Name.ToString().Trim();

        var blockScoped = root.DescendantNodes()
            .OfType<NamespaceDeclarationSyntax>()
            .FirstOrDefault();
        return blockScoped?.Name.ToString().Trim();
    }

    // ──────────────────────────────────────────────────────────────
    //  Syntax rewriters
    // ──────────────────────────────────────────────────────────────

    private sealed class NamespaceDeclarationRenamer : CSharpSyntaxRewriter
    {
        private readonly string _oldNs;
        private readonly string _newNs;

        public NamespaceDeclarationRenamer(string oldNs, string newNs)
        {
            _oldNs = oldNs;
            _newNs = newNs;
        }

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node)
        {
            if (node.Name.ToString().Trim() == _oldNs)
                return node.WithName(SyntaxFactory.ParseName(" " + _newNs).WithTriviaFrom(node.Name));
            return base.VisitFileScopedNamespaceDeclaration(node);
        }

        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node)
        {
            if (node.Name.ToString().Trim() == _oldNs)
                return node.WithName(SyntaxFactory.ParseName(" " + _newNs).WithTriviaFrom(node.Name));
            return base.VisitNamespaceDeclaration(node);
        }
    }

    private sealed class UsingDirectiveRenamer : CSharpSyntaxRewriter
    {
        private readonly string _oldNs;
        private readonly string _newNs;

        public bool Changed { get; private set; }

        public UsingDirectiveRenamer(string oldNs, string newNs)
        {
            _oldNs = oldNs;
            _newNs = newNs;
        }

        public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node)
        {
            if (node.Name?.ToString().Trim() == _oldNs)
            {
                Changed = true;
                return node.WithName(SyntaxFactory.ParseName(" " + _newNs).WithTriviaFrom(node.Name));
            }

            return base.VisitUsingDirective(node);
        }
    }
}
