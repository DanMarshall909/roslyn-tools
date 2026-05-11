using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Services for static class analysis and processing.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance service used by the static-to-instance migration dogfood path.")]
internal sealed class DestaticClassService : IRtService
{
    private readonly IDocumentLocatorService _documents;
    private readonly ILogger<DestaticClassService> _logger;
    private readonly ISymbolResolutionService _symbols;
    private readonly ISolutionChangeWriter _writer;

    public DestaticClassService()
        : this(
            new RtConsoleLogger<DestaticClassService>(),
            new SymbolResolutionService(),
            new DocumentLocatorService(),
            new SolutionChangeWriter())
    {
    }

    public DestaticClassService(
        ISymbolResolutionService symbols,
        IDocumentLocatorService documents,
        ISolutionChangeWriter writer)
        : this(new RtConsoleLogger<DestaticClassService>(), symbols, documents, writer)
    {
    }

    public DestaticClassService(
        ILogger<DestaticClassService> logger,
        ISymbolResolutionService symbols,
        IDocumentLocatorService documents,
        ISolutionChangeWriter writer)
    {
        _logger = logger;
        _symbols = symbols;
        _documents = documents;
        _writer = writer;
    }

    /// <summary>
    /// Runs the static class analysis process.
    /// </summary>
    internal async Task RunAsync(Solution solution, string typeName, bool dryRun)
    {
        var originalSolution = solution;
        var symbol = await _symbols.FindTypeByFqnAsync(solution, typeName);

        if (symbol is not INamedTypeSymbol typeSymbol)
            throw new InvalidOperationException($"'{typeName}' is not a type.");

        if (!typeSymbol.IsStatic)
            throw new InvalidOperationException($"'{typeName}' is not a static class.");

        var classDeclaration = await _documents.GetDeclarationAsync<ClassDeclarationSyntax>(solution, typeSymbol);

        var declaringDocumentId = _documents.FindDocumentId(solution, typeSymbol)
                                  ?? throw new InvalidOperationException(
                                      $"Cannot locate source document for '{typeName}'.");

        solution = await RewriteCallSitesAsync(solution, typeSymbol);
        solution = await RewriteDeclarationAsync(solution, declaringDocumentId, classDeclaration);

        _logger.RtInfo("Migrated static class '{TypeName}' to instance class.", typeSymbol.ToDisplayString());
        await _writer.ApplyChangesAsync(originalSolution, solution, dryRun);
    }

