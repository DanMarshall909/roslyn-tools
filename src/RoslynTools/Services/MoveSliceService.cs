using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Plans and validates a coherent cross-project architectural slice move.</summary>
internal sealed class MoveSliceService : IRtService
{
    private const int DiagnosticLimit = 50;

    [SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "Service instance preserves the operation/service dependency boundary.")]
    internal async Task<Result<MoveSlicePreparation, RtError>> PrepareAsync(
        Solution solution,
        MoveSliceIntent intent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var completeness = await WorkspaceCompleteness.AssessSemanticAsync(solution, cancellationToken);
        if (completeness.State != WorkspaceCompletenessState.Complete)
            return Error(
                "The workspace is incomplete; slice moves require error-free compilations for every project.",
                new Dictionary<string, string>
                {
                    ["workspaceState"] = completeness.State.ToString(),
                    ["diagnostics"] = string.Join(" | ", completeness.Diagnostics)
                });

        var targetProjects = solution.Projects
            .Where(project => string.Equals(
                project.Name,
                intent.TargetProject,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targetProjects.Length == 0)
            return Result<MoveSlicePreparation, RtError>.Err(RtError.NotFound(
                $"Project '{intent.TargetProject}' was not found."));
        if (targetProjects.Length > 1)
            return Result<MoveSlicePreparation, RtError>.Err(RtError.AmbiguousMatch(
                $"Project '{intent.TargetProject}' matched multiple loaded projects."));
        var targetProject = targetProjects[0];

        var selectionResult = intent.SelectionKind == MoveSliceSelectionKind.Types
            ? await SelectExplicitTypesAsync(solution, intent.Types, cancellationToken)
            : await SelectNamespaceAsync(solution, intent.SourceNamespace!, cancellationToken);
        SliceSelection? selection = null;
        RtError? selectionError = null;
        selectionResult.Match(value => selection = value, error => selectionError = error);
        if (selectionError is not null)
            return Result<MoveSlicePreparation, RtError>.Err(selectionError);

        var sourceProject = solution.GetProject(selection!.SourceProjectId)!;
        if (sourceProject.Id == targetProject.Id)
            return Error("move-slice requires different source and target projects.");

        var repositoryRoot = ResolveRepositoryRoot(solution, sourceProject, targetProject);
        if (repositoryRoot is null)
            return Error("The solution, source project, and target project must have physical paths.");
        if (!RepositoryTrustContext.IsExplicitlyTrustedFor(targetProject.FilePath!))
            return Error($"Target project '{targetProject.FilePath}' is outside the trusted repository root.");

        var rewriteResult = await RewriteAsync(
            solution,
            selection,
            intent,
            cancellationToken);
        if (rewriteResult.Error is not null)
            return Result<MoveSlicePreparation, RtError>.Err(rewriteResult.Error);

        var destinationResult = await PlanFileMovesAsync(
            solution,
            rewriteResult.Solution,
            selection,
            targetProject,
            intent,
            repositoryRoot,
            cancellationToken);
        if (destinationResult.Error is not null)
            return Result<MoveSlicePreparation, RtError>.Err(destinationResult.Error);

        var relatedTypes = intent.AnalyzeDependencies
            ? await AnalyzeDependenciesAsync(
                solution,
                selection,
                sourceProject,
                cancellationToken)
            : [];

        var projectPlanResult = await PlanProjectEditsAsync(
            solution,
            selection,
            sourceProject,
            targetProject,
            destinationResult.Moves,
            rewriteResult.References,
            intent,
            cancellationToken);
        if (projectPlanResult.Error is not null)
            return Result<MoveSlicePreparation, RtError>.Err(projectPlanResult.Error);

        var projectedSolution = await BuildProjectedSolutionAsync(
            rewriteResult.Solution,
            selection,
            targetProject.Id,
            destinationResult.Moves,
            projectPlanResult.ProjectReferences,
            intent.InternalsVisibleToEdits,
            cancellationToken);
        var projectedDiagnostics = await GetCompilationErrorsAsync(
            projectedSolution,
            cancellationToken);

        var diagnostics = new List<MutationDiagnostic>();
        diagnostics.AddRange(projectPlanResult.Diagnostics);
        diagnostics.AddRange(projectedDiagnostics.Select(diagnostic => new MutationDiagnostic(
            MutationDiagnosticSeverity.Error,
            diagnostic,
            "projected-compilation-error")));
        var applicable = diagnostics.All(diagnostic =>
            diagnostic.Severity != MutationDiagnosticSeverity.Error);

        var changedDocuments = rewriteResult.Solution.GetChanges(solution)
            .GetProjectChanges()
            .SelectMany(changes => changes.GetChangedDocuments())
            .Distinct()
            .ToArray();
        var sourceDocumentIds = selection.DocumentIds.ToHashSet();
        var sourceEdits = changedDocuments
            .Where(sourceDocumentIds.Contains)
            .Select(id => rewriteResult.Solution.GetDocument(id))
            .OfType<Document>()
            .Select(document => new MoveSliceDocumentEdit(
                document.FilePath!,
                "Change the selected declaration namespace and preserve source bindings."))
            .OrderBy(edit => edit.Path, StringComparer.Ordinal)
            .ToArray();
        var referenceEdits = changedDocuments
            .Where(id => !sourceDocumentIds.Contains(id))
            .Select(id => rewriteResult.Solution.GetDocument(id))
            .OfType<Document>()
            .Select(document => new MoveSliceDocumentEdit(
                document.FilePath!,
                "Retarget bound references to the moved slice."))
            .OrderBy(edit => edit.Path, StringComparer.Ordinal)
            .ToArray();

        var manifest = new MoveSliceResult(
            new MoveSliceSelection(
                intent.SelectionKind,
                intent.Types,
                intent.SourceNamespace),
            sourceProject.Name,
            targetProject.Name,
            intent.TargetNamespace,
            applicable,
            BuildTypeManifest(
                solution,
                selection,
                destinationResult.TargetNamespacesByDocument),
            sourceEdits,
            referenceEdits,
            destinationResult.Moves
                .Select(move => new MoveFileResult(move.SourcePath, move.DestinationPath))
                .OrderBy(move => move.SourcePath, StringComparer.Ordinal)
                .ToArray(),
            projectPlanResult.Manifest,
            relatedTypes,
            changedDocuments.Length);

        return Result<MoveSlicePreparation, RtError>.Ok(new MoveSlicePreparation(
            rewriteResult.Solution,
            manifest,
            destinationResult.Moves,
            projectPlanResult.Writes,
            diagnostics));
    }

    private static async Task<Result<SliceSelection, RtError>> SelectExplicitTypesAsync(
        Solution solution,
        IReadOnlyList<string> requestedTypes,
        CancellationToken cancellationToken)
    {
        var explicitSymbols = new List<INamedTypeSymbol>();
        foreach (var typeName in requestedTypes)
        {
            var matches = await FindExactSourceTypesAsync(solution, typeName, cancellationToken);
            if (matches.Count == 0)
                return Result<SliceSelection, RtError>.Err(RtError.NotFound(
                    $"No source type with FQN '{typeName}' was found."));
            if (matches.Select(match => match.ProjectId).Distinct().Count() != 1)
                return Result<SliceSelection, RtError>.Err(RtError.AmbiguousMatch(
                    $"Source type '{typeName}' is declared in multiple projects."));
            if (matches.Any(match => match.Symbol.ContainingType is not null))
                return ErrorSelection($"Nested type '{typeName}' cannot be moved independently.");
            explicitSymbols.Add(matches[0].Symbol);
        }

        var sourceProjectIds = explicitSymbols
            .SelectMany(symbol => symbol.DeclaringSyntaxReferences)
            .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Project.Id)
            .OfType<ProjectId>()
            .Distinct()
            .ToArray();
        if (sourceProjectIds.Length != 1)
            return ErrorSelection("Every explicitly selected type must originate in one source project.");

        var explicitNames = requestedTypes.ToHashSet(StringComparer.Ordinal);
        var selectedDocuments = explicitSymbols
            .SelectMany(symbol => symbol.DeclaringSyntaxReferences)
            .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Id)
            .OfType<DocumentId>()
            .ToHashSet();
        var selectedTypes = new Dictionary<string, SelectedType>(StringComparer.Ordinal);
        var pending = new Queue<DocumentId>(selectedDocuments);
        while (pending.TryDequeue(out var documentId))
        {
            var document = solution.GetDocument(documentId)!;
            foreach (var type in await GetTopLevelTypesAsync(document, cancellationToken))
            {
                var name = type.ToDisplayString();
                if (!selectedTypes.TryAdd(name, new SelectedType(
                        type,
                        explicitNames.Contains(name)
                            ? MoveSliceTypeSelectionReason.Explicit
                            : MoveSliceTypeSelectionReason.CoLocatedDeclaration)))
                    continue;

                foreach (var declaration in type.DeclaringSyntaxReferences)
                {
                    var partialDocument = solution.GetDocument(declaration.SyntaxTree);
                    if (partialDocument is not null && selectedDocuments.Add(partialDocument.Id))
                        pending.Enqueue(partialDocument.Id);
                }
            }
        }

        foreach (var name in explicitNames)
            if (!selectedTypes.ContainsKey(name))
                return ErrorSelection($"Type '{name}' could not be expanded into a complete file selection.");

        return Result<SliceSelection, RtError>.Ok(new SliceSelection(
            sourceProjectIds[0],
            selectedDocuments.OrderBy(id => id.Id).ToArray(),
            selectedTypes.Values
                .OrderBy(type => type.Symbol.ToDisplayString(), StringComparer.Ordinal)
                .ToArray()));
    }

    private static async Task<Result<SliceSelection, RtError>> SelectNamespaceAsync(
        Solution solution,
        string sourceNamespace,
        CancellationToken cancellationToken)
    {
        var matches = new List<(ProjectId ProjectId, INamedTypeSymbol Symbol)>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var type in await GetTopLevelTypesAsync(document, cancellationToken))
                {
                    var namespaceName = type.ContainingNamespace.ToDisplayString();
                    if (IsNamespaceOrDescendant(namespaceName, sourceNamespace))
                        matches.Add((project.Id, type));
                }
            }
        }

        if (matches.Count == 0)
            return Result<SliceSelection, RtError>.Err(RtError.NotFound(
                $"No source types were found in namespace '{sourceNamespace}'."));
        var projectIds = matches.Select(match => match.ProjectId).Distinct().ToArray();
        if (projectIds.Length != 1)
            return ErrorSelection(
                $"Namespace '{sourceNamespace}' spans multiple source projects; select explicit types instead.");

        var documents = matches
            .SelectMany(match => match.Symbol.DeclaringSyntaxReferences)
            .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Id)
            .OfType<DocumentId>()
            .Distinct()
            .ToArray();
        foreach (var documentId in documents)
        {
            var document = solution.GetDocument(documentId)!;
            var allTypes = await GetTopLevelTypesAsync(document, cancellationToken);
            var outside = allTypes.FirstOrDefault(type => !IsNamespaceOrDescendant(
                type.ContainingNamespace.ToDisplayString(),
                sourceNamespace));
            if (outside is not null)
                return ErrorSelection(
                    $"File '{document.FilePath}' also declares '{outside.ToDisplayString()}' outside the selected namespace. Files are indivisible; select explicit types to review the co-located declaration.");
        }

        return Result<SliceSelection, RtError>.Ok(new SliceSelection(
            projectIds[0],
            documents.OrderBy(id => id.Id).ToArray(),
            matches
                .Select(match => match.Symbol)
                .GroupBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
                .Select(group => group.First())
                .Select(symbol => new SelectedType(symbol, MoveSliceTypeSelectionReason.Namespace))
                .OrderBy(type => type.Symbol.ToDisplayString(), StringComparer.Ordinal)
                .ToArray()));
    }

    private static async Task<RewritePlan> RewriteAsync(
        Solution original,
        SliceSelection selection,
        MoveSliceIntent intent,
        CancellationToken cancellationToken)
    {
        var selectedSymbols = selection.Types.Select(type => type.Symbol).ToArray();
        var referencesByType = new Dictionary<string, IReadOnlyList<ReferencedSymbol>>(StringComparer.Ordinal);
        var namespaceChanges = new Dictionary<DocumentId, Dictionary<TextSpan, string>>();
        var qualifiedChanges = new Dictionary<DocumentId, Dictionary<TextSpan, string>>();
        var requiredUsings = new Dictionary<DocumentId, HashSet<string>>();
        foreach (var type in selectedSymbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            referencesByType[type.ToDisplayString()] = (await SymbolFinder.FindReferencesAsync(
                type,
                original,
                cancellationToken)).ToArray();
        }

        foreach (var documentId in selection.DocumentIds)
        {
            var originalDocument = original.GetDocument(documentId)!;
            var semanticModel = await originalDocument.GetSemanticModelAsync(cancellationToken);
            var root = await originalDocument.GetSyntaxRootAsync(cancellationToken);
            if (semanticModel is null || root is null)
                return RewritePlan.Fail($"Document '{originalDocument.FilePath}' has no complete semantic model.");

            var documentNamespaceChanges = GetOrAdd(namespaceChanges, documentId);
            foreach (var declaration in root.DescendantNodes().OfType<BaseNamespaceDeclarationSyntax>())
            {
                var declaredNamespace = semanticModel.GetDeclaredSymbol(declaration, cancellationToken)?
                    .ToDisplayString();
                if (declaredNamespace is null)
                    continue;
                var ownsSelectedType = declaration.Members
                    .Select(member => semanticModel.GetDeclaredSymbol(member, cancellationToken))
                    .OfType<INamedTypeSymbol>()
                    .Any(symbol => selectedSymbols.Contains(symbol, SymbolEqualityComparer.Default));
                if (!ownsSelectedType)
                    continue;
                documentNamespaceChanges[declaration.Span] = MapNamespace(declaredNamespace, intent);
            }

            if (documentNamespaceChanges.Count == 0)
                return RewritePlan.Fail(
                    $"Document '{originalDocument.FilePath}' has no rewritable namespace declaration for the selected types.");

            var preservedNamespaces = CollectUnmovedNamespaceDependencies(
                root,
                semanticModel,
                selectedSymbols,
                documentNamespaceChanges.Values,
                cancellationToken);
            foreach (var preservedNamespace in preservedNamespaces)
                GetOrAdd(requiredUsings, documentId).Add(preservedNamespace);
        }

        foreach (var selectedType in selection.Types)
        {
            var sourceNamespace = selectedType.Symbol.ContainingNamespace.ToDisplayString();
            var targetNamespace = MapNamespace(sourceNamespace, intent);
            foreach (var referenced in referencesByType[selectedType.Symbol.ToDisplayString()])
            {
                foreach (var location in referenced.Locations)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var document = original.GetDocument(location.Document.Id);
                    var root = document is null
                        ? null
                        : await document.GetSyntaxRootAsync(cancellationToken);
                    if (document is null || root is null)
                        return RewritePlan.Fail("A bound reference document disappeared during planning.");

                    var node = root.FindNode(location.Location.SourceSpan, getInnermostNodeForTie: true);
                    var outer = FindOutermostQualifiedName(node);
                    if (outer is not null)
                    {
                        var oldText = outer.ToString();
                        var newText = RewriteNamespacePrefix(oldText, sourceNamespace, targetNamespace);
                        if (!string.Equals(oldText, newText, StringComparison.Ordinal))
                            GetOrAdd(qualifiedChanges, document.Id)[outer.Span] = newText;
                    }
                    else if (!selection.DocumentIds.Contains(location.Document.Id))
                        GetOrAdd(requiredUsings, document.Id).Add(targetNamespace);
                }
            }
        }

        var solution = original;
        var changedDocumentIds = namespaceChanges.Keys
            .Concat(qualifiedChanges.Keys)
            .Concat(requiredUsings.Keys)
            .Distinct()
            .ToArray();
        foreach (var documentId in changedDocumentIds)
        {
            var document = original.GetDocument(documentId)!;
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (root is null)
                return RewritePlan.Fail($"Document '{document.FilePath}' has no syntax root.");
            var rewrittenRoot = new SliceDocumentRewriter(
                namespaceChanges.GetValueOrDefault(documentId),
                qualifiedChanges.GetValueOrDefault(documentId)).Visit(root)!;
            foreach (var namespaceName in requiredUsings.GetValueOrDefault(documentId) ?? [])
                rewrittenRoot = EnsureUsing(rewrittenRoot, namespaceName);
            solution = solution.WithDocumentSyntaxRoot(documentId, rewrittenRoot);
        }

        return new RewritePlan(solution, referencesByType.Values.SelectMany(value => value).ToArray(), null);
    }

    private static async Task<FileMovePlan> PlanFileMovesAsync(
        Solution original,
        Solution rewritten,
        SliceSelection selection,
        Project targetProject,
        MoveSliceIntent intent,
        string repositoryRoot,
        CancellationToken cancellationToken)
    {
        var targetDirectory = Path.GetDirectoryName(targetProject.FilePath)!;
        var rootNamespace = string.IsNullOrWhiteSpace(targetProject.DefaultNamespace)
            ? Path.GetFileNameWithoutExtension(targetProject.FilePath)
            : targetProject.DefaultNamespace;
        var moves = new List<PreparedFileMove>();
        var destinations = new HashSet<string>(PathComparer);
        var namespaces = new Dictionary<DocumentId, string>();

        foreach (var documentId in selection.DocumentIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var originalDocument = original.GetDocument(documentId)!;
            var rewrittenDocument = rewritten.GetDocument(documentId)!;
            if (originalDocument.FilePath is null)
                return FileMovePlan.Fail($"Selected document '{originalDocument.Name}' has no physical path.");
            if (!IsContainedBy(repositoryRoot, originalDocument.FilePath)
                || !RepositoryTrustContext.IsExplicitlyTrustedFor(originalDocument.FilePath))
                return FileMovePlan.Fail(
                    $"Selected document '{originalDocument.FilePath}' is outside the trusted repository.");

            var semanticModel = await originalDocument.GetSemanticModelAsync(cancellationToken);
            if (semanticModel is null)
                return FileMovePlan.Fail($"Document '{originalDocument.FilePath}' has no semantic model.");
            var targetNamespaces = (await GetTopLevelTypesAsync(originalDocument, cancellationToken))
                .Where(type => selection.Types.Any(selected =>
                    SymbolEqualityComparer.Default.Equals(selected.Symbol, type)))
                .Select(type => MapNamespace(type.ContainingNamespace.ToDisplayString(), intent))
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (targetNamespaces.Length != 1)
                return FileMovePlan.Fail(
                    $"Document '{originalDocument.FilePath}' maps to multiple target namespaces and cannot have one deterministic destination.");
            var targetNamespace = targetNamespaces[0];
            namespaces[documentId] = targetNamespace;

            var relativeDirectory = NamespaceToRelativeDirectory(
                targetNamespace,
                rootNamespace ?? string.Empty,
                targetDirectory);
            var destination = Path.GetFullPath(Path.Combine(
                targetDirectory,
                relativeDirectory,
                Path.GetFileName(originalDocument.FilePath)));
            if (!IsContainedBy(repositoryRoot, destination)
                || !RepositoryTrustContext.IsExplicitlyTrustedFor(destination))
                return FileMovePlan.Fail(
                    $"Destination '{destination}' is outside the trusted repository.");
            if (!destinations.Add(destination) || File.Exists(destination))
                return FileMovePlan.Fail(
                    $"Move destination '{destination}' already exists or is duplicated.");
            moves.Add(new PreparedFileMove(originalDocument.FilePath, destination));

            _ = rewrittenDocument;
        }

        return new FileMovePlan(
            moves.OrderBy(move => move.SourcePath, StringComparer.Ordinal).ToArray(),
            namespaces,
            null);
    }

    private static async Task<IReadOnlyList<MoveSliceRelatedType>> AnalyzeDependenciesAsync(
        Solution solution,
        SliceSelection selection,
        Project sourceProject,
        CancellationToken cancellationToken)
    {
        var moved = selection.Types.Select(type => type.Symbol)
            .ToHashSet(SymbolEqualityComparer.Default);
        var evidence = new Dictionary<string, List<MoveSliceDependencyEvidence>>(StringComparer.Ordinal);
        foreach (var documentId in selection.DocumentIds)
        {
            var document = solution.GetDocument(documentId)!;
            var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (semanticModel is null || root is null || document.FilePath is null)
                continue;

            foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var symbol = semanticModel.GetSymbolInfo(name, cancellationToken).Symbol;
                var type = symbol switch
                {
                    INamedTypeSymbol named => named,
                    IMethodSymbol method => method.ContainingType,
                    IPropertySymbol property => property.ContainingType,
                    IFieldSymbol field => field.ContainingType,
                    _ => null
                };
                if (type is null || moved.Contains(type))
                    continue;
                var declaringProject = type.DeclaringSyntaxReferences
                    .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Project.Id)
                    .OfType<ProjectId>()
                    .FirstOrDefault();
                if (declaringProject != sourceProject.Id)
                    continue;

                var fqn = type.ToDisplayString();
                if (!evidence.TryGetValue(fqn, out var locations))
                {
                    locations = [];
                    evidence[fqn] = locations;
                }
                var line = name.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var item = new MoveSliceDependencyEvidence(document.FilePath, line, fqn);
                if (!locations.Contains(item))
                    locations.Add(item);
            }
        }

        return evidence
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .Select(entry => new MoveSliceRelatedType(
                entry.Key,
                sourceProject.Name,
                entry.Value
                    .OrderBy(item => item.File, StringComparer.Ordinal)
                    .ThenBy(item => item.Line)
                    .ToArray()))
            .ToArray();
    }

    private static async Task<ProjectEditPlan> PlanProjectEditsAsync(
        Solution solution,
        SliceSelection selection,
        Project sourceProject,
        Project targetProject,
        IReadOnlyList<PreparedFileMove> moves,
        IReadOnlyList<ReferencedSymbol> references,
        MoveSliceIntent intent,
        CancellationToken cancellationToken)
    {
        var documents = new Dictionary<string, ProjectXml>(PathComparer);
        var manifest = new List<MoveSliceProjectEdit>();
        var diagnostics = new List<MutationDiagnostic>();
        ProjectXml? Load(Project project)
        {
            if (project.FilePath is null || !File.Exists(project.FilePath))
                return null;
            if (documents.TryGetValue(project.FilePath, out var existing))
                return existing;
            try
            {
                var original = File.ReadAllText(project.FilePath);
                var value = new ProjectXml(
                    project,
                    original,
                    XDocument.Parse(original, LoadOptions.PreserveWhitespace));
                documents[project.FilePath] = value;
                return value;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or System.Xml.XmlException)
            {
                return null;
            }
        }

        var sourceXml = Load(sourceProject);
        var targetXml = Load(targetProject);
        if (sourceXml is null || targetXml is null)
            return ProjectEditPlan.Fail("Source and target project files must exist and contain valid XML.");

        var sourceDirectory = Path.GetDirectoryName(sourceProject.FilePath)!;
        var targetDirectory = Path.GetDirectoryName(targetProject.FilePath)!;
        foreach (var move in moves)
        {
            var sourceRelative = NormalizeProjectPath(Path.GetRelativePath(sourceDirectory, move.SourcePath));
            var targetRelative = NormalizeProjectPath(Path.GetRelativePath(targetDirectory, move.DestinationPath));
            var removed = sourceXml.Document.Descendants()
                .Where(element => element.Name.LocalName == "Compile")
                .Where(element => string.Equals(
                    NormalizeProjectPath((string?)element.Attribute("Include") ?? string.Empty),
                    sourceRelative,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            foreach (var compile in removed)
                compile.Remove();
            if (removed.Length != 0)
                manifest.Add(new MoveSliceProjectEdit(
                    MoveSliceProjectEditKind.CompileItem,
                    sourceProject.Name,
                    sourceProject.FilePath!,
                    $"Remove explicit Compile Include for '{sourceRelative}'.",
                    true,
                    "Required by the selected file move."));

            if (!UsesDefaultCompileItems(targetXml.Document)
                && !HasItem(targetXml.Document, "Compile", targetRelative))
            {
                AddItem(targetXml.Document, "Compile", targetRelative);
                manifest.Add(new MoveSliceProjectEdit(
                    MoveSliceProjectEditKind.CompileItem,
                    targetProject.Name,
                    targetProject.FilePath!,
                    $"Add Compile Include for '{targetRelative}'.",
                    true,
                    "Required by the selected file move."));
            }
        }

        var requiredReferences = await FindRequiredProjectReferencesAsync(
            solution,
            selection,
            sourceProject,
            targetProject,
            references,
            cancellationToken);
        var projectedReferences = new List<(ProjectId Consumer, ProjectReference Reference)>();
        foreach (var (consumer, dependency) in requiredReferences)
        {
            if (consumer.ProjectReferences.Any(reference => reference.ProjectId == dependency.Id))
                continue;
            var requested = intent.UpdateProjectReferences;
            manifest.Add(new MoveSliceProjectEdit(
                MoveSliceProjectEditKind.ProjectReference,
                consumer.Name,
                consumer.FilePath ?? string.Empty,
                $"Add ProjectReference to '{dependency.Name}'.",
                requested,
                requested ? intent.ProjectReferenceJustification : null));
            if (!requested)
            {
                diagnostics.Add(new MutationDiagnostic(
                    MutationDiagnosticSeverity.Error,
                    $"Project '{consumer.Name}' requires a reference to '{dependency.Name}'. Review the manifest, then explicitly request project-reference updates with a justification.",
                    "project-reference-not-authorized"));
                continue;
            }
            if (ProjectTransitivelyReferences(solution, dependency.Id, consumer.Id))
            {
                diagnostics.Add(new MutationDiagnostic(
                    MutationDiagnosticSeverity.Error,
                    $"Adding a reference from '{consumer.Name}' to '{dependency.Name}' would create a cycle.",
                    "project-reference-cycle"));
                continue;
            }

            var consumerXml = Load(consumer);
            if (consumerXml is null || dependency.FilePath is null)
                return ProjectEditPlan.Fail(
                    $"Project-reference edit for '{consumer.Name}' has no valid physical project file.");
            var include = NormalizeProjectPath(Path.GetRelativePath(
                Path.GetDirectoryName(consumer.FilePath)!,
                dependency.FilePath));
            AddItem(consumerXml.Document, "ProjectReference", include);
            projectedReferences.Add((consumer.Id, new ProjectReference(dependency.Id)));
        }

        foreach (var edit in intent.InternalsVisibleToEdits)
        {
            var projects = solution.Projects
                .Where(project => string.Equals(project.Name, edit.Project, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (projects.Length != 1)
                return ProjectEditPlan.Fail(
                    $"InternalsVisibleTo project '{edit.Project}' did not resolve uniquely.");
            var projectXml = Load(projects[0]);
            if (projectXml is null)
                return ProjectEditPlan.Fail(
                    $"InternalsVisibleTo project '{edit.Project}' has no valid physical project file.");
            if (!HasItem(projectXml.Document, "InternalsVisibleTo", edit.Assembly))
                AddItem(projectXml.Document, "InternalsVisibleTo", edit.Assembly);
            manifest.Add(new MoveSliceProjectEdit(
                MoveSliceProjectEditKind.InternalsVisibleTo,
                projects[0].Name,
                projects[0].FilePath!,
                $"Add InternalsVisibleTo for '{edit.Assembly}'.",
                true,
                intent.InternalsVisibleToJustification));
        }

        var writes = documents.Values
            .Where(document => !XNode.DeepEquals(
                document.Document,
                XDocument.Parse(document.Original, LoadOptions.PreserveWhitespace)))
            .Select(document => new PreparedFileWrite(
                document.Project.FilePath!,
                document.Document.ToString(SaveOptions.DisableFormatting),
                document.Original))
            .OrderBy(write => write.Path, StringComparer.Ordinal)
            .ToArray();
        return new ProjectEditPlan(
            writes,
            manifest
                .OrderBy(edit => edit.ProjectPath, StringComparer.Ordinal)
                .ThenBy(edit => edit.Description, StringComparer.Ordinal)
                .ToArray(),
            projectedReferences,
            diagnostics,
            null);
    }

    private static async Task<IReadOnlyList<(Project Consumer, Project Dependency)>> FindRequiredProjectReferencesAsync(
        Solution solution,
        SliceSelection selection,
        Project sourceProject,
        Project targetProject,
        IReadOnlyList<ReferencedSymbol> references,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<(ProjectId, ProjectId), (Project, Project)>();
        foreach (var reference in references.SelectMany(reference => reference.Locations))
        {
            var consumer = solution.GetDocument(reference.Document.Id)?.Project;
            if (consumer is null || consumer.Id == targetProject.Id)
                continue;
            result[(consumer.Id, targetProject.Id)] = (consumer, targetProject);
        }

        var movedSymbols = selection.Types.Select(type => type.Symbol)
            .ToHashSet(SymbolEqualityComparer.Default);
        foreach (var documentId in selection.DocumentIds)
        {
            var document = solution.GetDocument(documentId)!;
            var model = await document.GetSemanticModelAsync(cancellationToken);
            var root = await document.GetSyntaxRootAsync(cancellationToken);
            if (model is null || root is null)
                continue;
            foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbol = model.GetSymbolInfo(name, cancellationToken).Symbol;
                var containingType = symbol switch
                {
                    INamedTypeSymbol type => type,
                    IMethodSymbol method => method.ContainingType,
                    IPropertySymbol property => property.ContainingType,
                    IFieldSymbol field => field.ContainingType,
                    _ => null
                };
                if (containingType is null || movedSymbols.Contains(containingType))
                    continue;
                var dependencyId = containingType.DeclaringSyntaxReferences
                    .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Project.Id)
                    .OfType<ProjectId>()
                    .FirstOrDefault();
                if (dependencyId is null || dependencyId == targetProject.Id)
                    continue;
                var dependency = solution.GetProject(dependencyId);
                if (dependency is not null)
                    result[(targetProject.Id, dependency.Id)] = (targetProject, dependency);
            }
        }

        return result.Values
            .Where(pair => pair.Item1.Id != pair.Item2.Id)
            .OrderBy(pair => pair.Item1.Name, StringComparer.Ordinal)
            .ThenBy(pair => pair.Item2.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private static async Task<Solution> BuildProjectedSolutionAsync(
        Solution rewritten,
        SliceSelection selection,
        ProjectId targetProjectId,
        IReadOnlyList<PreparedFileMove> moves,
        IReadOnlyList<(ProjectId Consumer, ProjectReference Reference)> references,
        IReadOnlyList<InternalsVisibleToEditRequest> friendEdits,
        CancellationToken cancellationToken)
    {
        var solution = rewritten;
        var moveBySource = moves.ToDictionary(move => move.SourcePath, PathComparer);
        foreach (var documentId in selection.DocumentIds)
        {
            var document = solution.GetDocument(documentId)!;
            var text = await document.GetTextAsync(cancellationToken);
            var sourcePath = document.FilePath!;
            var move = moveBySource[sourcePath];
            solution = solution.RemoveDocument(documentId);
            solution = solution.AddDocument(
                DocumentId.CreateNewId(targetProjectId),
                document.Name,
                text,
                filePath: move.DestinationPath);
        }

        foreach (var (consumer, reference) in references)
            solution = solution.AddProjectReference(consumer, reference);

        foreach (var group in friendEdits.GroupBy(edit => edit.Project, StringComparer.OrdinalIgnoreCase))
        {
            var project = solution.Projects.Single(candidate => string.Equals(
                candidate.Name,
                group.Key,
                StringComparison.OrdinalIgnoreCase));
            var attributes = string.Join(
                Environment.NewLine,
                group.Select(edit =>
                    $"[assembly: System.Runtime.CompilerServices.InternalsVisibleTo(\"{EscapeString(edit.Assembly)}\")]"));
            solution = solution.AddDocument(
                DocumentId.CreateNewId(project.Id),
                $"rt-move-slice-ivt-{Guid.NewGuid():N}.cs",
                SourceText.From(attributes));
        }

        return solution;
    }

    private static async Task<IReadOnlyList<string>> GetCompilationErrorsAsync(
        Solution solution,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken);
            if (compilation is null)
            {
                errors.Add($"Project '{project.Name}' did not produce a projected compilation.");
                continue;
            }
            errors.AddRange(compilation.GetDiagnostics(cancellationToken)
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Take(DiagnosticLimit - errors.Count)
                .Select(diagnostic =>
                    $"{project.Name}: {diagnostic.Id}: {diagnostic.GetMessage(CultureInfo.InvariantCulture)}"));
            if (errors.Count >= DiagnosticLimit)
                break;
        }
        return errors;
    }

    private static MoveSliceTypeManifest[] BuildTypeManifest(
        Solution solution,
        SliceSelection selection,
        IReadOnlyDictionary<DocumentId, string> targetNamespaces)
    {
        return selection.Types.Select(selected =>
        {
            var paths = selected.Symbol.DeclaringSyntaxReferences
                .Select(reference => solution.GetDocument(reference.SyntaxTree)?.FilePath)
                .OfType<string>()
                .Distinct(PathComparer)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var documentId = selected.Symbol.DeclaringSyntaxReferences
                .Select(reference => solution.GetDocument(reference.SyntaxTree)?.Id)
                .OfType<DocumentId>()
                .First();
            var targetNamespace = targetNamespaces[documentId];
            return new MoveSliceTypeManifest(
                selected.Symbol.ToDisplayString(),
                $"{targetNamespace}.{selected.Symbol.Name}",
                selected.Reason,
                paths);
        }).OrderBy(type => type.SourceType, StringComparer.Ordinal).ToArray();
    }

    private static async Task<IReadOnlyList<(ProjectId ProjectId, INamedTypeSymbol Symbol)>> FindExactSourceTypesAsync(
        Solution solution,
        string fqn,
        CancellationToken cancellationToken)
    {
        var result = new List<(ProjectId, INamedTypeSymbol)>();
        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                foreach (var symbol in await GetTopLevelTypesAsync(document, cancellationToken))
                    if (string.Equals(symbol.ToDisplayString(), fqn, StringComparison.Ordinal))
                        result.Add((project.Id, symbol));
            }
        }
        return result
            .GroupBy(item => (item.Item1, item.Item2.ToDisplayString()))
            .Select(group => group.First())
            .ToArray();
    }

    private static async Task<IReadOnlyList<INamedTypeSymbol>> GetTopLevelTypesAsync(
        Document document,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken);
        var model = await document.GetSemanticModelAsync(cancellationToken);
        if (root is null || model is null)
            return [];
        return root.DescendantNodes()
            .OfType<MemberDeclarationSyntax>()
            .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            .Select(node => model.GetDeclaredSymbol(node, cancellationToken))
            .OfType<INamedTypeSymbol>()
            .Where(symbol => symbol.ContainingType is null)
            .GroupBy(symbol => symbol.ToDisplayString(), StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    private static string[] CollectUnmovedNamespaceDependencies(
        SyntaxNode root,
        SemanticModel semanticModel,
        IReadOnlyList<INamedTypeSymbol> movedSymbols,
        IEnumerable<string> targetNamespaces,
        CancellationToken cancellationToken)
    {
        var moved = movedSymbols.ToHashSet(SymbolEqualityComparer.Default);
        var targetSet = targetNamespaces.ToHashSet(StringComparer.Ordinal);
        return root.DescendantNodes().OfType<SimpleNameSyntax>()
            .Select(name => semanticModel.GetSymbolInfo(name, cancellationToken).Symbol)
            .OfType<INamedTypeSymbol>()
            .Where(type => !moved.Contains(type))
            .Select(type => type.ContainingNamespace.ToDisplayString())
            .Where(namespaceName => !string.IsNullOrEmpty(namespaceName)
                                    && !targetSet.Contains(namespaceName))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(namespaceName => namespaceName, StringComparer.Ordinal)
            .ToArray();
    }

    private static string MapNamespace(string sourceNamespace, MoveSliceIntent intent)
    {
        if (intent.SelectionKind == MoveSliceSelectionKind.Types)
            return intent.TargetNamespace;
        if (string.Equals(sourceNamespace, intent.SourceNamespace, StringComparison.Ordinal))
            return intent.TargetNamespace;
        return intent.TargetNamespace + sourceNamespace[intent.SourceNamespace!.Length..];
    }

    private static SyntaxNode EnsureUsing(SyntaxNode root, string namespaceName)
    {
        if (root is not CompilationUnitSyntax compilationUnit
            || compilationUnit.Usings.Any(usingDirective => string.Equals(
                usingDirective.Name?.ToString(),
                namespaceName,
                StringComparison.Ordinal)))
            return root;
        return compilationUnit.AddUsings(
            SyntaxFactory.UsingDirective(SyntaxFactory.ParseName(namespaceName))
                .WithTrailingTrivia(SyntaxFactory.CarriageReturnLineFeed));
    }

    private static SyntaxNode? FindOutermostQualifiedName(SyntaxNode node)
    {
        var current = node;
        while (current.Parent is QualifiedNameSyntax or MemberAccessExpressionSyntax)
            current = current.Parent;
        return current != node ? current : null;
    }

    private static string RewriteNamespacePrefix(
        string text,
        string sourceNamespace,
        string targetNamespace)
    {
        const string globalAlias = "global::";
        var prefix = text.StartsWith(globalAlias, StringComparison.Ordinal) ? globalAlias : string.Empty;
        var candidate = prefix.Length == 0 ? text : text[prefix.Length..];
        var segments = sourceNamespace.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            var suffix = string.Join('.', segments[index..]);
            if (candidate.StartsWith(suffix + ".", StringComparison.Ordinal))
                return prefix + targetNamespace + candidate[suffix.Length..];
        }
        return text;
    }

    private static string NamespaceToRelativeDirectory(
        string namespaceName,
        string rootNamespace,
        string projectDirectory)
    {
        if (string.Equals(namespaceName, rootNamespace, StringComparison.Ordinal))
            return string.Empty;
        if (namespaceName.StartsWith(rootNamespace + ".", StringComparison.Ordinal))
            return namespaceName[(rootNamespace.Length + 1)..]
                .Replace('.', Path.DirectorySeparatorChar);

        var projectName = Path.GetFileName(Path.TrimEndingDirectorySeparator(projectDirectory));
        var segments = namespaceName.Split('.');
        var boundary = Array.FindIndex(segments, segment => string.Equals(
            segment,
            projectName,
            StringComparison.Ordinal));
        return boundary >= 0
            ? string.Join(Path.DirectorySeparatorChar, segments[(boundary + 1)..])
            : namespaceName.Replace('.', Path.DirectorySeparatorChar);
    }

    private static string? ResolveRepositoryRoot(
        Solution solution,
        Project sourceProject,
        Project targetProject)
    {
        if (solution.FilePath is not null)
            return Path.GetDirectoryName(Path.GetFullPath(solution.FilePath));
        var sourceDirectory = Path.GetDirectoryName(sourceProject.FilePath);
        var targetDirectory = Path.GetDirectoryName(targetProject.FilePath);
        if (sourceDirectory is null || targetDirectory is null)
            return null;
        var current = new DirectoryInfo(Path.GetFullPath(sourceDirectory));
        while (current is not null)
        {
            if (IsContainedBy(current.FullName, targetDirectory))
                return current.FullName;
            current = current.Parent;
        }
        return null;
    }

    private static bool IsContainedBy(string directory, string path)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(directory), Path.GetFullPath(path));
        return relative == "."
               || !Path.IsPathRooted(relative)
               && relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool IsNamespaceOrDescendant(string candidate, string root) =>
        string.Equals(candidate, root, StringComparison.Ordinal)
        || candidate.StartsWith(root + ".", StringComparison.Ordinal);

    private static bool UsesDefaultCompileItems(XDocument project)
    {
        var value = project.Descendants()
            .Where(element => element.Name.LocalName == "EnableDefaultCompileItems")
            .Select(element => element.Value.Trim())
            .LastOrDefault();
        return !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
               && project.Root?.Attribute("Sdk") is not null;
    }

    private static bool HasItem(XDocument project, string itemName, string include) =>
        project.Descendants().Any(element => element.Name.LocalName == itemName
                                             && string.Equals(
                                                 NormalizeProjectPath((string?)element.Attribute("Include") ?? string.Empty),
                                                 NormalizeProjectPath(include),
                                                 StringComparison.OrdinalIgnoreCase));

    private static void AddItem(XDocument project, string itemName, string include)
    {
        var root = project.Root!;
        var group = root.Elements().FirstOrDefault(element => element.Name.LocalName == "ItemGroup");
        if (group is null)
        {
            group = new XElement(root.Name.Namespace + "ItemGroup");
            root.Add(group);
        }
        group.Add(new XElement(
            root.Name.Namespace + itemName,
            new XAttribute("Include", include)));
    }

    private static bool ProjectTransitivelyReferences(Solution solution, ProjectId source, ProjectId target)
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
            foreach (var reference in solution.GetProject(current)?.ProjectReferences ?? [])
                pending.Push(reference.ProjectId);
        }
        return false;
    }

    private static string NormalizeProjectPath(string path) => path.Replace('\\', '/');

    private static string EscapeString(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static TValue GetOrAdd<TKey, TValue>(
        IDictionary<TKey, TValue> dictionary,
        TKey key)
        where TKey : notnull
        where TValue : new()
    {
        if (dictionary.TryGetValue(key, out var value))
            return value;
        value = new TValue();
        dictionary.Add(key, value);
        return value;
    }

    private static Result<MoveSlicePreparation, RtError> Error(
        string message,
        IReadOnlyDictionary<string, string>? context = null) =>
        Result<MoveSlicePreparation, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message, context));

    private static Result<SliceSelection, RtError> ErrorSelection(string message) =>
        Result<SliceSelection, RtError>.Err(new RtError(RtErrorKind.InvalidInput, message));

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private sealed record SelectedType(INamedTypeSymbol Symbol, MoveSliceTypeSelectionReason Reason);

    private sealed record SliceSelection(
        ProjectId SourceProjectId,
        IReadOnlyList<DocumentId> DocumentIds,
        IReadOnlyList<SelectedType> Types);

    private sealed record RewritePlan(
        Solution Solution,
        IReadOnlyList<ReferencedSymbol> References,
        RtError? Error)
    {
        internal static RewritePlan Fail(string message) => new(
            null!,
            [],
            new RtError(RtErrorKind.InvalidInput, message));
    }

    private sealed record FileMovePlan(
        IReadOnlyList<PreparedFileMove> Moves,
        IReadOnlyDictionary<DocumentId, string> TargetNamespacesByDocument,
        RtError? Error)
    {
        internal static FileMovePlan Fail(string message) => new(
            [],
            new Dictionary<DocumentId, string>(),
            new RtError(RtErrorKind.InvalidInput, message));
    }

    private sealed record ProjectXml(Project Project, string Original, XDocument Document);

    private sealed record ProjectEditPlan(
        IReadOnlyList<PreparedFileWrite> Writes,
        IReadOnlyList<MoveSliceProjectEdit> Manifest,
        IReadOnlyList<(ProjectId Consumer, ProjectReference Reference)> ProjectReferences,
        IReadOnlyList<MutationDiagnostic> Diagnostics,
        RtError? Error)
    {
        internal static ProjectEditPlan Fail(string message) => new(
            [],
            [],
            [],
            [],
            new RtError(RtErrorKind.InvalidInput, message));
    }

    private sealed class SliceDocumentRewriter(
        IReadOnlyDictionary<TextSpan, string>? namespaceChanges,
        IReadOnlyDictionary<TextSpan, string>? qualifiedChanges)
        : CSharpSyntaxRewriter
    {
        public override SyntaxNode? VisitNamespaceDeclaration(NamespaceDeclarationSyntax node) =>
            Rewrite(node, base.VisitNamespaceDeclaration(node));

        public override SyntaxNode? VisitFileScopedNamespaceDeclaration(FileScopedNamespaceDeclarationSyntax node) =>
            Rewrite(node, base.VisitFileScopedNamespaceDeclaration(node));

        private SyntaxNode? Rewrite(BaseNamespaceDeclarationSyntax original, SyntaxNode? visited)
        {
            if (visited is not BaseNamespaceDeclarationSyntax declaration
                || namespaceChanges is null
                || !namespaceChanges.TryGetValue(original.Span, out var targetNamespace))
                return visited;
            return declaration.WithName(
                SyntaxFactory.ParseName(targetNamespace).WithTriviaFrom(declaration.Name));
        }

        public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node) =>
            RewriteQualified(node, base.VisitQualifiedName(node));

        public override SyntaxNode? VisitAliasQualifiedName(AliasQualifiedNameSyntax node) =>
            RewriteQualified(node, base.VisitAliasQualifiedName(node));

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node) =>
            RewriteQualified(node, base.VisitMemberAccessExpression(node));

        private SyntaxNode? RewriteQualified(SyntaxNode original, SyntaxNode? visited)
        {
            if (visited is null
                || qualifiedChanges is null
                || !qualifiedChanges.TryGetValue(original.Span, out var replacementText))
                return visited;
            SyntaxNode replacement = original is ExpressionSyntax
                ? SyntaxFactory.ParseExpression(replacementText)
                : SyntaxFactory.ParseName(replacementText);
            return replacement.WithTriviaFrom(visited);
        }
    }
}
