using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// A service for transforming parameters in the Roslyn tooling ecosystem
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
/// <summary>
/// Core logic for the <c>transform-parameters</c> command.
/// Generates a new <c>record ParamObj(...)</c> in the same file as the target method,
/// rewrites the method signature to accept one <c>ParamObj args</c> parameter,
/// and updates every call site from <c>Foo(a, b, c)</c> to <c>Foo(new ParamObj(a, b, c))</c>.
/// Also supports reusing an existing type via <c>--existing-type</c>.
/// </summary>
internal class TransformParametersService : IRtService
{
    private readonly ILogger<TransformParametersService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public TransformParametersService()
        : this(NullLogger<TransformParametersService>.Instance, new SolutionChangeWriter())
    {
    }

    public TransformParametersService(ILogger<TransformParametersService> logger,
        ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }

    /// <summary>
    ///     Entry point called by <see cref="RoslynTools.Commands.TransformParametersCommand" />.
    ///     Exactly one of <paramref name="newTypeName" /> or <paramref name="existingTypeFqn" /> must be non-null.
    /// </summary>
    internal async Task RunAsync(
        Solution sln,
        string methodSpec,
        string? newTypeName,
        string? existingTypeFqn,
        bool dryRun)
    {
        // Mutual-exclusion guard
        if (newTypeName is not null && existingTypeFqn is not null)
            throw new InvalidOperationException(
                "--new-type and --existing-type are mutually exclusive. Specify only one.");
        if (newTypeName is null && existingTypeFqn is null)
            throw new InvalidOperationException(
                "You must specify either --new-type (to generate a new record) or --existing-type (to reuse an existing type).");

        var prepared = await PrepareAsync(sln, methodSpec, newTypeName, existingTypeFqn);
        await _solutionChangeWriter.ApplyChangesAsync(sln, prepared, dryRun);
    }

