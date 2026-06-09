using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Extracts classes from a method's source code.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ExtractClassService : IRtService
{
    private readonly ILogger<ExtractClassService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    private readonly SymbolDisplayFormat TypeDisplayFormat = new(
        SymbolDisplayGlobalNamespaceStyle.Omitted,
        SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        SymbolDisplayGenericsOptions.IncludeTypeParameters);
    private readonly IWorkspaceSessionFactory _sessionFactory;

    public ExtractClassService(ILogger<ExtractClassService> logger, ISolutionChangeWriter solutionChangeWriter, IWorkspaceSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    /// Runs the extraction process for a class from a method.
    /// </summary>
    internal async Task RunAsync(
        string typeName,
        string members,
        string newTypeName,
        bool isStatic,
        string? solutionPath,
        string? outputDir,
        bool dryRun)
    {
        var (workspace, solution) = await _sessionFactory.LoadWorkspaceAsync(solutionPath);
        using (workspace)
        {
            await ExecuteAsync(solution, typeName, members, newTypeName, isStatic, outputDir, dryRun);
        }
    }

    /// <summary>
    /// Executes the extraction process for a class from a method.
    /// </summary>
    internal async Task ExecuteAsync(
        Solution solution,
        string typeName,
        string members,
        string newTypeName,
        bool isStatic,
        string? outputDir,
        bool dryRun)
    {
        var originalSolution = solution;
        var target = await ResolveSourceTypeAsync(solution, typeName);
        if (target is null)
            return;

        var memberNames = members.Split(',')
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrEmpty(m))
            .ToHashSet(StringComparer.Ordinal);

        var selection = CollectSelectedMembers(target, memberNames);
        if (selection is null)
            return;

        var validationError = ValidateSelectedMembers(target, selection);
        if (validationError is not null)
        {
            ReportValidationError(validationError);
            return;
        }

        if (!isStatic)
        {
            var fieldName = DeriveFieldName(newTypeName);
            var paramName = fieldName.TrimStart('_');
            var preflight = new DependencyInjector(
                target.Symbol.Name,
                newTypeName,
                fieldName,
                paramName,
                target.SemanticModel,
                null,
                target.Symbol);

            preflight.Visit(target.Root);
            if (preflight.Status == DependencyInjectionStatus.Unsupported)
            {
                ReportValidationError(preflight.ValidationError ??
                                      $"Cannot inject '{newTypeName}' into '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
                return;
            }
        }

        var namespaceName = target.Symbol.ContainingNamespace?.IsGlobalNamespace == false
            ? target.Symbol.ContainingNamespace.ToDisplayString()
            : string.Empty;

        var membersToEmit = PromoteMembersReferencedFromOrigin(target, selection);
        var newClassText = BuildNewClassText(namespaceName, newTypeName, isStatic, membersToEmit);

        var updatedClassDecl =
            target.Declaration.RemoveNodes(selection.Declarations, SyntaxRemoveOptions.KeepNoTrivia)!;
        var updatedRoot = target.Root.ReplaceNode(target.Declaration, updatedClassDecl);
        solution = solution.WithDocumentSyntaxRoot(target.Document.Id, updatedRoot);

        if (!isStatic)
        {
            var updatedDocument = solution.GetDocument(target.Document.Id)
                                  ?? throw new InvalidOperationException("Updated source document could not be found.");
            var injectedRoot = await InjectExtractedTypeAsync(updatedDocument, target.Symbol, newTypeName);
            solution = solution.WithDocumentSyntaxRoot(target.Document.Id, injectedRoot);
        }

        var sourceDir = outputDir ?? Path.GetDirectoryName(target.Document.FilePath)!;
        var newFileName = newTypeName + ".cs";
        var newFilePath = Path.Combine(sourceDir, newFileName);
        var owningProject = solution.GetProject(target.Document.Project.Id)
                            ?? throw new InvalidOperationException("Cannot find project containing the class.");

        var newSourceText = SourceText.From(newClassText, Encoding.UTF8);
        var finalSolution = solution.AddDocument(
            DocumentId.CreateNewId(owningProject.Id),
            newFileName,
            newSourceText,
            filePath: newFilePath);

        _logger.RtInfo("Extracting '{MemberNames}' from '{TargetType}' into '{NewTypeName}'...",
            string.Join(", ", selection.Names.OrderBy(n => n, StringComparer.Ordinal)),
            target.Symbol.ToDisplayString(TypeDisplayFormat), newTypeName);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, finalSolution, dryRun);
    }

    /// <summary>
    /// Resolves the source type for a method.
    /// </summary>
    internal async Task<ResolvedSourceType?> ResolveSourceTypeAsync(Solution solution, string typeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        var isQualifiedName = normalizedTypeName.Contains('.', StringComparison.Ordinal);
        var matches = new List<ResolvedSourceType>();

        foreach (var project in solution.Projects)
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null) continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel is null) continue;

                foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                        continue;

                    if (!IsTypeMatch(symbol, normalizedTypeName, isQualifiedName))
                        continue;

                    matches.Add(new ResolvedSourceType(document, root, semanticModel, declaration, symbol));
                }
            }

        if (matches.Count == 0)
        {
            ReportValidationError($"Class '{typeName}' not found in solution.");
            return null;
        }

        if (matches.Count > 1)
        {
            var candidateList = string.Join(", ", matches
                .Select(m => m.Symbol.ToDisplayString(TypeDisplayFormat))
                .Distinct(StringComparer.Ordinal));
            ReportValidationError(
                $"Class '{typeName}' is ambiguous across the loaded solution: {candidateList}. Pass a fully qualified type name.");
            return null;
        }

        return matches[0];
    }

    /// <summary>
    /// Collects selected members based on resolved source type.
    /// </summary>
    internal SelectedMembers? CollectSelectedMembers(ResolvedSourceType target, HashSet<string> memberNames)
    {
        var declarations = new List<MemberDeclarationSyntax>();
        var symbols = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        var foundNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var member in target.Declaration.Members)
            switch (member)
            {
                case MethodDeclarationSyntax method when memberNames.Contains(method.Identifier.Text):
                    declarations.Add(method);
                    foundNames.Add(method.Identifier.Text);
                    AddDeclaredSymbol(symbols, target.SemanticModel.GetDeclaredSymbol(method));
                    break;

                case PropertyDeclarationSyntax property when memberNames.Contains(property.Identifier.Text):
                    declarations.Add(property);
                    foundNames.Add(property.Identifier.Text);
                    AddDeclaredSymbol(symbols, target.SemanticModel.GetDeclaredSymbol(property));
                    break;

                // A nested type named in --members moves wholesale (FB-362 defect 3).
                case BaseTypeDeclarationSyntax nestedType when memberNames.Contains(nestedType.Identifier.Text):
                    declarations.Add(nestedType);
                    foundNames.Add(nestedType.Identifier.Text);
                    AddDeclaredSymbol(symbols, target.SemanticModel.GetDeclaredSymbol(nestedType));
                    break;

                case FieldDeclarationSyntax field:
                    var selectedVariables = field.Declaration.Variables
                        .Where(v => memberNames.Contains(v.Identifier.Text))
                        .ToList();

                    if (selectedVariables.Count == 0)
                        break;

                    if (field.Declaration.Variables.Count > 1 &&
                        selectedVariables.Count != field.Declaration.Variables.Count)
                    {
                        ReportValidationError(
                            $"Cannot extract field '{selectedVariables[0].Identifier.Text}' from '{target.Symbol.ToDisplayString(TypeDisplayFormat)}' because declaration '{field.Declaration}' contains multiple variables. Split the declaration first.");
                        return null;
                    }

                    declarations.Add(field);
                    foreach (var variable in selectedVariables)
                    {
                        foundNames.Add(variable.Identifier.Text);
                        AddDeclaredSymbol(symbols, target.SemanticModel.GetDeclaredSymbol(variable));
                    }

                    break;
            }

        if (foundNames.Count == 0)
        {
            ReportValidationError(
                $"None of the specified members ({string.Join(", ", memberNames.OrderBy(n => n, StringComparer.Ordinal))}) were found in '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
            return null;
        }

        var missing = memberNames.Except(foundNames, StringComparer.Ordinal).OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
        {
            ReportValidationError(
                $"Cannot extract from '{target.Symbol.ToDisplayString(TypeDisplayFormat)}' because these members were not found: {string.Join(", ", missing)}.");
            return null;
        }

        return new SelectedMembers(declarations, symbols, foundNames);
    }

    /// <summary>
    /// Validates the selected members against the resolved source type.
    /// </summary>
    internal string? ValidateSelectedMembers(ResolvedSourceType target, SelectedMembers selection)
    {
        foreach (var member in selection.Declarations)
        {
            var bareThis = member.DescendantNodes()
                .OfType<ThisExpressionSyntax>()
                .FirstOrDefault(thisExpr => thisExpr.Parent is not MemberAccessExpressionSyntax
                {
                    Expression: ThisExpressionSyntax
                });

            if (bareThis is not null)
                return
                    $"Cannot extract '{GetMemberName(member)}' from '{target.Symbol.ToDisplayString(TypeDisplayFormat)}' because it uses 'this' directly. Keep it on the source type or rewrite it to avoid source-class instance state.";

            foreach (var reference in member.DescendantNodes().OfType<IdentifierNameSyntax>())
                if (TryFindUnsafeSourceMember(reference, target, selection.Symbols, out var unsafeMember))
                    return BuildUnsafeReferenceMessage(target, member, unsafeMember);

            foreach (var reference in member.DescendantNodes().OfType<MemberAccessExpressionSyntax>())
                if (TryFindUnsafeSourceMember(reference, target, selection.Symbols, out var unsafeMember))
                    return BuildUnsafeReferenceMessage(target, member, unsafeMember);
        }

        return null;
    }

    private bool TryFindUnsafeSourceMember(
        SyntaxNode referenceNode,
        ResolvedSourceType target,
        HashSet<ISymbol> extractedSymbols,
        out ISymbol unsafeMember)
    {
        unsafeMember = null!;

        var symbolInfo = target.SemanticModel.GetSymbolInfo(referenceNode);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        if (symbol is null)
            return false;

        symbol = symbol.OriginalDefinition;

        if (symbol is not IFieldSymbol and not IPropertySymbol and not IMethodSymbol and not IEventSymbol)
            return false;

        if (symbol.IsStatic || symbol.ContainingType is null)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(symbol.ContainingType, target.Symbol))
            return false;

        if (symbol is IMethodSymbol method && method.MethodKind == MethodKind.Constructor)
            return false;

        if (extractedSymbols.Contains(symbol))
            return false;

        unsafeMember = symbol;
        return true;
    }

    private string BuildUnsafeReferenceMessage(
        ResolvedSourceType target,
        MemberDeclarationSyntax member,
        ISymbol unsafeMember)
    {
        return
            $"Cannot extract '{GetMemberName(member)}' from '{target.Symbol.ToDisplayString(TypeDisplayFormat)}' because it references non-extracted instance member '{unsafeMember.Name}'. Extract that member too or keep '{GetMemberName(member)}' on the source type.";
    }

    /// <summary>
    /// Returns the selected member declarations to emit into the new type, promoting
    /// any moved member's <c>private</c> modifier to <c>internal</c> when a member
    /// that stays on the origin type still references it — otherwise the origin
    /// could not reach it after the move (FB-362 defect 2). Members referenced only
    /// by other moved members keep their original access; nothing is over-promoted.
    /// </summary>
    internal List<MemberDeclarationSyntax> PromoteMembersReferencedFromOrigin(
        ResolvedSourceType target,
        SelectedMembers selection)
    {
        // Symbols of selected members that a member remaining on the origin references.
        var referencedFromOrigin = new HashSet<ISymbol>(SymbolEqualityComparer.Default);
        foreach (var member in target.Declaration.Members)
        {
            if (selection.Declarations.Contains(member))
                continue;

            foreach (var reference in member.DescendantNodes().OfType<SimpleNameSyntax>())
            {
                var symbol = target.SemanticModel.GetSymbolInfo(reference).Symbol?.OriginalDefinition;
                if (symbol is not null && selection.Symbols.Contains(symbol))
                    referencedFromOrigin.Add(symbol);
            }
        }

        var result = new List<MemberDeclarationSyntax>(selection.Declarations.Count);
        foreach (var member in selection.Declarations)
        {
            var declaredSymbol = target.SemanticModel.GetDeclaredSymbol(member)?.OriginalDefinition;
            if (declaredSymbol is not null &&
                referencedFromOrigin.Contains(declaredSymbol) &&
                TryPromotePrivateToInternal(member, out var promoted))
            {
                result.Add(promoted);
            }
            else
            {
                result.Add(member);
            }
        }

        return result;
    }

    private bool TryPromotePrivateToInternal(MemberDeclarationSyntax member, out MemberDeclarationSyntax promoted)
    {
        var modifiers = member.Modifiers;
        var privateToken = modifiers.FirstOrDefault(m => m.IsKind(SyntaxKind.PrivateKeyword));
        if (privateToken == default)
        {
            promoted = member;
            return false;
        }

        var internalToken = SyntaxFactory.Token(
            privateToken.LeadingTrivia,
            SyntaxKind.InternalKeyword,
            privateToken.TrailingTrivia);
        promoted = member.WithModifiers(modifiers.Replace(privateToken, internalToken));
        return true;
    }

    private async Task<SyntaxNode> InjectExtractedTypeAsync(
        Document updatedDocument,
        INamedTypeSymbol targetSymbol,
        string newTypeName)
    {
        var updatedRoot = await updatedDocument.GetSyntaxRootAsync()
                          ?? throw new InvalidOperationException("Updated source syntax root could not be loaded.");
        var updatedSemanticModel = await updatedDocument.GetSemanticModelAsync()
                                   ?? throw new InvalidOperationException(
                                       "Updated source semantic model could not be loaded.");
        var targetDisplayName = targetSymbol.ToDisplayString(TypeDisplayFormat);
        var updatedTargetDeclaration = updatedRoot.DescendantNodes()
                                           .OfType<ClassDeclarationSyntax>()
                                           .FirstOrDefault(declaration =>
                                               updatedSemanticModel.GetDeclaredSymbol(declaration) is INamedTypeSymbol
                                                   candidate &&
                                               candidate.ToDisplayString(TypeDisplayFormat) == targetDisplayName)
                                       ?? throw new InvalidOperationException(
                                           $"Cannot locate '{targetDisplayName}' after extraction.");
        var updatedTargetSymbol = updatedSemanticModel.GetDeclaredSymbol(updatedTargetDeclaration) as INamedTypeSymbol
                                  ?? throw new InvalidOperationException(
                                      $"Cannot resolve '{targetDisplayName}' after extraction.");

        var fieldName = DeriveFieldName(newTypeName);
        var paramName = fieldName.TrimStart('_');
        var injector = new DependencyInjector(
            updatedTargetSymbol.Name,
            newTypeName,
            fieldName,
            paramName,
            updatedSemanticModel,
            null,
            updatedTargetSymbol);

        var injectedRoot = injector.Visit(updatedRoot);

        return injector.Status switch
        {
            DependencyInjectionStatus.Applied => injectedRoot!,
            DependencyInjectionStatus.AlreadyInjected => updatedRoot,
            DependencyInjectionStatus.Unsupported => throw new InvalidOperationException(
                injector.ValidationError ??
                $"Cannot inject '{newTypeName}' into '{updatedTargetSymbol.ToDisplayString(TypeDisplayFormat)}'."),
            _ => throw new InvalidOperationException(
                $"Cannot locate '{updatedTargetSymbol.ToDisplayString(TypeDisplayFormat)}' after extraction.")
        };
    }

    /// <summary>
    /// Builds new class text based on selected members and other parameters.
    /// Each member is emitted with its original token text intact (no per-line
    /// trimming), then the whole compilation unit is re-parsed and re-indented by
    /// the Roslyn formatter. This keeps nested block bodies correctly indented and
    /// leaves token interiors — e.g. verbatim string literals — byte-for-byte
    /// untouched, so the output is idempotent under `dotnet format` (FB-362).
    /// </summary>
    internal string BuildNewClassText(
        string namespaceName,
        string newTypeName,
        bool isStatic,
        List<MemberDeclarationSyntax> membersToExtract)
    {
        var sb = new StringBuilder();

        var hasNamespace = !string.IsNullOrEmpty(namespaceName);
        if (hasNamespace)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"namespace {namespaceName}");
            sb.AppendLine("{");
        }

        var staticKeyword = isStatic ? "static " : string.Empty;
        sb.AppendLine(CultureInfo.InvariantCulture, $"public {staticKeyword}class {newTypeName}");
        sb.AppendLine("{");

        foreach (var member in membersToExtract)
            sb.AppendLine(member.ToFullString().Trim());

        sb.AppendLine("}");
        if (hasNamespace)
            sb.AppendLine("}");

        using var workspace = new AdhocWorkspace();
        var root = CSharpSyntaxTree.ParseText(sb.ToString()).GetRoot();
        var formatted = Formatter.Format(root, workspace).ToFullString();
        return formatted.TrimEnd() + Environment.NewLine;
    }

    /// <summary>
    /// Determines if a given type symbol matches a specified string type.
    /// </summary>
    internal bool IsTypeMatch(INamedTypeSymbol symbol, string normalizedTypeName, bool isQualifiedName)
    {
        if (isQualifiedName)
            return NormalizeTypeName(symbol.ToDisplayString(TypeDisplayFormat)) == normalizedTypeName;

        return symbol.Name == normalizedTypeName;
    }

    /// <summary>
    /// Normalizes a given type name according to specified rules.
    /// </summary>
    internal string NormalizeTypeName(string typeName)
    {
        return typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();
    }

    private void AddDeclaredSymbol(HashSet<ISymbol> symbols, ISymbol? symbol)
    {
        if (symbol is not null)
            symbols.Add(symbol.OriginalDefinition);
    }

    /// <summary>
    /// Extracts the name of a member from a member declaration syntax.
    /// </summary>
    internal string GetMemberName(MemberDeclarationSyntax member)
    {
        return member switch
        {
            MethodDeclarationSyntax method => method.Identifier.Text,
            PropertyDeclarationSyntax property => property.Identifier.Text,
            BaseTypeDeclarationSyntax type => type.Identifier.Text,
            FieldDeclarationSyntax field => string.Join(", ",
                field.Declaration.Variables.Select(v => v.Identifier.Text)),
            _ => member.Kind().ToString()
        };
    }

    /// <summary>
    /// Reports an error based on the provided string input.
    /// </summary>
    internal void ReportValidationError(string message)
    {
        _logger.RtError("{Message}", message);
        Environment.ExitCode = 1;
    }

    /// <summary>
    /// Derives a field name from a given string.
    /// </summary>
    internal string DeriveFieldName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return "_dependency";

        var name = char.ToLower(typeName[0], CultureInfo.InvariantCulture) + typeName[1..];
        return "_" + name;
    }

    /// <summary>
    /// Returns the resolved source type of a given type symbol.
    /// </summary>
    internal sealed record ResolvedSourceType(
        Document Document,
        SyntaxNode Root,
        SemanticModel SemanticModel,
        ClassDeclarationSyntax Declaration,
        INamedTypeSymbol Symbol);

    /// <summary>
    /// Returns the selected members for a given type symbol.
    /// </summary>
    internal sealed record SelectedMembers(
        List<MemberDeclarationSyntax> Declarations,
        HashSet<ISymbol> Symbols,
        HashSet<string> Names);
}
