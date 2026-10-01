using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides services for moving code elements between projects
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class MoveService : IRtService
{
    private readonly ISymbolResolutionService _symbols;

    public MoveService(ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();
    }
    // ──────────────────────────────────────────────────────────────
    //  Entry points
    // ──────────────────────────────────────────────────────────────

    /// <summary>Prepares a code move without writing files or rendering host output.</summary>
    internal async Task<Result<MovePreparation, RtError>> PrepareAsync(
        Solution solution,
        MoveIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return intent switch
        {
            TypeToNamespaceMove move => await PrepareTypeToNamespaceAsync(
                solution,
                move,
                cancellationToken),
            NamespaceToNamespaceMove move => await PrepareNamespaceToNamespaceAsync(
                solution,
                move,
                cancellationToken),
            TypeToProjectMove move => await PrepareTypeToProjectAsync(
                solution,
                move,
                cancellationToken),
            _ => Result<MovePreparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"Unsupported move intent '{intent.GetType().Name}'."))
        };
    }

    private async Task<Result<MovePreparation, RtError>> PrepareTypeToNamespaceAsync(
        Solution solution,
        TypeToNamespaceMove intent,
        CancellationToken cancellationToken)
    {
        var symbolResult = await ResolveMoveTypeAsync(solution, intent.Type);
        INamedTypeSymbol? symbol = null;
        RtError? symbolError = null;
        symbolResult.Match(value => symbol = value, error => symbolError = error);
        if (symbolError is not null)
            return Result<MovePreparation, RtError>.Err(symbolError);
        if (symbol!.ContainingType is not null)
            return Unsupported("Nested types cannot be moved independently.", intent.Type);

        var sourceNamespace = symbol.ContainingNamespace.ToDisplayString();
        if (string.IsNullOrEmpty(sourceNamespace))
            return Unsupported("Types in the global namespace are not supported.", intent.Type);

        var movedTypes = await CollectTypesInMovedDeclarationsAsync(
            solution,
            symbol,
            cancellationToken);
        var movedResults = BuildMovedTypeResults(solution, movedTypes, symbol);
        var sourceProjects = ResolveSourceProjects(solution, symbol);
        if (string.Equals(sourceNamespace, intent.TargetNamespace, StringComparison.Ordinal))
        {
            return Result<MovePreparation, RtError>.Ok(new MovePreparation(
                solution,
                new MoveResult(
                    MoveMode.TypeToNamespace,
                    movedResults,
                    sourceProjects,
                    null,
                    sourceNamespace,
                    intent.TargetNamespace,
                    0,
                    [],
                    []),
                [],
                [],
                [new MutationDiagnostic(
                    MutationDiagnosticSeverity.Information,
                    "Source and target namespaces are identical.")]));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var references = (await Task.WhenAll(movedTypes.Select(type =>
                SymbolFinder.FindReferencesAsync(type, solution, cancellationToken))))
            .SelectMany(found => found)
            .ToList();
        var referenceDocumentCount = references
            .SelectMany(reference => reference.Locations)
            .Select(location => location.Document.Id)
            .Distinct()
            .Count();

        var rewritten = await RewriteAllDeclarationsAsync(
            solution,
            symbol,
            intent.TargetNamespace);
        rewritten = await ApplyReferenceUsingsAsync(
            rewritten,
            references,
            sourceNamespace,
            intent.TargetNamespace);

        var declarationPaths = symbol.DeclaringSyntaxReferences
            .Select(reference => solution.GetDocument(reference.SyntaxTree)?.FilePath)
            .OfType<string>()
            .Distinct(PathComparer)
            .ToList();
        var (fileMoves, fileMoveError) = intent.MoveFile
            ? PrepareNamespaceFileMoves(
                solution,
                declarationPaths,
                sourceNamespace,
                intent.TargetNamespace)
            : ([], null);
        if (fileMoveError is not null)
            return Result<MovePreparation, RtError>.Err(fileMoveError);

        var moveResults = fileMoves
            .Select(move => new MoveFileResult(move.SourcePath, move.DestinationPath))
            .ToList();
        var diagnostics = movedTypes.Count > 1
            ? new[]
            {
                new MutationDiagnostic(
                    MutationDiagnosticSeverity.Warning,
                    $"The containing namespace declaration also moves {movedTypes.Count - 1} co-located type(s).",
                    "co-located-types")
            }
            : [];
        return Result<MovePreparation, RtError>.Ok(new MovePreparation(
            rewritten,
            new MoveResult(
                MoveMode.TypeToNamespace,
                movedResults,
                sourceProjects,
                null,
                sourceNamespace,
                intent.TargetNamespace,
                referenceDocumentCount,
                moveResults,
                []),
            fileMoves,
            [],
            diagnostics));
    }

    private async Task<Result<MovePreparation, RtError>> PrepareNamespaceToNamespaceAsync(
        Solution solution,
        NamespaceToNamespaceMove intent,
        CancellationToken cancellationToken)
    {
        var declarationPaths = new List<string>();
        var parentDependencies = new Dictionary<DocumentId, IReadOnlyList<string>>();
        var movedTypes = new List<INamedTypeSymbol>();
        var sourceProjects = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
                var declarations = root.DescendantNodes()
                    .OfType<BaseNamespaceDeclarationSyntax>()
                    .Where(declaration =>
                    {
                        var namespaceName = semanticModel?
                            .GetDeclaredSymbol(declaration, cancellationToken)?
                            .ToDisplayString();
                        return namespaceName is not null
                               && IsNamespaceOrDescendant(
                                   namespaceName,
                                   intent.SourceNamespace);
                    })
                    .ToList();
                if (declarations.Count != 0)
                {
                    if (semanticModel is not null)
                    {
                        foreach (var declaration in declarations)
                            foreach (var type in declaration.Members.OfType<BaseTypeDeclarationSyntax>())
                                if (semanticModel.GetDeclaredSymbol(type, cancellationToken)
                                    is INamedTypeSymbol named)
                                    movedTypes.Add(named);
                    }

                    if (document.FilePath is not null)
                        declarationPaths.Add(document.FilePath);
                    sourceProjects.Add(project.Name);

                    var dependencies = CollectFormerParentNamespaceDependencies(
                        root,
                        semanticModel,
                        intent.SourceNamespace,
                        intent.TargetNamespace,
                        cancellationToken);
                    if (dependencies.Length != 0)
                        parentDependencies[document.Id] = dependencies;
                }
            }
        }

        if (movedTypes.Count == 0)
            return Result<MovePreparation, RtError>.Err(RtError.NotFound(
                $"No types were found in namespace '{intent.SourceNamespace}'."));

        var movedResults = BuildMovedTypeResults(solution, movedTypes, explicitlyRequested: null);
        if (string.Equals(intent.SourceNamespace, intent.TargetNamespace, StringComparison.Ordinal))
        {
            return Result<MovePreparation, RtError>.Ok(new MovePreparation(
                solution,
                new MoveResult(
                    MoveMode.NamespaceToNamespace,
                    movedResults,
                    sourceProjects.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                    null,
                    intent.SourceNamespace,
                    intent.TargetNamespace,
                    0,
                    [],
                    []),
                [],
                [],
                [new MutationDiagnostic(
                    MutationDiagnosticSeverity.Information,
                    "Source and target namespaces are identical.")]));
        }

        var references = (await Task.WhenAll(
                movedTypes
                    .GroupBy(type => type.ToDisplayString(), StringComparer.Ordinal)
                    .Select(group => group.First())
                    .Select(type => SymbolFinder.FindReferencesAsync(
                        type,
                        solution,
                        cancellationToken))))
            .SelectMany(reference => reference)
            .ToList();
        var rewritten = await ApplyReferenceUsingsAsync(
            solution,
            references,
            intent.SourceNamespace,
            intent.TargetNamespace,
            ensureTargetNamespaceUsing: false);
        foreach (var documentId in rewritten.Projects
                     .SelectMany(project => project.DocumentIds)
                     .ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = rewritten.GetDocument(documentId);
            var root = document is null
                ? null
                : await document.GetSyntaxRootAsync(cancellationToken);
            if (root is null)
                continue;

            var rewriter = new NamespaceMoveRewriter(
                intent.SourceNamespace,
                intent.TargetNamespace);
            var updatedRoot = rewriter.Visit(root);
            if (!rewriter.Changed)
                continue;
            rewritten = rewritten.WithDocumentSyntaxRoot(documentId, updatedRoot);
        }

        var preservedParentNamespaces = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (documentId, dependencies) in parentDependencies)
        {
            var document = rewritten.GetDocument(documentId);
            var root = document is null
                ? null
                : await document.GetSyntaxRootAsync(cancellationToken);
            if (root is null)
                continue;

            foreach (var dependency in dependencies)
            {
                var changed = false;
                root = EnsureNamespaceUsing(root, dependency, ref changed);
                if (changed)
                    preservedParentNamespaces.Add(dependency);
            }

            rewritten = rewritten.WithDocumentSyntaxRoot(documentId, root);
        }

        var changedDocumentCount = rewritten.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(changes => changes.GetChangedDocuments())
            .Distinct()
            .Count();

        var (fileMoves, fileMoveError) = intent.MoveFiles
            ? PrepareNamespaceFileMoves(
                solution,
                declarationPaths,
                intent.SourceNamespace,
                intent.TargetNamespace)
            : ([], null);
        if (fileMoveError is not null)
            return Result<MovePreparation, RtError>.Err(fileMoveError);

        return Result<MovePreparation, RtError>.Ok(new MovePreparation(
            rewritten,
            new MoveResult(
                MoveMode.NamespaceToNamespace,
                movedResults,
                sourceProjects.OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                null,
                intent.SourceNamespace,
                intent.TargetNamespace,
                changedDocumentCount,
                fileMoves.Select(move =>
                    new MoveFileResult(move.SourcePath, move.DestinationPath)).ToArray(),
                []),
            fileMoves,
            [],
            preservedParentNamespaces
                .OrderBy(namespaceName => namespaceName, StringComparer.Ordinal)
                .Select(namespaceName => new MutationDiagnostic(
                    MutationDiagnosticSeverity.Warning,
                    $"Added an import for former parent namespace '{namespaceName}' to preserve bound references.",
                    "preserved-parent-namespace-dependency"))
                .ToArray()));
    }

    private async Task<Result<MovePreparation, RtError>> PrepareTypeToProjectAsync(
        Solution solution,
        TypeToProjectMove intent,
        CancellationToken cancellationToken)
    {
        var symbolResult = await ResolveMoveTypeAsync(solution, intent.Type);
        INamedTypeSymbol? symbol = null;
        RtError? symbolError = null;
        symbolResult.Match(value => symbol = value, error => symbolError = error);
        if (symbolError is not null)
            return Result<MovePreparation, RtError>.Err(symbolError);
        if (symbol!.ContainingType is not null)
            return Unsupported("Nested types cannot be moved independently.", intent.Type);

        var sourceDocuments = symbol.DeclaringSyntaxReferences
            .Select(reference => solution.GetDocument(reference.SyntaxTree))
            .OfType<Document>()
            .ToList();
        var sourceProjects = sourceDocuments.Select(document => document.Project)
            .DistinctBy(project => project.Id)
            .ToList();
        if (sourceProjects.Count != 1)
            return Unsupported("A cross-project move must originate in exactly one project.", intent.Type);

        var sourceProject = sourceProjects[0];
        var targetProject = solution.Projects.FirstOrDefault(project =>
            string.Equals(project.Name, intent.TargetProject, StringComparison.OrdinalIgnoreCase));
        if (targetProject is null)
            return Result<MovePreparation, RtError>.Err(RtError.NotFound(
                $"Project '{intent.TargetProject}' was not found.",
                new Dictionary<string, string> { ["to-project"] = intent.TargetProject }));

        var sourceNamespace = symbol.ContainingNamespace.ToDisplayString();
        if (sourceProject.Id == targetProject.Id)
        {
            if (intent.TargetNamespace is not null
                && !string.Equals(sourceNamespace, intent.TargetNamespace, StringComparison.Ordinal))
                return await PrepareTypeToNamespaceAsync(
                    solution,
                    new TypeToNamespaceMove(intent.Type, intent.TargetNamespace, MoveFile: false),
                    cancellationToken);

            var movedTypes = await CollectTypesInMovedDeclarationsAsync(
                solution,
                symbol,
                cancellationToken);
            return Result<MovePreparation, RtError>.Ok(new MovePreparation(
                solution,
                new MoveResult(
                    MoveMode.TypeToProject,
                    BuildMovedTypeResults(solution, movedTypes, symbol),
                    [sourceProject.Name],
                    targetProject.Name,
                    sourceNamespace,
                    intent.TargetNamespace ?? sourceNamespace,
                    0,
                    [],
                    []),
                [],
                [],
                [new MutationDiagnostic(
                    MutationDiagnosticSeverity.Information,
                    "Source and target projects are identical.")]));
        }

        var sourceDirectory = Path.GetDirectoryName(sourceProject.FilePath);
        var targetDirectory = Path.GetDirectoryName(targetProject.FilePath);
        if (sourceDirectory is null || targetDirectory is null)
            return Unsupported("Source and target projects must have physical project paths.", intent.Type);

        var movedTypesForResult = await CollectTypesInMovedDeclarationsAsync(
            solution,
            symbol,
            cancellationToken);
        var references = (await Task.WhenAll(movedTypesForResult.Select(type =>
                SymbolFinder.FindReferencesAsync(type, solution, cancellationToken))))
            .SelectMany(found => found)
            .ToList();

        var rewritten = solution;
        if (intent.TargetNamespace is not null
            && !string.Equals(sourceNamespace, intent.TargetNamespace, StringComparison.Ordinal))
        {
            rewritten = await RewriteAllDeclarationsAsync(
                rewritten,
                symbol,
                intent.TargetNamespace);
            rewritten = await ApplyReferenceUsingsAsync(
                rewritten,
                references,
                sourceNamespace,
                intent.TargetNamespace);
        }

        var fileMoves = new List<PreparedFileMove>();
        var destinations = new HashSet<string>(PathComparer);
        foreach (var document in sourceDocuments)
        {
            if (document.FilePath is null || !IsContainedBy(sourceDirectory, document.FilePath))
                return Unsupported(
                    $"Declaration '{document.FilePath}' is outside source project '{sourceProject.Name}'.",
                    intent.Type);
            var relativePath = Path.GetRelativePath(sourceDirectory, document.FilePath);
            var destinationPath = Path.GetFullPath(Path.Combine(targetDirectory, relativePath));
            if (!destinations.Add(destinationPath) || File.Exists(destinationPath))
                return Conflict($"Move destination '{destinationPath}' already exists or is duplicated.");
            fileMoves.Add(new PreparedFileMove(document.FilePath, destinationPath));
        }

        var projectEditResult = PrepareProjectFileEdits(
            solution,
            sourceProject,
            targetProject,
            fileMoves,
            references);
        List<PreparedFileWrite>? projectEdits = null;
        List<MoveProjectEditResult>? projectEditDescriptions = null;
        RtError? projectError = null;
        projectEditResult.Match(
            value => (projectEdits, projectEditDescriptions) = value,
            error => projectError = error);
        if (projectError is not null)
            return Result<MovePreparation, RtError>.Err(projectError);

        var referenceDocumentCount = references
            .SelectMany(reference => reference.Locations)
            .Select(location => location.Document.Id)
            .Distinct()
            .Count();
        var diagnostics = movedTypesForResult.Count > 1
            ? new[]
            {
                new MutationDiagnostic(
                    MutationDiagnosticSeverity.Warning,
                    $"The declaration file also moves {movedTypesForResult.Count - 1} co-located type(s).",
                    "co-located-types")
            }
            : [];
        return Result<MovePreparation, RtError>.Ok(new MovePreparation(
            rewritten,
            new MoveResult(
                MoveMode.TypeToProject,
                BuildMovedTypeResults(solution, movedTypesForResult, symbol),
                [sourceProject.Name],
                targetProject.Name,
                sourceNamespace,
                intent.TargetNamespace ?? sourceNamespace,
                referenceDocumentCount,
                fileMoves.Select(move =>
                    new MoveFileResult(move.SourcePath, move.DestinationPath)).ToArray(),
                projectEditDescriptions!),
            fileMoves,
            projectEdits!,
            diagnostics));
    }

    private async Task<Result<INamedTypeSymbol, RtError>> ResolveMoveTypeAsync(
        Solution solution,
        string type)
    {
        try
        {
            return Result<INamedTypeSymbol, RtError>.Ok(
                (INamedTypeSymbol)await _symbols.FindTypeByFqnAsync(solution, type));
        }
        catch (InvalidOperationException exception)
        {
            return Result<INamedTypeSymbol, RtError>.Err(RtError.NotFound(
                exception.Message,
                new Dictionary<string, string> { ["type"] = type }));
        }
    }

    private static Result<MovePreparation, RtError> Unsupported(string message, string type) =>
        Result<MovePreparation, RtError>.Err(new RtError(
            RtErrorKind.InvalidInput,
            message,
            new Dictionary<string, string> { ["type"] = type }));

    private static Result<MovePreparation, RtError> Conflict(string message) =>
        Result<MovePreparation, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message));

    private async Task<List<INamedTypeSymbol>> CollectTypesInMovedDeclarationsAsync(
        Solution solution,
        INamedTypeSymbol primary,
        CancellationToken cancellationToken)
    {
        var result = new List<INamedTypeSymbol>();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var syntaxReference in primary.DeclaringSyntaxReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = solution.GetDocument(syntaxReference.SyntaxTree);
            if (document is null)
                continue;
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var node = await syntaxReference.GetSyntaxAsync(cancellationToken);
            var declaration = node.AncestorsAndSelf()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();
            if (semanticModel is null || declaration is null)
                continue;

            foreach (var typeDeclaration in declaration.Members.OfType<BaseTypeDeclarationSyntax>())
                if (semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken)
                        is INamedTypeSymbol type
                    && seen.Add(type))
                    result.Add(type);
        }

        if (seen.Add(primary))
            result.Insert(0, primary);
        return result;
    }

    private static MoveTypeResult[] BuildMovedTypeResults(
        Solution solution,
        IEnumerable<INamedTypeSymbol> types,
        INamedTypeSymbol? explicitlyRequested)
    {
        return types
            .GroupBy(type => type.ToDisplayString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(type => new MoveTypeResult(
                type.ToDisplayString(),
                explicitlyRequested is not null
                && SymbolEqualityComparer.Default.Equals(type, explicitlyRequested),
                type.DeclaringSyntaxReferences
                    .Select(reference => solution.GetDocument(reference.SyntaxTree)?.FilePath)
                    .OfType<string>()
                    .FirstOrDefault() ?? string.Empty))
            .OrderBy(result => result.FullyQualifiedName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] ResolveSourceProjects(
        Solution solution,
        INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences
            .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Project.Name)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

    private (List<PreparedFileMove> Moves, RtError? Error) PrepareNamespaceFileMoves(
        Solution solution,
        IEnumerable<string> declarationPaths,
        string sourceNamespace,
        string targetNamespace)
    {
        var moves = new List<PreparedFileMove>();
        var destinations = new HashSet<string>(PathComparer);
        foreach (var sourcePath in declarationPaths.Distinct(PathComparer))
        {
            var document = solution.Projects
                .SelectMany(project => project.Documents)
                .FirstOrDefault(candidate => PathComparer.Equals(candidate.FilePath, sourcePath));
            var projectDirectory = Path.GetDirectoryName(document?.Project.FilePath);
            if (document is null || projectDirectory is null || !IsContainedBy(projectDirectory, sourcePath))
                return ([], new RtError(
                    RtErrorKind.InvalidInput,
                    $"Declaration '{sourcePath}' is outside its project directory."));

            var rootNamespace = document.Project.DefaultNamespace;
            if (string.IsNullOrWhiteSpace(rootNamespace))
                rootNamespace = Path.GetFileNameWithoutExtension(document.Project.FilePath);
            var oldDirectory = NamespaceToRelativeDir(
                sourceNamespace,
                rootNamespace ?? string.Empty,
                projectDirectory);
            var newDirectory = NamespaceToRelativeDir(
                targetNamespace,
                rootNamespace ?? string.Empty,
                projectDirectory);
            var relativePath = Path.GetRelativePath(projectDirectory, sourcePath);
            if (!IsInRelativeDirectory(relativePath, oldDirectory))
                return ([], new RtError(
                    RtErrorKind.InvalidInput,
                    $"Declaration '{sourcePath}' is not under namespace directory '{oldDirectory}'."));

            var remainder = oldDirectory.Length == 0
                ? relativePath
                : relativePath[oldDirectory.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var destination = Path.GetFullPath(
                Path.Combine(projectDirectory, newDirectory, remainder));
            if (PathComparer.Equals(sourcePath, destination))
                continue;
            if (!destinations.Add(destination) || File.Exists(destination))
                return ([], new RtError(
                    RtErrorKind.InvalidInput,
                    $"Move destination '{destination}' already exists or is duplicated."));
            moves.Add(new PreparedFileMove(sourcePath, destination));
        }

        return (moves, null);
    }

    private static bool IsContainedBy(string directory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return !Path.IsPathRooted(relative)
               && relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsInRelativeDirectory(string relativePath, string directory)
    {
        if (directory.Length == 0)
            return true;
        return string.Equals(relativePath, directory, StringComparison.OrdinalIgnoreCase)
               || relativePath.StartsWith(
                   directory + Path.DirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase)
               || relativePath.StartsWith(
                   directory + Path.AltDirectorySeparatorChar,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNamespaceOrDescendant(string candidate, string namespaceRoot) =>
        string.Equals(candidate, namespaceRoot, StringComparison.Ordinal)
        || candidate.StartsWith(namespaceRoot + ".", StringComparison.Ordinal);

    private static Result<(List<PreparedFileWrite> Writes, List<MoveProjectEditResult> Results), RtError>
        PrepareProjectFileEdits(
            Solution solution,
            Project sourceProject,
            Project targetProject,
            IReadOnlyList<PreparedFileMove> moves,
            IReadOnlyList<ReferencedSymbol> references)
    {
        if (sourceProject.FilePath is null || targetProject.FilePath is null)
            return Result<(List<PreparedFileWrite>, List<MoveProjectEditResult>), RtError>.Err(
                new RtError(RtErrorKind.InvalidInput, "Projects must have physical project files."));

        var documents = new Dictionary<string, (string Original, XDocument Document)>(PathComparer);
        var descriptions = new List<MoveProjectEditResult>();
        XDocument Load(string path)
        {
            if (documents.TryGetValue(path, out var existing))
                return existing.Document;
            var original = File.ReadAllText(path);
            var document = XDocument.Parse(original, LoadOptions.PreserveWhitespace);
            documents[path] = (original, document);
            return document;
        }

        var sourceXml = Load(sourceProject.FilePath);
        var targetXml = Load(targetProject.FilePath);
        var sourceDirectory = Path.GetDirectoryName(sourceProject.FilePath)!;
        var targetDirectory = Path.GetDirectoryName(targetProject.FilePath)!;
        foreach (var move in moves)
        {
            var sourceRelative = NormalizeProjectPath(Path.GetRelativePath(sourceDirectory, move.SourcePath));
            var targetRelative = NormalizeProjectPath(Path.GetRelativePath(targetDirectory, move.DestinationPath));
            foreach (var compile in sourceXml.Descendants()
                         .Where(element => element.Name.LocalName == "Compile")
                         .Where(element => string.Equals(
                             NormalizeProjectPath((string?)element.Attribute("Include") ?? string.Empty),
                             sourceRelative,
                             StringComparison.OrdinalIgnoreCase))
                         .ToList())
                compile.Remove();
            if (!UsesDefaultCompileItems(targetXml)
                && !targetXml.Descendants().Any(element =>
                    element.Name.LocalName == "Compile"
                    && string.Equals(
                        NormalizeProjectPath((string?)element.Attribute("Include") ?? string.Empty),
                        targetRelative,
                        StringComparison.OrdinalIgnoreCase)))
            {
                AddItem(targetXml, "Compile", targetRelative);
                descriptions.Add(new MoveProjectEditResult(
                    targetProject.FilePath,
                    $"Add Compile Include for '{targetRelative}'."));
            }
        }

        if (!XNode.DeepEquals(sourceXml, XDocument.Parse(
                documents[sourceProject.FilePath].Original,
                LoadOptions.PreserveWhitespace)))
            descriptions.Add(new MoveProjectEditResult(
                sourceProject.FilePath,
                "Remove explicit source Compile items."));

        var consumingProjects = references
            .SelectMany(reference => reference.Locations)
            .Select(location => solution.GetDocument(location.Document.Id)?.Project)
            .OfType<Project>()
            .Where(project => project.Id != targetProject.Id)
            .DistinctBy(project => project.Id)
            .ToList();
        foreach (var consumer in consumingProjects)
        {
            if (consumer.ProjectReferences.Any(reference => reference.ProjectId == targetProject.Id))
                continue;
            if (ProjectTransitivelyReferences(solution, targetProject.Id, consumer.Id))
                return Result<(List<PreparedFileWrite>, List<MoveProjectEditResult>), RtError>.Err(
                    new RtError(
                        RtErrorKind.InvalidInput,
                        $"Moving to '{targetProject.Name}' would require a circular project reference from '{consumer.Name}'."));
            if (consumer.FilePath is null)
                return Result<(List<PreparedFileWrite>, List<MoveProjectEditResult>), RtError>.Err(
                    new RtError(
                        RtErrorKind.InvalidInput,
                        $"Consuming project '{consumer.Name}' has no project file path."));

            var consumerXml = Load(consumer.FilePath);
            var consumerDirectory = Path.GetDirectoryName(consumer.FilePath)!;
            var include = NormalizeProjectPath(
                Path.GetRelativePath(consumerDirectory, targetProject.FilePath));
            AddItem(consumerXml, "ProjectReference", include);
            descriptions.Add(new MoveProjectEditResult(
                consumer.FilePath,
                $"Add ProjectReference to '{targetProject.Name}'."));
        }

        var writes = documents
            .Where(entry => !XNode.DeepEquals(
                entry.Value.Document,
                XDocument.Parse(entry.Value.Original, LoadOptions.PreserveWhitespace)))
            .Select(entry => new PreparedFileWrite(
                entry.Key,
                entry.Value.Document.ToString(SaveOptions.DisableFormatting),
                entry.Value.Original))
            .ToList();
        return Result<(List<PreparedFileWrite>, List<MoveProjectEditResult>), RtError>.Ok(
            (writes, descriptions));
    }

    private static bool UsesDefaultCompileItems(XDocument project)
    {
        var disabled = project.Descendants()
            .Where(element => element.Name.LocalName == "EnableDefaultCompileItems")
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        if (string.Equals(disabled, "false", StringComparison.OrdinalIgnoreCase))
            return false;
        return project.Root?.Attribute("Sdk") is not null;
    }

    private static void AddItem(XDocument project, string itemName, string include)
    {
        var root = project.Root!;
        var ns = root.Name.Namespace;
        var group = root.Elements().FirstOrDefault(element => element.Name.LocalName == "ItemGroup");
        if (group is null)
        {
            group = new XElement(ns + "ItemGroup");
            root.Add(group);
        }
        group.Add(new XElement(ns + itemName, new XAttribute("Include", include)));
    }

    private static bool ProjectTransitivelyReferences(
        Solution solution,
        ProjectId source,
        ProjectId target)
    {
        var visited = new HashSet<ProjectId>();
        var pending = new Stack<ProjectId>();
        pending.Push(source);
        while (pending.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;
            if (current == target)
                return true;
            var project = solution.GetProject(current);
            if (project is null)
                continue;
            foreach (var reference in project.ProjectReferences)
                pending.Push(reference.ProjectId);
        }
        return false;
    }

    private static string NormalizeProjectPath(string path) => path.Replace('\\', '/');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    /// <summary>
    /// Rewrites a type's containing namespace declaration.
    /// </summary>
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
                newRoot = EnsureNamespaceUsing(newRoot, oldNamespace, ref _);
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
        string targetNamespace,
        bool ensureTargetNamespaceUsing = true)
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

            if (ensureTargetNamespaceUsing)
                updatedRoot = EnsureNamespaceUsing(updatedRoot, targetNamespace, ref changed);

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
            var newFqText = RewriteNamespacePrefix(oldFqText, oldNamespace, targetNamespace);
            if (oldFqText == newFqText) continue;

            var newNode = SyntaxFactory.ParseName(newFqText).WithTriviaFrom(qualifiedRoot);
            currentRoot = currentRoot.ReplaceNode(qualifiedRoot, newNode);
            changed = true;
        }

        return (currentRoot, changed);
    }

    private static string RewriteNamespacePrefix(
        string text,
        string oldNamespace,
        string targetNamespace)
    {
        const string globalAlias = "global::";
        var prefix = text.StartsWith(globalAlias, StringComparison.Ordinal)
            ? globalAlias
            : string.Empty;
        var candidate = prefix.Length == 0 ? text : text[prefix.Length..];
        var oldSegments = oldNamespace.Split('.');

        for (var index = 0; index < oldSegments.Length; index++)
        {
            var namespaceSuffix = string.Join('.', oldSegments[index..]);
            if (!candidate.StartsWith(namespaceSuffix + ".", StringComparison.Ordinal))
                continue;

            return prefix
                   + targetNamespace
                   + candidate[namespaceSuffix.Length..];
        }

        return text;
    }

    private static string[] CollectFormerParentNamespaceDependencies(
        SyntaxNode root,
        SemanticModel? semanticModel,
        string sourceNamespace,
        string targetNamespace,
        CancellationToken cancellationToken)
    {
        if (semanticModel is null)
            return [];

        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsQualifiedNamePart(name))
                continue;

            var symbol = semanticModel.GetSymbolInfo(name, cancellationToken).Symbol;
            var namespaceName = symbol?.ContainingNamespace?.ToDisplayString();
            if (string.IsNullOrEmpty(namespaceName)
                || !sourceNamespace.StartsWith(namespaceName + ".", StringComparison.Ordinal)
                || targetNamespace.StartsWith(namespaceName + ".", StringComparison.Ordinal))
                continue;

            dependencies.Add(namespaceName);
        }

        return dependencies.OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }

    private static bool IsQualifiedNamePart(SimpleNameSyntax name) =>
        name.Parent is QualifiedNameSyntax qualifiedName && qualifiedName.Right == name
        || name.Parent is AliasQualifiedNameSyntax aliasQualifiedName && aliasQualifiedName.Name == name
        || name.Parent is MemberAccessExpressionSyntax memberAccess && memberAccess.Name == name;

    private SyntaxNode EnsureNamespaceUsing(
        SyntaxNode root,
        string namespaceName,
        ref bool changed)
    {
        if (root is not CompilationUnitSyntax compilationUnit)
            return root;

        var existingUsings = compilationUnit.Usings
            .Select(u => u.Name?.ToFullString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existingUsings.Contains(namespaceName))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(
                SyntaxFactory.ParseName(" " + namespaceName))
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

    /// <summary>
    /// Converts a namespace to its relative path within the solution.
    /// </summary>
    internal string NamespaceToRelativeDir(
        string ns,
        string rootNamespace,
        string? projectDirectory = null)
    {
        if (ns == rootNamespace)
            return "";

        if (ns.StartsWithOrdinal(rootNamespace + "."))
            return ns[(rootNamespace.Length + 1)..].Replace('.', Path.DirectorySeparatorChar);

        // A project can be renamed before or after its namespaces. When the loaded
        // DefaultNamespace represents the other side of that rename, use the project
        // folder as the stable boundary instead of nesting the complete namespace.
        var projectFolder = projectDirectory is null
            ? null
            : Path.GetFileName(Path.TrimEndingDirectorySeparator(projectDirectory));
        if (!string.IsNullOrWhiteSpace(projectFolder))
        {
            var segments = ns.Split('.');
            var projectBoundary = Array.FindIndex(
                segments,
                segment => string.Equals(segment, projectFolder, StringComparison.Ordinal));
            if (projectBoundary >= 0)
                return string.Join(
                    Path.DirectorySeparatorChar,
                    segments[(projectBoundary + 1)..]);
        }

        return ns.Replace('.', Path.DirectorySeparatorChar);
    }
}
