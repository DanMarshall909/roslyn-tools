using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class MakeSealedCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to seal") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("make-sealed", "Add the sealed modifier to a class that has no derived types");
        cmd.AddAlias("ms");
        cmd.Add(typeOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, solution, dryRun) =>
        {
            await ExecuteAsync(type, solution, dryRun);
        }, typeOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = sln;

            // Find the class symbol across all projects
            INamedTypeSymbol? classSymbol = null;
            DocumentId? docId = null;

            foreach (var project in sln.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
                {
                    if (symbol is INamedTypeSymbol namedType &&
                        namedType.TypeKind == TypeKind.Class &&
                        namedType.Name == typeName)
                    {
                        classSymbol = namedType;
                        break;
                    }
                }

                if (classSymbol != null) break;
            }

            if (classSymbol == null)
                throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

            // Already sealed — no-op
            if (classSymbol.IsSealed)
            {
                Console.WriteLine($"Class {typeName} is already sealed.");
                return;
            }

            // Abstract — cannot seal
            if (classSymbol.IsAbstract)
            {
                Console.Error.WriteLine($"Cannot seal an abstract class.");
                return;
            }

            // Check for derived types
            var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(
                classSymbol, sln, cancellationToken: CancellationToken.None);

            var derivedList = derivedTypes.ToList();
            if (derivedList.Count > 0)
            {
                var names = string.Join(", ", derivedList.Select(d => d.Name));
                throw new InvalidOperationException($"Cannot seal {typeName}: derived types exist: {names}");
            }

            // Locate the syntax reference
            var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
                throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

            var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

            // Find document ID
            var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
                throw new InvalidOperationException($"No source location found for '{typeName}'.");

            foreach (var project in sln.Projects)
            {
                foreach (var doc in project.Documents)
                {
                    if (doc.FilePath != null &&
                        string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        docId = doc.Id;
                        break;
                    }
                }
                if (docId != null) break;
            }

            if (docId == null)
                throw new InvalidOperationException("Cannot locate source document for the target class.");

            // Build new modifiers: insert sealed after access modifiers, before other modifiers
            var existingModifiers = classSyntax.Modifiers;
            var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space);

            // Find insertion index: after public/private/protected/internal/static/partial modifiers
            // that precede the 'class' keyword. We want sealed inserted before 'class',
            // after any access/static modifiers.
            int insertIndex = existingModifiers.Count; // default: append
            for (int i = 0; i < existingModifiers.Count; i++)
            {
                var kind = existingModifiers[i].Kind();
                if (kind != SyntaxKind.PublicKeyword &&
                    kind != SyntaxKind.PrivateKeyword &&
                    kind != SyntaxKind.ProtectedKeyword &&
                    kind != SyntaxKind.InternalKeyword &&
                    kind != SyntaxKind.StaticKeyword &&
                    kind != SyntaxKind.PartialKeyword)
                {
                    insertIndex = i;
                    break;
                }
            }

            var newModifiers = existingModifiers.Insert(insertIndex, sealedToken);
            var newClassSyntax = classSyntax.WithModifiers(newModifiers);

            // Replace the class node in the document root
            var doc2 = sln.GetDocument(docId)!;
            var root = (await doc2.GetSyntaxRootAsync())!;
            var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax;
            if (oldNode == null)
                throw new InvalidOperationException("Cannot locate class syntax node in document.");

            root = root.ReplaceNode(oldNode, newClassSyntax);
            sln = sln.WithDocumentSyntaxRoot(docId, root);

            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }
}
