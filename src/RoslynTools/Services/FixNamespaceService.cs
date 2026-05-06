using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class FixNamespaceService
{
    internal async Task ExecuteAsync(string filePath, string? solutionPath, bool dryRun)
    {
        filePath = Path.GetFullPath(filePath);

        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            await RunAsync(solution, filePath, dryRun);
        }
    }

    internal async Task RunAsync(Solution solution, string filePath, bool dryRun)
    {
        var originalSolution = solution;

        var targetDoc = solution.Projects
            .SelectMany(p => p.Documents)
            .FirstOrDefault(d => string.Equals(
                d.FilePath, filePath, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"File '{filePath}' not found in solution.");

        var project = solution.GetProject(targetDoc.Project.Id)
            ?? throw new InvalidOperationException("Cannot resolve project for document.");

        var projectFilePath = project.FilePath
            ?? throw new InvalidOperationException("Project has no file path.");
        var projectDir = Path.GetDirectoryName(projectFilePath)!;

        var expectedNamespace = ComputeExpectedNamespace(filePath, projectDir, project.AssemblyName);

        var syntaxRoot = await targetDoc.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("Cannot get syntax root.");

        var currentNamespace = GetDeclaredNamespace(syntaxRoot);
        if (currentNamespace == null)
        {
            RtLog.Info("No namespace declaration found in '{FilePath}'. Nothing to fix.", filePath);
            return;
        }

        if (currentNamespace == expectedNamespace)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"Namespace already correct: {expectedNamespace}");
            return;
        }

        RtLog.Info(
            "Fixing namespace in '{FileName}': '{CurrentNamespace}' -> '{ExpectedNamespace}'",
            Path.GetFileName(filePath),
            currentNamespace,
            expectedNamespace);

        var nsRewriter = new NamespaceDeclarationRenamer(currentNamespace, expectedNamespace);
        var newRoot = nsRewriter.Visit(syntaxRoot);
        solution = solution.WithDocumentSyntaxRoot(targetDoc.Id, newRoot);

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

    internal string ComputeExpectedNamespace(string filePath, string projectDir, string assemblyName)
    {
        var fileDir = Path.GetDirectoryName(filePath)!;
        var relativeDir = Path.GetRelativePath(projectDir, fileDir);

        if (relativeDir == "." || relativeDir == string.Empty)
            return assemblyName;

        var subNamespace = relativeDir
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace('/', '.')
            .Trim('.');

        return assemblyName + "." + subNamespace;
    }

    internal string? GetDeclaredNamespace(SyntaxNode root)
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

    internal sealed class NamespaceDeclarationRenamer : CSharpSyntaxRewriter
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

    internal sealed class UsingDirectiveRenamer : CSharpSyntaxRewriter
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
