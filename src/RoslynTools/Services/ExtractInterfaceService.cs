using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods for extracting and building interface-related information.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ExtractInterfaceService : IRtService
{
    private readonly ILogger<ExtractInterfaceService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly ISymbolResolutionService _symbols;

    public ExtractInterfaceService()
        : this(new RtConsoleLogger<ExtractInterfaceService>(), new SolutionChangeWriter())
    {
    }

    public ExtractInterfaceService(ILogger<ExtractInterfaceService> logger, ISolutionChangeWriter solutionChangeWriter,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Runs an asynchronous interface extraction process with provided parameters.
    /// </summary>
    internal async Task RunAsync(
        Solution sln,
        string typeName,
        string? outputDir,
        string? ns,
        bool dryRun)
    {
        var originalSln = sln;

        var classSymbol = await _symbols.FindSymbolAsync(sln, typeName, "type") as INamedTypeSymbol
                          ?? throw new InvalidOperationException($"'{typeName}' is not a named type.");

        if (classSymbol.TypeKind != TypeKind.Class)
            throw new InvalidOperationException($"'{typeName}' must be a class, but is {classSymbol.TypeKind}.");

        var interfaceName = "I" + classSymbol.Name;
        var targetNamespace = ns ?? classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

        var classFilePath = classSymbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (string.IsNullOrEmpty(classFilePath))
            throw new InvalidOperationException($"Cannot determine source file for '{typeName}'.");

        var classDir = outputDir ?? Path.GetDirectoryName(classFilePath)!;
        var interfaceFileName = interfaceName + ".cs";
        var interfaceFilePath = Path.Combine(classDir, interfaceFileName);

        var publicMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m =>
                m.DeclaredAccessibility == Accessibility.Public &&
                !m.IsStatic &&
                m.MethodKind == MethodKind.Ordinary)
            .ToList();

        var publicProperties = classSymbol.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p =>
                p.DeclaredAccessibility == Accessibility.Public &&
                !p.IsStatic)
            .ToList();

        var interfaceText = InterfaceTextBuilder.BuildInterfaceText(
            targetNamespace, interfaceName, ((IEnumerable<ISymbol>)publicMethods).Concat(publicProperties));

        var classDocument = sln.Projects
                                .SelectMany(p => p.Documents)
                                .FirstOrDefault(d =>
                                    string.Equals(d.FilePath, classFilePath, StringComparison.OrdinalIgnoreCase))
                            ?? throw new InvalidOperationException(
                                $"Document for '{classFilePath}' not found in solution.");

        var updatedClassSolution = await AddInterfaceToClassAsync(sln, classDocument, classSymbol, interfaceName);

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

        _logger.RtInfo("Extracting interface '{InterfaceName}' from '{Name}'...", interfaceName, classSymbol.Name);
        await _solutionChangeWriter.ApplyChangesAsync(originalSln, finalSolution, dryRun);
    }

    /// <summary>
    /// Asynchronously adds an interface to a class in a solution.
    /// </summary>
    internal async Task<Solution> AddInterfaceToClassAsync(
        Solution solution,
        Document classDocument,
        INamedTypeSymbol classSymbol,
        string interfaceName)
    {
        var root = await classDocument.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("Cannot get syntax root.");

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
            var newTypes = classDecl.BaseList.Types.Add(interfaceTypeName);
            updatedClass = classDecl.WithBaseList(classDecl.BaseList.WithTypes(newTypes));
        }

        var newRoot = root.ReplaceNode(classDecl, updatedClass);
        return solution.WithDocumentSyntaxRoot(classDocument.Id, newRoot);
    }
}