    internal async Task<Solution> PrepareAsync(Solution sln, string methodSpec, string? newTypeName, string? existingTypeFqn, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (newTypeName is not null && existingTypeFqn is not null)
            throw new InvalidOperationException("--new-type and --existing-type are mutually exclusive. Specify only one.");
        if (newTypeName is null && existingTypeFqn is null)
            throw new InvalidOperationException("You must specify either --new-type (to generate a new record) or --existing-type (to reuse an existing type).");
        var prepared = existingTypeFqn is not null
            ? await PrepareExistingTypeAsync(sln, methodSpec, existingTypeFqn)
            : await PrepareNewTypeAsync(sln, methodSpec, newTypeName!);
        cancellationToken.ThrowIfCancellationRequested();
        return prepared;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --new-type code path (original behaviour)
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Solution> PrepareNewTypeAsync(
        Solution sln,
        string methodSpec,
        string newTypeName)
    {
        _logger.RtInfo("Transforming parameters of '{MethodSpec}' → record '{NewTypeName}'...", methodSpec,
            newTypeName);
        var (methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);

        var unsafeParams = CollectUnsafeParameters(methodSymbol);
        if (unsafeParams.Count > 0)
        {
            var names = string.Join(", ", unsafeParams);
            throw new InvalidOperationException(
                $"Cannot transform: parameter(s) '{names}' use out/ref/in modifiers which are not supported. " +
                "Remove or exclude those parameters before running this command.");
        }

        if (methodSymbol.Parameters.Length == 0)
            throw new InvalidOperationException(
                $"Method '{methodSpec}' has no parameters to transform.");

        // Rewrite call sites FIRST while spans are still valid against the original solution.
        sln = await RewriteCallSitesAsync(sln, methodSymbol, newTypeName, true);

        // Insert record declaration into the method's file.
        // doc still valid — call-site rewrite only touches call-site documents.
        sln = await InsertRecordDeclarationAsync(sln, doc, methodSymbol, newTypeName);

        // Re-resolve method symbol after syntax tree was modified to get fresh syntax reference.
        (methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);
        sln = await RewriteMethodSignatureAsync(sln, doc, methodSymbol, newTypeName);

        return sln;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  --existing-type code path
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Solution> PrepareExistingTypeAsync(
        Solution sln,
        string methodSpec,
        string existingTypeFqn)
    {
        _logger.RtInfo("Transforming parameters of '{MethodSpec}' → existing type '{ExistingTypeFqn}'...", methodSpec,
            existingTypeFqn);
        var (methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);

        var unsafeParams = CollectUnsafeParameters(methodSymbol);
        if (unsafeParams.Count > 0)
        {
            var names = string.Join(", ", unsafeParams);
            throw new InvalidOperationException(
                $"Cannot transform: parameter(s) '{names}' use out/ref/in modifiers which are not supported.");
        }

        if (methodSymbol.Parameters.Length == 0)
            throw new InvalidOperationException(
                $"Method '{methodSpec}' has no parameters to transform.");

        var existingType = await FindExistingTypeAsync(sln, existingTypeFqn);
        ValidateExistingTypeMembers(methodSymbol, existingType);

        var isRecord = existingType.IsRecord;
        var shortName = existingType.Name;

        sln = await RewriteCallSitesAsync(sln, methodSymbol, shortName, isRecord);

        (methodSymbol, doc) = await FindMethodAsync(sln, methodSpec);
        sln = await RewriteMethodSignatureAsync(sln, doc, methodSymbol, shortName);

        return sln;
    }

    private async Task<INamedTypeSymbol> FindExistingTypeAsync(Solution sln, string fqn)
    {
        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            var symbol = compilation.GetTypeByMetadataName(fqn);
            if (symbol != null)
                return symbol;
        }

        throw new InvalidOperationException(
            $"Existing type '{fqn}' not found in the solution. " +
            "Ensure you are using the fully-qualified name (e.g. MyApp.Services.PlaceOrderArgs).");
    }

    private void ValidateExistingTypeMembers(IMethodSymbol method, INamedTypeSymbol existingType)
    {
        var missing = new List<string>();

        if (existingType.IsRecord)
        {
            // For records: match against primary constructor parameters
            var ctorParams = existingType.InstanceConstructors
                                 .Where(c => c.Parameters.Length > 0)
                                 .OrderByDescending(c => c.Parameters.Length)
                                 .FirstOrDefault()?.Parameters
                             ?? ImmutableArray<IParameterSymbol>.Empty;

            var paramNames = ctorParams.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var param in method.Parameters)
            {
                var capitalized = Capitalize(param.Name);
                if (!paramNames.Contains(param.Name) && !paramNames.Contains(capitalized))
                    missing.Add(param.Name);
            }
        }
        else
        {
            // For classes: match against public properties and fields
            var memberNames = existingType.GetMembers()
                .Where(m => m.DeclaredAccessibility == Accessibility.Public &&
                            (m is IPropertySymbol || m is IFieldSymbol))
                .Select(m => m.Name)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var param in method.Parameters)
            {
                var capitalized = Capitalize(param.Name);
                if (!memberNames.Contains(param.Name) && !memberNames.Contains(capitalized))
                    missing.Add(param.Name);
            }
        }

        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"Existing type '{existingType.Name}' is missing members for parameter(s): " +
                string.Join(", ", missing) + ". " +
                "Every method parameter must have a matching named property/field (class) or record parameter.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Symbol resolution
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<(IMethodSymbol Symbol, Document Doc)>
        FindMethodAsync(Solution sln, string methodSpec)
    {
        var dotIdx = methodSpec.LastIndexOf('.');
        if (dotIdx <= 0)
            throw new InvalidOperationException(
                $"Method spec must be 'TypeName.MethodName', got: '{methodSpec}'");

        var typeName = methodSpec[..dotIdx];
        var methodName = methodSpec[(dotIdx + 1)..];

        foreach (var project in sln.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) continue;

            foreach (var typeSymbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type)
                         .OfType<INamedTypeSymbol>())
            {
                var method = typeSymbol.GetMembers(methodName)
                    .OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary);

                if (method == null) continue;

                var syntaxRef = method.DeclaringSyntaxReferences.FirstOrDefault();
                if (syntaxRef == null) continue;

                var doc = sln.GetDocument(syntaxRef.SyntaxTree)
                          ?? throw new InvalidOperationException(
                              $"Cannot find document for method '{methodSpec}'.");

