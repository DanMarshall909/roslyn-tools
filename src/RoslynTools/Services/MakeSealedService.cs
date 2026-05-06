using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class MakeSealedService : IRtService
{
    private readonly ILogger<MakeSealedService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public MakeSealedService()
        : this(new RtConsoleLogger<MakeSealedService>(), new SolutionChangeWriter())
    {
    }

    public MakeSealedService(ILogger<MakeSealedService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }
    internal async Task RunAsync(Solution sln, string typeName, bool dryRun)
    {
        var originalSolution = sln;

        var (classSymbol, syntaxRef, docId) = await FindClassAndDocumentAsync(sln, typeName);

        if (!await CheckDerivedTypesAsync(typeName, classSymbol, sln))
            return;

        sln = await AddSealedModifierAsync(sln, syntaxRef, docId);

        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    private async Task<(INamedTypeSymbol classSymbol, SyntaxReference syntaxRef, DocumentId docId)>
        FindClassAndDocumentAsync(Solution sln, string typeName)
    {
        var classSymbol = await FindClassSymbolAsync(sln, typeName)
            ?? throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

        var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
            ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

        var docId = SymbolDocumentLocator.FindDocumentId(sln, classSymbol)
            ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

        return (classSymbol, syntaxRef, docId);
    }

    private async Task<bool> CheckDerivedTypesAsync(
        string typeName, INamedTypeSymbol classSymbol, Solution sln)
    {
        if (classSymbol.IsSealed)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"Class {typeName} is already sealed.");
            return false;
        }

        if (classSymbol.IsAbstract)
        {
            _logger.RtError("Cannot seal an abstract class.");
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

    private SyntaxTokenList BuildSealedModifiers(SyntaxTokenList existingModifiers)
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
