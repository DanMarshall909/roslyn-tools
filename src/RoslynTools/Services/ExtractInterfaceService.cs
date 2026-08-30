using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods for extracting and building interface-related information.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ExtractInterfaceService : IRtService
{
    private readonly ISymbolResolutionService _symbols;

    public ExtractInterfaceService()
        : this(new SymbolResolutionService())
    {
    }

    public ExtractInterfaceService(ISymbolResolutionService symbols)
    {
        _symbols = symbols;
    }

    /// <summary>
    /// Calculates an interface extraction without writing or rendering a result.
    /// </summary>
    internal async Task<Result<Preparation, RtError>> PrepareAsync(
        Solution sln,
        string typeName,
        string? outputDir,
        string? ns,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var resolved = await _symbols.TryFindSymbolAsync(sln, typeName, "type");
        INamedTypeSymbol? classSymbol = null;
        RtError? resolutionError = null;
        resolved.Match(value => classSymbol = value as INamedTypeSymbol, error => resolutionError = error);
        if (resolutionError is not null)
            return Result<Preparation, RtError>.Err(resolutionError);

        if (classSymbol is null)
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"'{typeName}' is not a named type.",
                new Dictionary<string, string> { ["type"] = typeName }));

        if (classSymbol.TypeKind != TypeKind.Class)
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"'{typeName}' must be a class, but is {classSymbol.TypeKind}.",
                new Dictionary<string, string> { ["type"] = typeName }));

        var interfaceName = "I" + classSymbol.Name;
        // Stryker disable once String : ContainingNamespace is non-null for every source type, so the '?? string.Empty' fallback is unreachable.
        var targetNamespace = ns ?? classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        var classFilePath = classSymbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (string.IsNullOrEmpty(classFilePath))
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Cannot determine source file for '{typeName}'.",
                new Dictionary<string, string> { ["type"] = typeName }));

        var classDir = outputDir ?? Path.GetDirectoryName(classFilePath)!;
        var interfaceFileName = interfaceName + ".cs";
        var interfaceFilePath = Path.Combine(classDir, interfaceFileName);

        // Members visible at the interface's accessibility level: a public source class
        // exposes only public members; an internal source class also exposes its internal members.
        var includeInternal = classSymbol.DeclaredAccessibility != Accessibility.Public;
        bool IsExposed(Accessibility a) =>
            a == Accessibility.Public || (includeInternal && a == Accessibility.Internal);

        var exposedMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                IsExposed(m.DeclaredAccessibility) &&
                !m.IsStatic &&
                m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var exposedProperties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                IsExposed(p.DeclaredAccessibility) &&
                !p.IsStatic)
            .ToList();

        var interfaceText = InterfaceTextBuilder.BuildInterfaceText(
            targetNamespace, interfaceName,
            ((IEnumerable<ISymbol>)exposedMethods).Concat(exposedProperties),
            classSymbol.DeclaredAccessibility);

        // Stryker disable once Linq, NullCoalescing, String : classFilePath was derived from this same classSymbol's in-source location, so the document is always present (FirstOrDefault never null, '?? throw' and its message unreachable).
        var classDocument = sln.Projects
                                .SelectMany(p => p.Documents)
                                .FirstOrDefault(d =>
                                    string.Equals(d.FilePath, classFilePath, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException(
                                $"Document for '{classFilePath}' not found in solution.");

        var updatedClassSolution = await AddInterfaceToClassAsync(
            sln,
            classDocument,
            classSymbol,
            interfaceName,
            cancellationToken);

        // Stryker disable once NullCoalescing, String : the owning project always contains the just-found classDocument, so '?? throw' and its message are unreachable. NOTE: this statement also carries a KILLABLE Any()->All() Linq mutant (covered by succeeds_when_project_has_multiple_documents), so we deliberately do NOT disable Linq here — the remaining FirstOrDefault()->First() Linq mutant is a documented equivalent (single owning project => First()==FirstOrDefault()) that cannot be disabled without masking the killable Any()->All().
        var owningProject = updatedClassSolution.Projects
                                .FirstOrDefault(p => p.Documents.Any(d =>
                                    string.Equals(d.FilePath, classFilePath, StringComparison.OrdinalIgnoreCase)))
                            ?? throw new InvalidOperationException("Cannot find project containing the class.");

        var interfaceSourceText = SourceText.From(interfaceText, Encoding.UTF8);
        var finalSolution = updatedClassSolution.AddDocument(
            DocumentId.CreateNewId(owningProject.Id),
            interfaceFileName,
            interfaceSourceText,
            filePath: interfaceFilePath);

        return Result<Preparation, RtError>.Ok(new Preparation(
            finalSolution,
            classSymbol.Name,
            interfaceName,
            targetNamespace,
            interfaceFilePath,
            exposedMethods.Count + exposedProperties.Count));
    }

    /// <summary>
    /// Asynchronously adds an interface to a class in a solution.
    /// </summary>
    internal async Task<Solution> AddInterfaceToClassAsync(
        Solution solution,
        Document classDocument,
        INamedTypeSymbol classSymbol,
        string interfaceName,
        CancellationToken cancellationToken = default)
    {
        // Stryker disable once NullCoalescing, String : a parsed C# document always has a syntax root, so '?? throw' and its message are unreachable.
        var root = await classDocument.GetSyntaxRootAsync(cancellationToken)
                   ?? throw new InvalidOperationException("Cannot get syntax root.");

        // Stryker disable once Linq, NullCoalescing, String : the class declaration always exists in its own resolved document, so FirstOrDefault is never null and '?? throw' / its message are unreachable. (The '==' Equality mutant on the identifier match IS killable and is left enabled.)
        var classDecl = root.DescendantNodes()
                            .OfType<ClassDeclarationSyntax>()
                            .FirstOrDefault(c => c.Identifier.Text == classSymbol.Name)
                        ?? throw new InvalidOperationException(
                            $"Class declaration '{classSymbol.Name}' not found in syntax tree.");

        var interfaceTypeName = SyntaxFactory.SimpleBaseType(
            SyntaxFactory.IdentifierName(interfaceName));

        ClassDeclarationSyntax updatedClass;

        if (classDecl.BaseList is null)
        {
            var colonToken = SyntaxFactory.Token(
                SyntaxFactory.TriviaList(SyntaxFactory.Space),
                SyntaxKind.ColonToken,
                SyntaxFactory.TriviaList(SyntaxFactory.Space));

            var baseList = SyntaxFactory.BaseList(
                colonToken,
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceTypeName));

            var identifierToken = classDecl.Identifier
                .WithTrailingTrivia(SyntaxFactory.TriviaList());
            var classWithCleanId = classDecl.WithIdentifier(identifierToken);
            updatedClass = classWithCleanId.WithBaseList(baseList);
        }
        else
        {
            // Move the existing last base type's trailing trivia (e.g. the newline before '{')
            // onto the appended interface so the brace stays on its own line, and give the new
            // type a leading space so it reads ", IFoo" rather than the malformed ",IFoo".
            var existingTypes = classDecl.BaseList.Types;
            var lastType = existingTypes[^1];
            var trailing = lastType.GetTrailingTrivia();

            var appended = interfaceTypeName
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(trailing);

            var newTypes = existingTypes
                .Replace(lastType, lastType.WithTrailingTrivia(SyntaxFactory.TriviaList()))
                .Add(appended);
            updatedClass = classDecl.WithBaseList(classDecl.BaseList.WithTypes(newTypes));
        }

        var newRoot = root.ReplaceNode(classDecl, updatedClass);
        return solution.WithDocumentSyntaxRoot(classDocument.Id, newRoot);
    }

    internal sealed record Preparation(
        Solution Solution,
        string TypeName,
        string InterfaceName,
        string Namespace,
        string InterfaceFilePath,
        int MemberCount);
}
