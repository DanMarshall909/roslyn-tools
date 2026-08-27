using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Scans a Roslyn solution against the versioned language-feature catalogue.</summary>
internal sealed class LanguageFeaturesAnalyzer : IRtService
{
    private readonly IReadOnlyList<LanguageFeatureDefinition> _definitions =
        LanguageFeatureCatalog.Definitions;

    public LanguageFeaturesAnalyzer()
    {
    }

    internal async Task<LanguageFeaturesResult> AnalyzeAsync(
        Solution solution,
        LanguageFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<LanguageFeatureDiagnostic>();
        var aggregate = new FeatureAccumulatorSet();
        var projectScans = new List<ProjectScan>();
        var solutionDirectory = Path.GetDirectoryName(solution.FilePath);

        foreach (var project in solution.Projects.OrderBy(project => project.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (project.Language != LanguageNames.CSharp)
            {
                diagnostics.Add(new LanguageFeatureDiagnostic(
                    "unsupported_project",
                    "warning",
                    $"Project '{project.Name}' uses unsupported language '{project.Language}'.",
                    project.Name,
                    project.FilePath));
                continue;
            }

            var projectScan = await AnalyzeProjectAsync(
                project,
                solutionDirectory,
                request,
                diagnostics,
                cancellationToken);
            projectScans.Add(projectScan);
            aggregate.Merge(projectScan.Accumulators);
        }

        var totals = new LanguageFeatureTotals(
            projectScans.Count,
            projectScans.Sum(project => project.Files),
            projectScans.Sum(project => project.NonBlankSourceLines));
        AddUnsupportedDetectorDiagnostics(request, diagnostics);
        var selectedDefinitions = SelectDefinitions(request, aggregate, diagnostics);
        var features = selectedDefinitions
            .Select(definition => CreateResult(
                definition,
                aggregate[definition.Id],
                totals.Files,
                totals.NonBlankSourceLines,
                request.IncludeLocations))
            .ToList();
        var selectedIds = selectedDefinitions.Select(definition => definition.Id).ToHashSet(StringComparer.Ordinal);
        var projects = projectScans.Select(project => new LanguageFeatureProjectResult(
                project.Name,
                project.LanguageVersion,
                project.EffectiveLanguageVersion,
                project.Files,
                project.NonBlankSourceLines,
                selectedDefinitions
                    .Where(definition => selectedIds.Contains(definition.Id))
                    .Select(definition => CreateProjectMetric(
                        definition.Id,
                        project.Accumulators[definition.Id],
                        project.Files,
                        project.NonBlankSourceLines,
                        request.IncludeLocations))
                    .ToList()))
            .ToList();

        return new LanguageFeaturesResult(
            LanguageFeatureCatalog.Version,
            request.IncludeGenerated ? "included" : "excluded",
            totals,
            projects,
            features,
            diagnostics);
    }

    private static async Task<ProjectScan> AnalyzeProjectAsync(
        Project project,
        string? solutionDirectory,
        LanguageFeaturesRequest request,
        List<LanguageFeatureDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var accumulators = new FeatureAccumulatorSet();
        var analyzedPaths = new List<string>();
        var sourceLines = 0;

        foreach (var document in project.Documents.OrderBy(document => document.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var text = await document.GetTextAsync(cancellationToken);
                if (!request.IncludeGenerated && IsGenerated(document.FilePath, text))
                    continue;

                var path = NormalizePath(solutionDirectory, document.FilePath, document.Name);
                analyzedPaths.Add(path);
                sourceLines += CountSourceLines(text);
                var root = await document.GetSyntaxRootAsync(cancellationToken);
                if (root is null)
                {
                    diagnostics.Add(new LanguageFeatureDiagnostic(
                        "document_unavailable",
                        "error",
                        $"Syntax was unavailable for '{path}'.",
                        project.Name,
                        path));
                    continue;
                }

                AddParseDiagnostics(project.Name, path, root.SyntaxTree, diagnostics);
                ScanSyntax(project.Name, path, root, accumulators);
                await ScanSemanticAsync(project.Name, path, document, root, accumulators, diagnostics, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                diagnostics.Add(new LanguageFeatureDiagnostic(
                    "document_analysis_failed",
                    "error",
                    $"Could not analyze '{document.FilePath ?? document.Name}': {exception.Message}",
                    project.Name,
                    document.FilePath ?? document.Name));
            }
        }

        if (project.CompilationOptions is CSharpCompilationOptions compilationOptions
            && compilationOptions.NullableContextOptions != NullableContextOptions.Disable)
        {
            var location = new LanguageFeatureLocation(
                project.Name,
                NormalizePath(solutionDirectory, project.FilePath, project.Name),
                1,
                1);
            accumulators["nullable_reference_types"].AddProjectOption(location, analyzedPaths);
        }

        var parseOptions = project.ParseOptions as CSharpParseOptions;
        var specifiedVersion = parseOptions?.LanguageVersion ?? LanguageVersion.Default;
        return new ProjectScan(
            project.Name,
            LanguageVersionFacts.ToDisplayString(specifiedVersion),
            LanguageVersionFacts.ToDisplayString(
                LanguageVersionFacts.MapSpecifiedToEffectiveVersion(specifiedVersion)),
            analyzedPaths.Count,
            sourceLines,
            accumulators);
    }

    private static void ScanSyntax(
        string project,
        string path,
        SyntaxNode root,
        FeatureAccumulatorSet accumulators)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            var location = CreateLocation(project, path, node.GetLocation());
            ScanOverlappingSyntax(node, location, path, accumulators);
            switch (node)
            {
                case FileScopedNamespaceDeclarationSyntax:
                    accumulators["file_scoped_namespace"].Add(location, path);
                    accumulators["file_scoped_namespace"].AddOpportunity();
                    break;
                case NamespaceDeclarationSyntax:
                    accumulators["file_scoped_namespace"].AddOpportunity(location);
                    break;
                case UsingDirectiveSyntax usingDirective when !usingDirective.GlobalKeyword.IsKind(SyntaxKind.None):
                    accumulators["global_using"].Add(location, path);
                    break;
                case RecordDeclarationSyntax:
                    accumulators["record_declaration"].Add(location, path);
                    break;
                case TypeDeclarationSyntax typeDeclaration
                    when typeDeclaration is not RecordDeclarationSyntax && typeDeclaration.ParameterList is not null:
                    accumulators["primary_constructor"].Add(location, path);
                    accumulators["primary_constructor"].AddOpportunity();
                    break;
                case AccessorDeclarationSyntax accessor when accessor.IsKind(SyntaxKind.InitAccessorDeclaration):
                    accumulators["init_accessor"].Add(location, path);
                    accumulators["init_accessor"].AddOpportunity();
                    break;
                case AccessorDeclarationSyntax accessor when accessor.IsKind(SyntaxKind.SetAccessorDeclaration):
                    accumulators["init_accessor"].AddOpportunity(location);
                    break;
                case FunctionPointerTypeSyntax:
                    accumulators["function_pointer"].Add(location, path);
                    break;
                case CollectionExpressionSyntax:
                    accumulators["collection_expression"].Add(location, path);
                    break;
                case LiteralExpressionSyntax literal when IsRawString(literal.Token):
                    accumulators["raw_string_literal"].Add(location, path);
                    break;
                case InterpolatedStringExpressionSyntax interpolated when IsRawString(interpolated.StringStartToken):
                    accumulators["raw_string_literal"].Add(location, path);
                    accumulators["interpolated_string"].Add(location, path);
                    break;
                case ImplicitObjectCreationExpressionSyntax:
                    accumulators["target_typed_new"].Add(location, path);
                    break;
                case WithExpressionSyntax:
                    accumulators["with_expression"].Add(location, path);
                    break;
                case SwitchExpressionSyntax:
                    accumulators["switch_expression"].Add(location, path);
                    accumulators["switch_expression"].AddOpportunity();
                    break;
                case SwitchStatementSyntax:
                    accumulators["switch_expression"].AddOpportunity(location);
                    break;
                case RangeExpressionSyntax:
                    accumulators["range_expression"].Add(location, path);
                    break;
                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.IndexExpression):
                    accumulators["index_expression"].Add(location, path);
                    break;
                case ConditionalAccessExpressionSyntax:
                    accumulators["null_conditional"].Add(location, path);
                    break;
                case AssignmentExpressionSyntax assignment when assignment.IsKind(SyntaxKind.CoalesceAssignmentExpression):
                    accumulators["null_coalescing_assignment"].Add(location, path);
                    break;
                case ThrowExpressionSyntax:
                    accumulators["throw_expression"].Add(location, path);
                    break;
                case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.DefaultLiteralExpression):
                    accumulators["default_literal"].Add(location, path);
                    break;
                case TupleExpressionSyntax:
                    accumulators["tuple_expression"].Add(location, path);
                    break;
                case AnonymousObjectCreationExpressionSyntax:
                    accumulators["anonymous_type"].Add(location, path);
                    break;
                case QueryExpressionSyntax:
                    accumulators["query_expression"].Add(location, path);
                    break;
                case LambdaExpressionSyntax lambda:
                    accumulators["lambda_expression"].Add(location, path);
                    if (!lambda.AsyncKeyword.IsKind(SyntaxKind.None))
                        accumulators["async_method"].Add(location, path);
                    break;
                case AnonymousMethodExpressionSyntax anonymousMethod
                    when !anonymousMethod.AsyncKeyword.IsKind(SyntaxKind.None):
                    accumulators["async_method"].Add(location, path);
                    break;
                case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.ObjectInitializerExpression):
                    accumulators["object_initializer"].Add(location, path);
                    break;
                case InitializerExpressionSyntax initializer when initializer.IsKind(SyntaxKind.CollectionInitializerExpression):
                    accumulators["collection_initializer"].Add(location, path);
                    break;
                case InterpolatedStringExpressionSyntax:
                    accumulators["interpolated_string"].Add(location, path);
                    break;
                case ListPatternSyntax:
                    accumulators["list_pattern"].Add(location, path);
                    break;
                case RecursivePatternSyntax:
                    accumulators["property_pattern"].Add(location, path);
                    break;
                case RelationalPatternSyntax:
                    accumulators["relational_pattern"].Add(location, path);
                    break;
                case BinaryPatternSyntax:
                case UnaryPatternSyntax:
                    accumulators["combinator_pattern"].Add(location, path);
                    break;
                case LocalDeclarationStatementSyntax declaration
                    when !declaration.UsingKeyword.IsKind(SyntaxKind.None):
                    accumulators["using_declaration"].Add(location, path);
                    if (!declaration.AwaitKeyword.IsKind(SyntaxKind.None))
                        accumulators["await_using"].Add(location, path);
                    break;
                case CommonForEachStatementSyntax forEach
                    when !forEach.AwaitKeyword.IsKind(SyntaxKind.None):
                    accumulators["await_foreach"].Add(location, path);
                    break;
                case UsingStatementSyntax usingStatement
                    when !usingStatement.AwaitKeyword.IsKind(SyntaxKind.None):
                    accumulators["await_using"].Add(location, path);
                    break;
                case LocalFunctionStatementSyntax localFunction:
                    accumulators["local_function"].Add(location, path);
                    if (localFunction.Modifiers.Any(SyntaxKind.AsyncKeyword))
                        accumulators["async_method"].Add(location, path);
                    break;
                case ArrowExpressionClauseSyntax:
                    accumulators["expression_bodied_member"].Add(location, path);
                    break;
                case TupleTypeSyntax:
                    accumulators["tuple_type"].Add(location, path);
                    break;
                case MethodDeclarationSyntax method when method.Modifiers.Any(SyntaxKind.AsyncKeyword):
                    accumulators["async_method"].Add(location, path);
                    break;
                case AwaitExpressionSyntax:
                    accumulators["await_expression"].Add(location, path);
                    break;
            }
        }
    }

    private static void ScanOverlappingSyntax(
        SyntaxNode node,
        LanguageFeatureLocation location,
        string path,
        FeatureAccumulatorSet accumulators)
    {
        if (node is PropertyDeclarationSyntax property)
        {
            if (property.Modifiers.Any(SyntaxKind.RequiredKeyword))
                accumulators["required_member"].Add(location, path);
            if (property.Modifiers.Any(SyntaxKind.PartialKeyword))
                accumulators["partial_property"].Add(location, path);
        }

        if (node is FieldDeclarationSyntax field && field.Modifiers.Any(SyntaxKind.RequiredKeyword))
            accumulators["required_member"].Add(location, path);

        if (node is IndexerDeclarationSyntax indexer && indexer.Modifiers.Any(SyntaxKind.PartialKeyword))
            accumulators["partial_indexer"].Add(location, path);

        if (node is EventDeclarationSyntax eventDeclaration
            && eventDeclaration.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            accumulators["partial_event"].Add(location, path);
        }

        if (node is EventFieldDeclarationSyntax eventField
            && eventField.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            accumulators["partial_event"].Add(location, path);
        }

        if (node is ConstructorDeclarationSyntax constructor
            && constructor.Modifiers.Any(SyntaxKind.PartialKeyword))
        {
            accumulators["partial_constructor"].Add(location, path);
        }

        if (node is BaseTypeDeclarationSyntax type
            && type.Modifiers.Any(SyntaxKind.FileKeyword))
        {
            accumulators["file_local_type"].Add(location, path);
        }

        if (node is StructDeclarationSyntax structDeclaration)
        {
            if (structDeclaration.Modifiers.Any(SyntaxKind.RefKeyword))
                accumulators["ref_struct"].Add(location, path);
            if (structDeclaration.Modifiers.Any(SyntaxKind.ReadOnlyKeyword))
                accumulators["readonly_struct"].Add(location, path);
        }

        if (node is ParameterSyntax parameter)
        {
            if (parameter.Modifiers.Any(SyntaxKind.ParamsKeyword)
                && parameter.Type is not ArrayTypeSyntax)
            {
                accumulators["params_collection"].Add(location, path);
            }

            if (parameter.Modifiers.Count > 0
                && parameter.Ancestors().Any(ancestor => ancestor is LambdaExpressionSyntax))
            {
                accumulators["lambda_parameter_modifier"].Add(location, path);
            }
        }

        if (node is LiteralExpressionSyntax literal && !IsRawString(literal.Token))
            AddEscapeCharacters(literal.Token, location, path, accumulators);
        if (node is InterpolatedStringTextSyntax interpolatedText)
            AddEscapeCharacters(interpolatedText.TextToken, location, path, accumulators);

        if (node is ExtensionBlockDeclarationSyntax)
            accumulators["extension_block"].Add(location, path);
        if (node is FieldExpressionSyntax)
            accumulators["field_backed_property"].Add(location, path);

        if (node is AssignmentExpressionSyntax assignment
            && (assignment.Left.DescendantNodesAndSelf().Any(child => child is ConditionalAccessExpressionSyntax)
                || assignment.Ancestors().Any(ancestor => ancestor is ConditionalAccessExpressionSyntax)))
        {
            accumulators["null_conditional_assignment"].Add(location, path);
        }

        if (node is InvocationExpressionSyntax invocation
            && invocation.Expression is IdentifierNameSyntax identifier
            && identifier.Identifier.ValueText == "nameof"
            && invocation.ArgumentList.Arguments
                .SelectMany(argument => argument.Expression.DescendantNodesAndSelf())
                .OfType<GenericNameSyntax>()
                .Any(genericName => genericName.IsUnboundGenericName))
        {
            accumulators["unbound_generic_nameof"].Add(location, path);
        }
    }

    private static void AddEscapeCharacters(
        SyntaxToken token,
        LanguageFeatureLocation location,
        string path,
        FeatureAccumulatorSet accumulators)
    {
        foreach (var _ in token.ValueText.Where(character => character == '\u001b'))
            accumulators["escape_character"].Add(location, path);
    }

    private static async Task ScanSemanticAsync(
        string project,
        string path,
        Document document,
        SyntaxNode root,
        FeatureAccumulatorSet accumulators,
        List<LanguageFeatureDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>().ToList();
        var effectiveLanguageVersion = document.Project.ParseOptions is CSharpParseOptions parseOptions
            ? LanguageVersionFacts.MapSpecifiedToEffectiveVersion(parseOptions.LanguageVersion)
            : LanguageVersion.Default;
        var classes = effectiveLanguageVersion >= LanguageVersion.CSharp12
            ? root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                .Where(PrimaryConstructorOpportunityDetector.HasPotentialShape)
                .ToList()
            : [];
        if (invocations.Count == 0 && classes.Count == 0)
            return;

        var semanticModel = await document.GetSemanticModelAsync(cancellationToken);
        if (semanticModel is null)
        {
            diagnostics.Add(new LanguageFeatureDiagnostic(
                "semantic_model_unavailable",
                "warning",
                $"Semantic analysis was unavailable for '{path}'.",
                project,
                path));
            return;
        }

        foreach (var invocation in invocations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var method = semanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol as IMethodSymbol;
            if (method?.ReducedFrom is null && method?.MethodKind != MethodKind.ReducedExtension)
                continue;

            accumulators["extension_method_invocation"].Add(
                CreateLocation(project, path, invocation.GetLocation()),
                path);
        }

        foreach (var declaration in classes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!PrimaryConstructorOpportunityDetector.TryGetCandidate(
                    declaration,
                    semanticModel,
                    cancellationToken,
                    out var constructor))
            {
                continue;
            }

            accumulators["primary_constructor"].AddOpportunity(
                CreateLocation(project, path, constructor.GetLocation()));
        }
    }

    private List<LanguageFeatureDefinition> SelectDefinitions(
        LanguageFeaturesRequest request,
        FeatureAccumulatorSet accumulators,
        List<LanguageFeatureDiagnostic> diagnostics)
    {
        IEnumerable<LanguageFeatureDefinition> selected = _definitions
            .Where(definition =>
                accumulators[definition.Id].Occurrences > 0
                || accumulators[definition.Id].Opportunities > 0);

        if (!string.IsNullOrWhiteSpace(request.Feature))
        {
            if (!_definitions.Any(definition =>
                    definition.Id.Equals(request.Feature, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new LanguageFeatureDiagnostic(
                    "unknown_feature",
                    "error",
                    $"Feature '{request.Feature}' is not in catalogue {LanguageFeatureCatalog.Version}."));
            }

            selected = selected.Where(definition =>
                definition.Id.Equals(request.Feature, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            if (!_definitions.Any(definition =>
                    definition.Category.Equals(request.Category, StringComparison.OrdinalIgnoreCase)))
            {
                diagnostics.Add(new LanguageFeatureDiagnostic(
                    "unknown_category",
                    "error",
                    $"Category '{request.Category}' is not in catalogue {LanguageFeatureCatalog.Version}."));
            }

            selected = selected.Where(definition =>
                definition.Category.Equals(request.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.IntroducedSince))
        {
            if (TryParseVersion(request.IntroducedSince, out var introducedSince))
            {
                selected = selected.Where(definition =>
                    TryParseVersion(definition.IntroducedIn, out var introducedIn)
                    && introducedIn >= introducedSince);
            }
            else
            {
                diagnostics.Add(new LanguageFeatureDiagnostic(
                    "invalid_version",
                    "error",
                    $"Introduced-since value '{request.IntroducedSince}' is not a C# version."));
                selected = [];
            }
        }

        if (request.RefactorableOnly)
        {
            selected = selected.Where(definition =>
                CreateRefactoring(definition.Id, accumulators[definition.Id], false) is not null);
        }

        selected = selected
            .OrderByDescending(definition => accumulators[definition.Id].Occurrences)
            .ThenBy(definition => definition.Id, StringComparer.Ordinal);

        if (request.Top is < 0)
        {
            diagnostics.Add(new LanguageFeatureDiagnostic(
                "invalid_top",
                "error",
                "Top must be zero or a positive integer."));
            return [];
        }

        if (request.Top is > 0)
            selected = selected.Take(request.Top.Value);

        return selected.ToList();
    }

    private void AddUnsupportedDetectorDiagnostics(
        LanguageFeaturesRequest request,
        List<LanguageFeatureDiagnostic> diagnostics)
    {
        if (request.RefactorableOnly)
            return;

        IEnumerable<LanguageFeatureDefinition> unsupported = _definitions
            .Where(definition => definition.DetectorKind == "unsupported");
        if (!string.IsNullOrWhiteSpace(request.Feature))
        {
            unsupported = unsupported.Where(definition =>
                definition.Id.Equals(request.Feature, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            unsupported = unsupported.Where(definition =>
                definition.Category.Equals(request.Category, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(request.IntroducedSince)
            && TryParseVersion(request.IntroducedSince, out var introducedSince))
        {
            unsupported = unsupported.Where(definition =>
                TryParseVersion(definition.IntroducedIn, out var introducedIn)
                && introducedIn >= introducedSince);
        }

        var ids = unsupported.Select(definition => definition.Id).Order(StringComparer.Ordinal).ToList();
        if (ids.Count == 0)
            return;

        diagnostics.Add(new LanguageFeatureDiagnostic(
            "unsupported_detector",
            "warning",
            "Pinned Roslyn 5.3 cannot reliably detect: " + string.Join(", ", ids)
            + ". These features are omitted, never reported as zero."));
    }

    private static LanguageFeatureResult CreateResult(
        LanguageFeatureDefinition definition,
        FeatureAccumulator accumulator,
        int files,
        int sourceLines,
        bool includeLocations) => new(
        definition.Id,
        definition.Name,
        definition.Category,
        definition.IntroducedIn,
        definition.DetectorKind,
        definition.CountingBasis,
        accumulator.Occurrences,
        accumulator.Files.Count,
        Rate(accumulator.Occurrences * 1000d, sourceLines),
        Rate(accumulator.Files.Count * 100d, files),
        CreateAdoption(definition.Id, accumulator, includeLocations),
        CreateRefactoring(definition.Id, accumulator, includeLocations),
        includeLocations ? SortLocations(accumulator.Locations) : null);

    private static LanguageFeatureProjectMetric CreateProjectMetric(
        string id,
        FeatureAccumulator accumulator,
        int files,
        int sourceLines,
        bool includeLocations) => new(
        id,
        accumulator.Occurrences,
        accumulator.Files.Count,
        Rate(accumulator.Occurrences * 1000d, sourceLines),
        Rate(accumulator.Files.Count * 100d, files),
        CreateAdoption(id, accumulator, includeLocations));

    private static LanguageFeatureAdoption? CreateAdoption(
        string id,
        FeatureAccumulator accumulator,
        bool includeLocations)
    {
        var label = id switch
        {
            "file_scoped_namespace" => "namespace declarations",
            "init_accessor" => "set or init accessors",
            "primary_constructor" =>
                "existing non-record primary constructors and conservatively eligible class constructor shapes",
            "switch_expression" => "switch constructs",
            _ => null
        };
        if (label is null || accumulator.Opportunities == 0)
            return null;

        return new LanguageFeatureAdoption(
            accumulator.Occurrences,
            accumulator.Opportunities,
            label,
            Rate(accumulator.Occurrences * 100d, accumulator.Opportunities),
            accumulator.ExceptionLocations.Count,
            includeLocations ? SortLocations(accumulator.ExceptionLocations) : null);
    }

    private static LanguageFeatureRefactoringOpportunity? CreateRefactoring(
        string id,
        FeatureAccumulator accumulator,
        bool includeLocations)
    {
        if (accumulator.ExceptionLocations.Count == 0)
            return null;

        var description = id switch
        {
            "file_scoped_namespace" =>
                "Review block-scoped namespace declarations for consistency; candidates are not proven safe for automatic conversion.",
            "init_accessor" =>
                "Review set accessors for init-only adoption; changing mutability requires API and usage analysis.",
            "primary_constructor" =>
                "Review moving public constructor parameters to the class header and direct assignments to readonly-field initializers; retain readonly fields. Do not replace field uses with captured primary parameters.",
            "switch_expression" =>
                "Review switch statements for expression form; control flow and side effects require semantic review.",
            _ => null
        };
        if (description is null)
            return null;

        return new LanguageFeatureRefactoringOpportunity(
            description,
            accumulator.ExceptionLocations.Count,
            false,
            null,
            includeLocations ? SortLocations(accumulator.ExceptionLocations) : null);
    }

    private static List<LanguageFeatureLocation> SortLocations(
        IEnumerable<LanguageFeatureLocation> locations) => locations
        .OrderBy(location => location.Project, StringComparer.Ordinal)
        .ThenBy(location => location.Path, StringComparer.Ordinal)
        .ThenBy(location => location.Line)
        .ThenBy(location => location.Column)
        .ToList();

    private static double Rate(double numerator, int denominator) => denominator == 0
        ? 0d
        : Math.Round(numerator / denominator, 3, MidpointRounding.AwayFromZero);

    private static bool TryParseVersion(string value, out decimal version)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith("C#", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[2..].Trim();
        if (!normalized.Contains('.', StringComparison.Ordinal))
            normalized += ".0";
        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out version);
    }

    private static bool IsRawString(SyntaxToken token) =>
        token.Kind().ToString().Contains("RawStringLiteralToken", StringComparison.Ordinal);

    private static int CountSourceLines(SourceText text) => text.Lines.Count(line =>
        !string.IsNullOrWhiteSpace(text.ToString(line.Span)));

    private static bool IsGenerated(string? filePath, SourceText text)
    {
        if (FilePathFilters.IsGeneratedOrIntermediate(filePath))
            return true;

        if (filePath?.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase) == true)
            return true;

        return text.ToString().Contains("<auto-generated", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(
        string? solutionDirectory,
        string? filePath,
        string fallbackName)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return fallbackName;
        if (string.IsNullOrWhiteSpace(solutionDirectory))
            return fallbackName;

        return Path.GetRelativePath(solutionDirectory, filePath).Replace('\\', '/');
    }

    private static void AddParseDiagnostics(
        string project,
        string path,
        SyntaxTree syntaxTree,
        List<LanguageFeatureDiagnostic> diagnostics)
    {
        foreach (var diagnostic in syntaxTree.GetDiagnostics()
                     .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            var lineSpan = diagnostic.Location.GetLineSpan();
            diagnostics.Add(new LanguageFeatureDiagnostic(
                "parse_error",
                "error",
                $"{diagnostic.Id}: {diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}",
                project,
                path,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.StartLinePosition.Character + 1));
        }
    }

    private static LanguageFeatureLocation CreateLocation(
        string project,
        string path,
        Location location)
    {
        var line = location.GetLineSpan().StartLinePosition;
        return new LanguageFeatureLocation(project, path, line.Line + 1, line.Character + 1);
    }

    private sealed record ProjectScan(
        string Name,
        string LanguageVersion,
        string EffectiveLanguageVersion,
        int Files,
        int NonBlankSourceLines,
        FeatureAccumulatorSet Accumulators);

    private sealed class FeatureAccumulatorSet
    {
        private readonly Dictionary<string, FeatureAccumulator> _values =
            LanguageFeatureCatalog.Definitions.ToDictionary(
                definition => definition.Id,
                _ => new FeatureAccumulator(),
                StringComparer.Ordinal);

        internal FeatureAccumulator this[string id] => _values[id];

        internal void Merge(FeatureAccumulatorSet other)
        {
            foreach (var definition in LanguageFeatureCatalog.Definitions)
                _values[definition.Id].Merge(other[definition.Id]);
        }
    }

    private sealed class FeatureAccumulator
    {
        internal int Occurrences { get; private set; }
        internal int Opportunities { get; private set; }
        internal HashSet<string> Files { get; } = new(StringComparer.Ordinal);
        internal List<LanguageFeatureLocation> Locations { get; } = [];
        internal List<LanguageFeatureLocation> ExceptionLocations { get; } = [];

        internal void Add(LanguageFeatureLocation location, string path)
        {
            Occurrences++;
            Files.Add(FileKey(location.Project, path));
            Locations.Add(location);
        }

        internal void AddProjectOption(
            LanguageFeatureLocation location,
            IEnumerable<string> affectedFiles)
        {
            Occurrences++;
            Files.UnionWith(affectedFiles.Select(path => FileKey(location.Project, path)));
            Locations.Add(location);
        }

        internal void AddOpportunity(LanguageFeatureLocation? exception = null)
        {
            Opportunities++;
            if (exception is not null)
                ExceptionLocations.Add(exception);
        }

        internal void Merge(FeatureAccumulator other)
        {
            Occurrences += other.Occurrences;
            Opportunities += other.Opportunities;
            Files.UnionWith(other.Files);
            Locations.AddRange(other.Locations);
            ExceptionLocations.AddRange(other.ExceptionLocations);
        }

        private static string FileKey(string project, string path) => project + "\0" + path;
    }
}