    private static async Task<Solution> RewriteDeclarationAsync(
        Solution solution,
        DocumentId documentId,
        ClassDeclarationSyntax originalClass)
    {
        var document = solution.GetDocument(documentId)
                       ?? throw new InvalidOperationException("Cannot locate class document.");
        var root = await document.GetSyntaxRootAsync()
                   ?? throw new InvalidOperationException("Cannot read class syntax root.");

        var currentClass = root.FindNode(originalClass.Span) as ClassDeclarationSyntax
                           ?? throw new InvalidOperationException("Cannot locate class syntax node.");

        var updatedClass = currentClass.ReplaceNodes(
            currentClass.Members.OfType<BaseMethodDeclarationSyntax>()
                .Cast<SyntaxNode>()
                .Concat(currentClass.Members.OfType<PropertyDeclarationSyntax>())
                .Concat(currentClass.Members.OfType<FieldDeclarationSyntax>()),
            (_, rewritten) => RemoveMemberStaticModifier(rewritten));

        updatedClass = updatedClass
            .WithModifiers(RemoveStaticModifier(updatedClass.Modifiers))
            .AddAttributeLists(SyntaxFactory.AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(SyntaxFactory.ParseName("SuppressMessage"))
                        .WithArgumentList(SyntaxFactory.ParseAttributeArgumentList(
                            "(\"Performance\", \"CA1822:Mark members as static\", Justification = \"Instance class created by rt destatic-class migration.\")")))));

        var newRoot = root.ReplaceNode(currentClass, updatedClass);
        if (newRoot is CompilationUnitSyntax compilationUnit)
            newRoot = RoslynSyntax.EnsureUsing(compilationUnit, "System.Diagnostics.CodeAnalysis");

        return solution.WithDocumentSyntaxRoot(documentId, newRoot);
    }

    private static async Task<Solution> RewriteCallSitesAsync(Solution solution, INamedTypeSymbol typeSymbol)
    {
        foreach (var project in solution.Projects.ToArray())
            foreach (var document in project.Documents.ToArray())
            {
                var root = await document.GetSyntaxRootAsync();
                var semanticModel = await document.GetSemanticModelAsync();
                if (root is null || semanticModel is null)
                    continue;

                var rewriter = new StaticTypeCallRewriter(semanticModel, typeSymbol);
                var newRoot = rewriter.Visit(root);
                if (!rewriter.Changed || newRoot is null)
                    continue;

                solution = solution.WithDocumentSyntaxRoot(document.Id, newRoot);
            }

        return solution;
    }

    private static SyntaxTokenList RemoveStaticModifier(SyntaxTokenList modifiers)
    {
        return SyntaxFactory.TokenList(modifiers.Where(m => !m.IsKind(SyntaxKind.StaticKeyword)));
    }

    private static SyntaxNode RemoveMemberStaticModifier(SyntaxNode node)
    {
        return node switch
        {
            BaseMethodDeclarationSyntax method => method.WithModifiers(RemoveStaticModifier(method.Modifiers)),
            PropertyDeclarationSyntax property => property.WithModifiers(RemoveStaticModifier(property.Modifiers)),
            FieldDeclarationSyntax field => field.WithModifiers(RemoveStaticModifier(field.Modifiers)),
            _ => node
        };
    }

    private sealed class StaticTypeCallRewriter(SemanticModel semanticModel, INamedTypeSymbol typeSymbol)
        : CSharpSyntaxRewriter
    {
        private readonly Stack<ClassDeclarationSyntax> _classStack = new();
        private readonly string _fieldName = DeriveFieldName(typeSymbol.Name);

        private readonly string _targetTypeName =
            typeSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        private int _injectedReceiverRewrites;

        public bool Changed { get; private set; }

        public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            _classStack.Push(node);
            var previousInjectedReceiverRewrites = _injectedReceiverRewrites;
            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
            _classStack.Pop();

            if (_injectedReceiverRewrites == previousInjectedReceiverRewrites)
                return visited;

            return AddConstructorInjection(visited);
        }

        public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var visited = (MemberAccessExpressionSyntax)base.VisitMemberAccessExpression(node)!;
            var expressionSymbol = semanticModel.GetSymbolInfo(node.Expression).Symbol;
            if (expressionSymbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) != _targetTypeName)
                return visited;

            var receiver = CreateReceiver(node)
                .WithTriviaFrom(visited.Expression);

            Changed = true;
            return visited.WithExpression(receiver);
        }

        public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node)!;
            if (node.Expression is not SimpleNameSyntax simpleName)
                return visited;

            var symbol = semanticModel.GetSymbolInfo(simpleName).Symbol;
            if (symbol?.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) !=
                _targetTypeName)
                return visited;

            var enclosingType = semanticModel.GetEnclosingSymbol(node.SpanStart)?.ContainingType;
            if (enclosingType is not null
                && enclosingType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) == _targetTypeName)
                return visited;

            var receiver = CreateReceiver(node);
            Changed = true;
            return visited.WithExpression(SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                receiver,
                simpleName));
        }

        private ExpressionSyntax CreateReceiver(SyntaxNode node)
        {
            if (CanUseInjectedReceiver(node))
            {
                _injectedReceiverRewrites++;
                return SyntaxFactory.IdentifierName(_fieldName);
            }

            return SyntaxFactory.ParseExpression($"new {typeSymbol.Name}()");
        }

        private bool CanUseInjectedReceiver(SyntaxNode node)
        {
            if (!_classStack.TryPeek(out var classDeclaration)
                || classDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword))
                return false;

            foreach (var ancestor in node.Ancestors())
            {
                if (ReferenceEquals(ancestor, classDeclaration))
                    break;

                if (ancestor is BaseMethodDeclarationSyntax method
                    && method.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return false;

                if (ancestor is BasePropertyDeclarationSyntax property
                    && property.Modifiers.Any(SyntaxKind.StaticKeyword))
                    return false;

                if (ancestor is FieldDeclarationSyntax)
                    return false;

                if (ancestor is PropertyDeclarationSyntax propertyDeclaration
                    && propertyDeclaration.AccessorList is null)
                    return false;
            }

            return true;
        }

        private ClassDeclarationSyntax AddConstructorInjection(ClassDeclarationSyntax classDeclaration)
        {
            if (classDeclaration.Members.OfType<FieldDeclarationSyntax>()
                .Any(field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == _fieldName)))
                return classDeclaration;

            var field = SyntaxFactory.ParseMemberDeclaration(
                $"private readonly {typeSymbol.Name} {_fieldName};")!;

            var constructors = classDeclaration.Members
                .OfType<ConstructorDeclarationSyntax>()
                .Where(ctor => !ctor.Modifiers.Any(SyntaxKind.StaticKeyword))
                .ToArray();

            if (constructors.Length == 0)
            {
                var constructor = SyntaxFactory.ParseMemberDeclaration(
                    $"public {classDeclaration.Identifier.ValueText}({typeSymbol.Name} {ToParameterName(typeSymbol.Name)})\r\n"
                    + "{\r\n"
                    + $"    {_fieldName} = {ToParameterName(typeSymbol.Name)};\r\n"
                    + "}")!;

                return classDeclaration.WithMembers(classDeclaration.Members.InsertRange(0, [field, constructor]));
            }

            var parameterName = ToParameterName(typeSymbol.Name);
            var rewrittenMembers = classDeclaration.Members
                .Select(member => member is ConstructorDeclarationSyntax constructor
                                  && !constructor.Modifiers.Any(SyntaxKind.StaticKeyword)
                    ? AddConstructorParameter(constructor, parameterName)
                    : member);

            var updatedClass = classDeclaration.WithMembers(SyntaxFactory.List(rewrittenMembers));

            return updatedClass.WithMembers(updatedClass.Members.Insert(0, field));
        }

        private ConstructorDeclarationSyntax AddConstructorParameter(
            ConstructorDeclarationSyntax constructor,
            string parameterName)
        {
            if (constructor.ParameterList.Parameters.Any(parameter => parameter.Identifier.ValueText == parameterName))
                return constructor;

            var parameter = SyntaxFactory.ParseParameterList($"({typeSymbol.Name} {parameterName})")
                .Parameters[0];

            var assignment = SyntaxFactory.ParseStatement($"{_fieldName} = {parameterName};");
            var body = constructor.Body ?? SyntaxFactory.Block();

            return constructor
                .WithParameterList(constructor.ParameterList.AddParameters(parameter))
                .WithBody(body.WithStatements(body.Statements.Insert(0, assignment)));
        }

        private static string DeriveFieldName(string typeName)
        {
            return "_" + ToParameterName(typeName);
        }

        private static string ToParameterName(string typeName)
        {
            return string.IsNullOrEmpty(typeName)
                ? "dependency"
                : char.ToLowerInvariant(typeName[0]) + typeName[1..];
        }
    }
}
