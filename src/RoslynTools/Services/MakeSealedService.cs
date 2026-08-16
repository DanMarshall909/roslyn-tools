using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides services for making classes sealed
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class MakeSealedService : IRtService
{
    private readonly IDocumentLocatorService _documents;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public MakeSealedService()
        : this(new SolutionChangeWriter())
    {
    }

    public MakeSealedService(ISolutionChangeWriter solutionChangeWriter,
        IDocumentLocatorService? documents = null)
    {
        _documents = documents ?? new DocumentLocatorService();
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Runs asynchronous sealed class transformation operations
    /// </summary>
    internal async Task RunAsync(Solution sln, string typeName, bool dryRun)
    {
        await SealAsync(sln, typeName, dryRun);
    }

    /// <summary>
    ///     Seals a class and returns a transport-neutral description of the outcome.
    ///     Rendering of no-op explanations belongs to the calling adapter.
    /// </summary>
    internal async Task<MakeSealedResult> SealAsync(Solution sln, string typeName, bool dryRun)
    {
        var prepared = await PrepareAsync(sln, typeName);
        if (prepared.Changed)
            await _solutionChangeWriter.ApplyChangesAsync(sln, prepared.Solution, dryRun);

        return prepared.Result;
    }

    /// <summary>
    ///     Calculates a make-sealed mutation without applying it. The daemon operation owns
    ///     the application boundary so every transport receives the same mutation envelope.
    /// </summary>
    internal async Task<MakeSealedPreparation> PrepareAsync(Solution sln, string typeName)
    {
        var (classSymbol, syntaxRef, docId) = await FindClassAndDocumentAsync(sln, typeName);

        var validation = await ValidateCanSealAsync(typeName, classSymbol, sln);
        if (validation is not null)
            return new MakeSealedPreparation(sln, validation, []);

        var changedSolution = await AddSealedModifierAsync(sln, syntaxRef, docId);
        var changedFilePaths = changedSolution.GetChanges(sln)
            .GetProjectChanges()
            .SelectMany(project => project.GetChangedDocuments())
            .Select(documentId => changedSolution.GetDocument(documentId))
            .Where(document => document is not null)
            .Select(document => document!.FilePath ?? document.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new MakeSealedPreparation(
            changedSolution,
            new MakeSealedResult(typeName, MakeSealedOutcome.Sealed, null),
            changedFilePaths);
    }

    private async Task<(INamedTypeSymbol classSymbol, SyntaxReference syntaxRef, DocumentId docId)>
        FindClassAndDocumentAsync(Solution sln, string typeName)
    {
        var classSymbol = await FindClassSymbolAsync(sln, typeName)
                          ?? throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

        var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                        ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

        var docId = _documents.FindDocumentId(sln, classSymbol)
                    ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

        return (classSymbol, syntaxRef, docId);
    }

    private async Task<MakeSealedResult?> ValidateCanSealAsync(
        string typeName, INamedTypeSymbol classSymbol, Solution sln)
    {
        if (classSymbol.IsSealed)
            return new MakeSealedResult(
                typeName,
                MakeSealedOutcome.AlreadySealed,
                $"Class {typeName} is already sealed.");

        if (classSymbol.IsAbstract)
            return new MakeSealedResult(
                typeName,
                MakeSealedOutcome.Abstract,
                "Cannot seal an abstract class.");

        var derivedTypes = await SymbolFinder.FindDerivedClassesAsync(
            classSymbol, sln, cancellationToken: CancellationToken.None);

        var derivedList = derivedTypes.ToList();
        if (derivedList.Count > 0)
        {
            var names = string.Join(", ", derivedList.Select(d => d.Name));
            throw new InvalidOperationException($"Cannot seal {typeName}: derived types exist: {names}");
        }

        return null;
    }

    private async Task<Solution> AddSealedModifierAsync(
        Solution sln, SyntaxReference syntaxRef, DocumentId docId)
    {
        var classSyntax = (ClassDeclarationSyntax)await syntaxRef.GetSyntaxAsync();

        var doc = sln.GetDocument(docId)!;
        var root = (await doc.GetSyntaxRootAsync())!;
        var oldNode = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax
                      ?? throw new InvalidOperationException("Cannot locate class syntax node in document.");

        var newClassSyntax = classSyntax.WithModifiers(BuildSealedModifiers(classSyntax.Modifiers));
        root = root.ReplaceNode(oldNode, newClassSyntax);

        return sln.WithDocumentSyntaxRoot(docId, root);
    }

    private async Task<INamedTypeSymbol?> FindClassSymbolAsync(Solution sln, string typeName)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
                if (symbol is INamedTypeSymbol namedType &&
                    namedType.TypeKind == TypeKind.Class &&
                    namedType.Name == typeName)
                    return namedType;
        }

        return null;
    }

    private SyntaxTokenList BuildSealedModifiers(SyntaxTokenList existingModifiers)
    {
        var sealedToken = SyntaxFactory.Token(SyntaxKind.SealedKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        // Insert sealed after access/static/partial modifiers, before any others
        var insertIndex = existingModifiers.Count;
        for (var i = 0; i < existingModifiers.Count; i++)
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
