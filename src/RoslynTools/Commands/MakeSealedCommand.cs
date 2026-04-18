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

            var classSymbol = await FindClassSymbolAsync(typeName, sln);
            if (classSymbol == null)
                throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

            if (!await ValidateClassCanBeSealedAsync(typeName, classSymbol, sln))
                return;

            var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

            var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

            var docId = await FindDocumentIdAsync(classSymbol, sln)
                ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

            var doc = sln.GetDocument(docId)!;
            var root = (await doc.GetSyntaxRootAsync())!;
            var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax
                ?? throw new InvalidOperationException("Cannot locate class syntax node in document.");

            var newClassSyntax = classSyntax.WithModifiers(BuildSealedModifiers(classSyntax.Modifiers));
            root = root.ReplaceNode(oldNode, newClassSyntax);
            sln = sln.WithDocumentSyntaxRoot(docId, root);

            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }

    private static async Task<INamedTypeSymbol?> FindClassSymbolAsync(string typeName, Solution sln)
    {
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
                    return namedType;
                }
            }
        }

        return null;
    }

    private static async Task<DocumentId?> FindDocumentIdAsync(INamedTypeSymbol classSymbol, Solution sln)
    {
        var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
        if (location == null) return null;

        foreach (var project in sln.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return doc.Id;
                }
            }
        }

        return null;
    }

    private static async Task<bool> ValidateClassCanBeSealedAsync(
        string typeName, INamedTypeSymbol classSymbol, Solution sln)
    {
        if (classSymbol.IsSealed)
        {
            Console.WriteLine($"Class {typeName} is already sealed.");
            return false;
        }

        if (classSymbol.IsAbstract)
        {
            Console.Error.WriteLine($"Cannot seal an abstract class.");
            return false;
        }

        var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(
            classSymbol, sln, cancellationToken: CancellationToken.None);

        var derivedList = derivedTypes.ToList();
        if (derivedList.Count > 0)
        {
            var names = string.Join(", ", derivedList.Select(d => d.Name));
            throw new InvalidOperationException($"Cannot seal {typeName}: derived types exist: {names}");
        }

        return true;
    }

    private static SyntaxTokenList BuildSealedModifiers(SyntaxTokenList existingModifiers)
    {
        var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Insert sealed after access/static/partial modifiers, before any others
        int insertIndex = existingModifiers.Count;
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

        return existingModifiers.Insert(insertIndex, sealedToken);
    }
}