                return (method, doc);
            }
        }

        throw new InvalidOperationException(
            $"Method '{methodSpec}' not found in solution.");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Validation
    // ──────────────────────────────────────────────────────────────────────────

    private List<string> CollectUnsafeParameters(IMethodSymbol method)
    {
        return method.Parameters
            .Where(p => p.RefKind != RefKind.None)
            .Select(p => p.Name)
            .ToList();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 1: Insert record declaration into the same file as the method
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Solution> InsertRecordDeclarationAsync(
        Solution sln,
        Document doc,
        IMethodSymbol method,
        string newTypeName)
    {
        var root = await doc.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("Cannot get syntax root.");

        var methodSyntax = (MethodDeclarationSyntax)await method.DeclaringSyntaxReferences[0].GetSyntaxAsync();
        var containingType = methodSyntax.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault()
                             ?? throw new InvalidOperationException("Method is not inside a type declaration.");

        var recordText = BuildRecordText(method.Parameters, newTypeName);
        var recordDecl = (RecordDeclarationSyntax)(SyntaxFactory.ParseMemberDeclaration(recordText)
                                                   ?? throw new InvalidOperationException(
                                                       "Failed to parse generated record declaration."));

        // Place the record just before the containing type's closing brace, as a sibling
        // at the namespace/file level — after the containing type
        var newRoot = InsertAfterType(root, containingType, recordDecl);
        return sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    private string BuildRecordText(
        IReadOnlyList<IParameterSymbol> parameters,
        string newTypeName)
    {
        var paramParts = parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var defaultPart = p.HasExplicitDefaultValue
                ? " = " + FormatDefaultValue(p)
                : string.Empty;
            return $"{typeName} {Capitalize(p.Name)}{defaultPart}";
        });

        return $"internal sealed record {newTypeName}({string.Join(", ", paramParts)});";
    }

    private string FormatDefaultValue(IParameterSymbol param)
    {
        if (param.ExplicitDefaultValue == null)
            return "null";
        if (param.ExplicitDefaultValue is string s)
            return $"\"{s}\"";
        if (param.ExplicitDefaultValue is bool b)
            return b ? "true" : "false";
        if (param.ExplicitDefaultValue is char c)
            return $"'{c}'";
        return param.ExplicitDefaultValue.ToString() ?? "default";
    }

    private SyntaxNode InsertAfterType(
        SyntaxNode root,
        TypeDeclarationSyntax containingType,
        RecordDeclarationSyntax recordDecl)
    {
        // Find the parent — could be a namespace or the compilation unit
        var parent = containingType.Parent;

        if (parent is BaseNamespaceDeclarationSyntax nsDecl)
        {
            var members = nsDecl.Members.ToList();
            var idx = members.IndexOf(containingType);
            if (idx < 0)
                throw new InvalidOperationException("Cannot locate containing type in namespace members.");

            var formattedRecord = recordDecl
                .WithLeadingTrivia(SyntaxFactory.LineFeed, SyntaxFactory.Whitespace(MemberIndent(containingType)));

            members.Insert(idx + 1, formattedRecord);
            var newNs = nsDecl.WithMembers(SyntaxFactory.List(members));
            return root.ReplaceNode(nsDecl, newNs);
        }

        if (parent is CompilationUnitSyntax compilationUnit)
        {
            var members = compilationUnit.Members.ToList();
            var idx = members.IndexOf(containingType);
            if (idx < 0)
                throw new InvalidOperationException("Cannot locate containing type in compilation unit.");

            var formattedRecord = recordDecl
                .WithLeadingTrivia(SyntaxFactory.LineFeed, SyntaxFactory.Whitespace(MemberIndent(containingType)));

            members.Insert(idx + 1, formattedRecord);
            var newCu = compilationUnit.WithMembers(SyntaxFactory.List(members));
            return root.ReplaceNode(compilationUnit, newCu);
        }

        throw new InvalidOperationException(
            $"Unexpected containing scope type: {parent?.GetType().Name ?? "null"}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 2: Rewrite method signature
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Solution> RewriteMethodSignatureAsync(
        Solution sln,
        Document doc,
        IMethodSymbol method,
        string newTypeName)
    {
        var root = await doc.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("Cannot get syntax root.");

        var methodSyntax = (MethodDeclarationSyntax)await method.DeclaringSyntaxReferences[0].GetSyntaxAsync();

        // Find the node in the (potentially updated) root by location
        var targetNode = root.FindNode(methodSyntax.Span) as MethodDeclarationSyntax
                         ?? throw new InvalidOperationException("Cannot locate method syntax in updated document.");

        var newParam = SyntaxFactory.Parameter(
                SyntaxFactory.Identifier("args"))
            .WithType(SyntaxFactory.ParseTypeName(newTypeName + " "));

        var newParamList = SyntaxFactory.ParameterList(
            SyntaxFactory.SingletonSeparatedList(newParam));

        var newMethod = targetNode.WithParameterList(newParamList);
        var newRoot = root.ReplaceNode(targetNode, newMethod);
        return sln.WithDocumentSyntaxRoot(doc.Id, newRoot);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Step 3: Rewrite call sites
    // ──────────────────────────────────────────────────────────────────────────

    private async Task<Solution> RewriteCallSitesAsync(
        Solution sln,
        IMethodSymbol method,
        string typeName,
        bool isRecord)
    {
        var references = await SymbolFinder.FindReferencesAsync(method, sln);

        // Collect all source locations and group by document ID
        var locationsByDocId = new Dictionary<DocumentId, List<Location>>();
        foreach (var refSymbol in references)
            foreach (var loc in refSymbol.Locations)
            {
                if (!loc.Location.IsInSource) continue;
                var docId = loc.Document.Id;
                if (!locationsByDocId.TryGetValue(docId, out var locs))
                {
                    locs = new List<Location>();
                    locationsByDocId[docId] = locs;
                }

                locs.Add(loc.Location);
            }

        // Capture method parameters for class-style object initializer
        var methodParams = method.Parameters;

        foreach (var (docId, locations) in locationsByDocId)
        {
            var doc = sln.GetDocument(docId);
            if (doc == null) continue;

            var root = await doc.GetSyntaxRootAsync();
            if (root == null) continue;

            var invocations = new List<InvocationExpressionSyntax>();
            foreach (var loc in locations)
            {
                var node = root.FindNode(loc.SourceSpan);
                var inv = node.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
                if (inv != null)
                    invocations.Add(inv);
            }

            if (invocations.Count == 0) continue;

            var newRoot = root.ReplaceNodes(
                invocations,
                (original, _) => RewriteInvocation(original, typeName, isRecord, methodParams));

            sln = sln.WithDocumentSyntaxRoot(docId, newRoot);
        }

        return sln;
    }

    private InvocationExpressionSyntax RewriteInvocation(
        InvocationExpressionSyntax original,
        string typeName,
        bool isRecord,
        ImmutableArray<IParameterSymbol> methodParams)
    {
        var args = original.ArgumentList.Arguments;
        var newKeyword = SyntaxFactory.Token(SyntaxKind.NewKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);

        ExpressionSyntax newObjectCreation;
        if (isRecord)
        {
            // Record: new ParamObj(arg1, arg2, arg3)
            newObjectCreation = SyntaxFactory.ObjectCreationExpression(
                newKeyword,
                SyntaxFactory.IdentifierName(typeName),
                SyntaxFactory.ArgumentList(args),
                null);
        }
        else
        {
            // Class: new ExistingType { PropA = arg1, PropB = arg2 }
            var initAssignments = args
                .Select((arg, i) =>
                {
                    var propName = i < methodParams.Length
                        ? Capitalize(methodParams[i].Name)
                        : $"Arg{i}";
                    // Build  PropName = value  with spaces around the equals token.
                    var lhs = SyntaxFactory.IdentifierName(propName)
                        .WithTrailingTrivia(SyntaxFactory.Space);
                    var rhs = arg.Expression
                        .WithLeadingTrivia(SyntaxFactory.Space);
                    return (ExpressionSyntax)SyntaxFactory.AssignmentExpression(
                        SyntaxKind.SimpleAssignmentExpression,
                        lhs,
                        rhs);
                })
                .ToArray();

            var initExpressions = SyntaxFactory.SeparatedList(initAssignments);
            var initializer = SyntaxFactory.InitializerExpression(
                    SyntaxKind.ObjectInitializerExpression,
                    initExpressions)
                .WithLeadingTrivia(SyntaxFactory.Space);

            newObjectCreation = SyntaxFactory.ObjectCreationExpression(
                newKeyword,
                SyntaxFactory.IdentifierName(typeName),
                null,
                initializer);
        }

        var newArgs = SyntaxFactory.ArgumentList(
            SyntaxFactory.SingletonSeparatedList(
                SyntaxFactory.Argument(newObjectCreation)));

        return original.WithArgumentList(newArgs);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private string Capitalize(string name)
    {
        return name.Length == 0 ? name : char.ToUpperInvariant(name[0]) + name[1..];
    }

    private string MemberIndent(TypeDeclarationSyntax type)
    {
        var ws = type.GetLeadingTrivia()
            .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        return ws == default ? string.Empty : ws.ToString();
    }
}
