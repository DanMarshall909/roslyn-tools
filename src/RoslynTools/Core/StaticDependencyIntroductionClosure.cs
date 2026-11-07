using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Operations;

namespace RoslynTools.Core;

/// <summary>Original constructor/value closure and independently rebound candidate wiring.</summary>
internal sealed class StaticDependencyIntroductionClosure
{
    private readonly StaticDependencyPreparation _preparation;
    private readonly StaticDependencyIntroductionRequest _request;
    private readonly INamedTypeSymbol _receiverType;
    private readonly List<Creation> _creations = [];
    private readonly SyntaxAnnotation _typeAnnotation = new();
    private readonly SyntaxAnnotation _constructorAnnotation = new();
    private Document _document = null!;
    private ClassDeclarationSyntax _type = null!;
    private ConstructorDeclarationSyntax _constructor = null!;
    private IMethodSymbol _constructorSymbol = null!;

    internal StaticDependencyIntroductionClosure(StaticDependencyPreparation preparation,
        StaticDependencyIntroductionRequest request, INamedTypeSymbol receiverType)
    {
        _preparation = preparation;
        _request = request;
        _receiverType = receiverType;
    }

    internal string FieldName => _request.PreferredFieldName;

    internal async Task<IReadOnlyList<StaticDependencySite>> AnalyzeAsync(
        IReadOnlyList<IParameterSymbol> callers, CancellationToken cancellationToken)
    {
        var type = _preparation.Type;
        if (type.DeclaredAccessibility != Accessibility.Internal || !type.IsSealed || type.Arity != 0
            || type.ContainingType is not null || type.BaseType?.SpecialType != SpecialType.System_Object
            || type.Interfaces.Length != 0 || type.DeclaringSyntaxReferences.Length != 1 || type.GetAttributes().Length != 0
            || type.ContainingAssembly.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString()
                == "System.Runtime.CompilerServices.InternalsVisibleToAttribute"))
            return [Block("type-shape", "Only one internal sealed, nongeneric, top-level class without base types, interfaces or activation attributes is supported.")];
        var syntax = await type.DeclaringSyntaxReferences[0].GetSyntaxAsync(cancellationToken);
        if (syntax is not ClassDeclarationSyntax declaration || declaration.ParameterList is not null
            || declaration.BaseList is not null || declaration.Modifiers.Any(SyntaxKind.PartialKeyword))
            return [Block("declaration", "Partial and primary-constructor declarations are unsupported.")];
        _type = declaration;
        _document = _preparation.Original.GetDocument(declaration.SyntaxTree)!;
        if (_document is null || !HasUniquePath(_document))
            return [Block("document", "The selected class must have one authored, unambiguous document path.")];
        if (type.InstanceConstructors.Length != 1 || type.InstanceConstructors[0].IsImplicitlyDeclared
            || type.StaticConstructors.Length != 0)
            return [Block("constructors", "Exactly one explicitly declared instance constructor is supported.")];
        _constructorSymbol = type.InstanceConstructors[0];
        // Complete acquisition and the nonpartial source-class guards ensure one ordinary source constructor declaration.
        var constructorSyntax = await _constructorSymbol.DeclaringSyntaxReferences[0].GetSyntaxAsync(cancellationToken);
        if (constructorSyntax is not ConstructorDeclarationSyntax { Body: not null, Initializer: null } constructor
            || _constructorSymbol.IsVararg || _constructorSymbol.GetAttributes().Length != 0
            || _constructorSymbol.Parameters.Any(parameter => parameter.RefKind != RefKind.None || parameter.IsOptional || parameter.IsParams))
            return [Block("constructor-shape", "Only an ordinary block-bodied constructor without chaining, activation attributes or adapted parameters is supported.")];
        _constructor = constructor;
        if (_request.ExistingReceiverFieldDocumentationId is not null
            || !SyntaxFacts.IsValidIdentifier(FieldName) || !SyntaxFacts.IsValidIdentifier(_request.PreferredParameterName)
            || FieldName == _request.PreferredParameterName || type.GetMembers(FieldName).Length != 0
            || declaration.DescendantTokens().Any(token => token.IsKind(SyntaxKind.IdentifierToken) && token.ValueText == FieldName)
            || constructor.DescendantTokens().Any(token => token.IsKind(SyntaxKind.IdentifierToken)
                && token.ValueText == _request.PreferredParameterName))
            return [Block("names", "Use unused field and constructor-parameter identifiers; existing receiver reuse is outside this slice.")];

        var blockers = new List<StaticDependencySite>();
        // Inventory both explicit and target-typed constructions, including generated and
        // other-project contexts. Unsupported uses block rather than disappearing from closure.
        foreach (var project in _preparation.Original.Projects.OrderBy(project => project.FilePath, StringComparer.Ordinal))
        {
            var generated = await project.GetSourceGeneratedDocumentsAsync(cancellationToken);
            foreach (var document in project.Documents.Concat(generated).OrderBy(document => document.FilePath, StringComparer.Ordinal))
            {
                var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
                var model = (await document.GetSemanticModelAsync(cancellationToken))!;
                foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    if (model.GetSymbolInfo(name, cancellationToken).Symbol is IMethodSymbol method && IsReflectionBoundary(method)
                        || model.GetMemberGroup(name, cancellationToken).OfType<IMethodSymbol>().Any(IsReflectionBoundary))
                        blockers.Add(Block("reflection:" + document.FilePath + ":" + name.SpanStart,
                            "Reflection or runtime activation prevents a complete static constructor closure."));
                }
                foreach (var creation in root.DescendantNodes().OfType<BaseObjectCreationExpressionSyntax>())
                {
                    if (model.GetOperation(creation, cancellationToken) is not IObjectCreationOperation operation
                        || !SameSymbol(operation.Constructor?.ContainingType, type))
                        continue;
                    var methodSyntax = creation.Ancestors().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    var method = methodSyntax is null ? null : model.GetDeclaredSymbol(methodSyntax, cancellationToken);
                    var bindings = callers.Where(parameter => SameSymbol(parameter.ContainingSymbol, method)).ToArray();
                    if (creation is not ObjectCreationExpressionSyntax explicitCreation
                        || document is SourceGeneratedDocument || document.Project.Id != _preparation.Project.Id
                        || !HasUniquePath(document) || !SameSymbol(operation.Constructor, _constructorSymbol)
                        || method is null || method.IsGenericMethod || method.ContainingType is not { IsGenericType: false }
                        || method.MethodKind != MethodKind.Ordinary
                        || method.DeclaringSyntaxReferences.Length != 1 || method.GetAttributes().Length != 0
                        || creation.Ancestors().Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                        || bindings.Length != 1 || explicitCreation.Initializer is not null
                        || operation.Arguments.Any(argument => argument.IsImplicit)
                        || !IsConfinedLocal(explicitCreation, methodSyntax!, model, cancellationToken))
                    {
                        blockers.Add(Block("consumer:" + document.FilePath + ":" + creation.SpanStart,
                            "Every creation must be an explicitly bound ordinary caller's confined local; escaping, generated, external and unsupported consumers are refused."));
                        continue;
                    }
                    _creations.Add(new Creation(document, explicitCreation, operation, bindings[0], new SyntaxAnnotation()));
                }
            }
        }

        // Type references account for contexts that are not object-creation operations:
        // typeof/nameof, declarations, generic activation, DI and other escaped surfaces.
        var references = await SymbolFinder.FindReferencesAsync(type, _preparation.Original, cancellationToken: cancellationToken);
        foreach (var reference in references.SelectMany(reference => reference.Locations))
        {
            if (!_creations.Any(creation => creation.Document.Id == reference.Document.Id
                && (creation.Node.Type.Span.Contains(reference.Location.SourceSpan)
                    || creation.Node.Parent?.Parent?.Parent is VariableDeclarationSyntax local
                    && local.Type.Span.Contains(reference.Location.SourceSpan))))
                blockers.Add(Block("type-use:" + reference.Document.FilePath + ":" + reference.Location.SourceSpan.Start,
                    "A consumer type reference is outside the admitted explicit creation closure."));
        }
        if (_creations.Count == 0)
            blockers.Add(Block("callers", "At least one completely known, explicitly parameter-bound constructor caller is required."));
        if (callers.Any(caller => !_creations.Any(creation => SameSymbol(creation.Parameter, caller))))
            blockers.Add(Block("caller-binding", "Every declared caller parameter must own an admitted creation."));

        // A selected instance may not escape through its own implementation either.
        var typeModel = (await _document.GetSemanticModelAsync(cancellationToken))!;
        foreach (var deferred in _type.DescendantNodes()
                     .Where(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
        {
            var operation = typeModel.GetOperation(deferred, cancellationToken)!;
            if (operation.Descendants().OfType<IInstanceReferenceOperation>().Any(instance => instance.IsImplicit
                && instance.ReferenceKind == InstanceReferenceKind.ContainingTypeInstance && SameSymbol(instance.Type, type)))
            {
                var form = deferred is LocalFunctionStatementSyntax ? "local function" : "lambda";
                blockers.Add(Block("implicit-instance-capture:" + deferred.SpanStart,
                    "Implicit instance capture in a " + form + " is unsupported; keep consumer instance access outside deferred functions."));
            }
        }
        foreach (var self in _type.DescendantNodes().OfType<ThisExpressionSyntax>())
        {
            if (self.Parent is not MemberAccessExpressionSyntax member || member.Expression != self
                || typeModel.GetSymbolInfo(member, cancellationToken).Symbol is not IFieldSymbol)
                blockers.Add(Block("self-escape:" + self.SpanStart, "Explicit self values outside field access are unsupported."));
        }
        foreach (var name in _type.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            if (typeModel.GetSymbolInfo(name, cancellationToken).Symbol is not IMethodSymbol { IsStatic: false } method
                || !SameSymbol(method.ContainingType, type))
                continue;
            var expression = name.Parent is MemberAccessExpressionSyntax member && member.Name == name ? (SyntaxNode)member : name;
            if (expression.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != expression)
                blockers.Add(Block("method-escape:" + name.SpanStart, "Instance method groups can escape the consumer and are unsupported."));
        }
        foreach (var invocation in _type.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (typeModel.GetOperation(invocation, cancellationToken) is IInvocationOperation { Instance: IInstanceReferenceOperation } call
                && !SameSymbol(call.TargetMethod.ContainingType, type))
                blockers.Add(Block("inherited-instance:" + invocation.SpanStart,
                    "Inherited instance calls can expose runtime identity or clone the consumer and are unsupported."));
        }
        return blockers;
    }

    private bool IsConfinedLocal(ObjectCreationExpressionSyntax creation, MethodDeclarationSyntax method,
        SemanticModel model, CancellationToken cancellationToken)
    {
        if (creation.Parent is not EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax variable }
            || variable.Parent is not VariableDeclarationSyntax { Parent: LocalDeclarationStatementSyntax local } declaration
            || declaration.Variables.Count != 1 || !declaration.Type.IsVar
            || !local.UsingKeyword.IsKind(SyntaxKind.None) || !local.AwaitKeyword.IsKind(SyntaxKind.None)
            || model.GetDeclaredSymbol(variable, cancellationToken) is not ILocalSymbol symbol)
            return false;
        foreach (var identifier in method.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier, cancellationToken).Symbol, symbol))
                continue;
            if (identifier.Ancestors().Any(node => node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax)
                || identifier.Parent is not MemberAccessExpressionSyntax member || member.Expression != identifier
                || member.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != member
                || model.GetOperation(invocation, cancellationToken) is not IInvocationOperation call
                || !SameSymbol(call.TargetMethod.ContainingType, _preparation.Type)
                || call.TargetMethod.IsStatic || call.TargetMethod.IsVirtual || call.TargetMethod.IsGenericMethod
                || call.TargetMethod.ReturnsByRef || call.TargetMethod.ReturnsByRefReadonly
                || SameSymbol(call.TargetMethod.ReturnType, _preparation.Type))
                return false;
        }
        return true;
    }

    internal async Task<Solution> IntroduceAsync(Solution annotated, CancellationToken cancellationToken)
    {
        var candidate = annotated;
        foreach (var id in _creations.Select(creation => creation.Document.Id).Append(_document.Id).Distinct())
        {
            var root = (await candidate.GetDocument(id)!.GetSyntaxRootAsync(cancellationToken))!;
            var nodes = new Dictionary<SyntaxNode, Func<SyntaxNode, SyntaxNode>>();
            if (id == _document.Id)
            {
                var type = root.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(node => node.Span == _type.Span);
                var constructor = type.Members.OfType<ConstructorDeclarationSyntax>().Single(node => node.Span == _constructor.Span);
                nodes.Add(constructor, current =>
                {
                    var changed = (ConstructorDeclarationSyntax)current;
                    var parameter = SyntaxFactory.Parameter(SyntaxFactory.Identifier(_request.PreferredParameterName))
                        .WithType(SyntaxFactory.ParseTypeName(_receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                            .WithTrailingTrivia(SyntaxFactory.Space)).WithLeadingTrivia(SyntaxFactory.Space);
                    var assignment = SyntaxFactory.ParseStatement($"this.{FieldName} = {_request.PreferredParameterName};")
                        .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia("        "))
                        .WithTrailingTrivia(SyntaxFactory.LineFeed);
                    return changed.WithParameterList(changed.ParameterList.AddParameters(parameter))
                        .WithBody(changed.Body!.WithStatements(changed.Body!.Statements.Insert(0, assignment)))
                        .WithAdditionalAnnotations(_constructorAnnotation);
                });
                nodes.Add(type, current =>
                {
                    var field = SyntaxFactory.ParseMemberDeclaration(
                        $"private readonly {_receiverType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)} {FieldName};")!
                        .WithLeadingTrivia(SyntaxFactory.ParseLeadingTrivia("    ")).WithTrailingTrivia(SyntaxFactory.LineFeed);
                    return ((ClassDeclarationSyntax)current).WithMembers(((ClassDeclarationSyntax)current).Members.Insert(0, field))
                        .WithAdditionalAnnotations(_typeAnnotation);
                });
            }
            foreach (var creation in _creations.Where(creation => creation.Document.Id == id))
            {
                var node = root.DescendantNodes().OfType<ObjectCreationExpressionSyntax>().Single(node => node.Span == creation.Node.Span);
                nodes.Add(node, current =>
                {
                    var changed = (ObjectCreationExpressionSyntax)current;
                    var argument = SyntaxFactory.Argument(SyntaxFactory.ParseExpression(Escape(creation.Parameter.Name)))
                        .WithNameColon(SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(_request.PreferredParameterName))
                            .WithTrailingTrivia(SyntaxFactory.Space)).WithLeadingTrivia(SyntaxFactory.Space);
                    return changed.WithArgumentList(changed.ArgumentList!.AddArguments(argument))
                        .WithAdditionalAnnotations(creation.Annotation);
                });
            }
            root = root.ReplaceNodes(nodes.Keys, (before, current) => nodes[before](current));
            candidate = candidate.WithDocumentSyntaxRoot(id, root);
        }
        return candidate;
    }

    internal async Task<(IReadOnlyList<StaticDependencyClosureChange> Changes, IReadOnlyList<StaticDependencySite> Blockers)> VerifyAsync(
        Solution candidate, CancellationToken cancellationToken)
    {
        var blockers = new List<StaticDependencySite>();
        var changes = new List<StaticDependencyClosureChange>();
        var document = candidate.GetDocument(_document.Id)!;
        var root = (await document.GetSyntaxRootAsync(cancellationToken))!;
        var model = (await document.GetSemanticModelAsync(cancellationToken))!;
        var type = (ClassDeclarationSyntax)root.GetAnnotatedNodes(_typeAnnotation).Single();
        var constructor = (ConstructorDeclarationSyntax)root.GetAnnotatedNodes(_constructorAnnotation).Single();
        var typeSymbol = model.GetDeclaredSymbol(type, cancellationToken)!;
        var constructorSymbol = model.GetDeclaredSymbol(constructor, cancellationToken)!;
        var fields = typeSymbol.GetMembers(FieldName).OfType<IFieldSymbol>().ToArray();
        var receiver = fields.Length == 1 ? fields[0] : null;
        var parameter = constructorSymbol.Parameters[^1];
        if (receiver is not { IsReadOnly: true, IsStatic: false, DeclaredAccessibility: Accessibility.Private }
            || !SameSymbol(receiver.Type, _receiverType) || !SameSymbol(parameter.Type, _receiverType)
            || parameter.Name != _request.PreferredParameterName || parameter.Ordinal != _constructorSymbol.Parameters.Length
            || constructorSymbol.Parameters.Length != _constructorSymbol.Parameters.Length + 1
            || !_constructorSymbol.Parameters.Zip(constructorSymbol.Parameters).All(pair =>
                pair.First.Name == pair.Second.Name && pair.First.RefKind == pair.Second.RefKind && SameSymbol(pair.First.Type, pair.Second.Type))
            || constructor.Body!.Statements.Count != _constructor.Body!.Statements.Count + 1
            || model.GetOperation(constructor.Body!.Statements[0], cancellationToken) is not IExpressionStatementOperation
            {
                Operation: ISimpleAssignmentOperation
                {
                    Target: IFieldReferenceOperation { Instance: IInstanceReferenceOperation } target,
                    Value: IParameterReferenceOperation value
                }
            }
            || !SameSymbol(target.Field, receiver) || !SymbolEqualityComparer.Default.Equals(value.Parameter, parameter))
            blockers.Add(Block("wiring", "Candidate must store the appended reader parameter in the private readonly field before original body statements."));
        if (blockers.Count != 0)
            return ([], blockers);

        var constructorId = constructorSymbol.GetDocumentationCommentId()!;
        changes.Add(Change(StaticDependencyClosureChangeKind.Field, _document, _type, null,
            receiver!.GetDocumentationCommentId()!, "Introduce the private readonly reader field."));
        changes.Add(Change(StaticDependencyClosureChangeKind.ConstructorParameter, _document, _constructor,
            _constructorSymbol.GetDocumentationCommentId(), constructorId, "Append the explicitly typed reader parameter."));
        changes.Add(Change(StaticDependencyClosureChangeKind.ConstructorAssignment, _document, _constructor,
            _constructorSymbol.GetDocumentationCommentId(), constructorId, "Assign the reader before all original constructor body uses."));
        foreach (var creation in _creations)
        {
            var callerDocument = candidate.GetDocument(creation.Document.Id)!;
            var callerRoot = (await callerDocument.GetSyntaxRootAsync(cancellationToken))!;
            var callerModel = (await callerDocument.GetSemanticModelAsync(cancellationToken))!;
            var node = callerRoot.GetAnnotatedNodes(creation.Annotation).Single();
            var operation = callerModel.GetOperation(node, cancellationToken) as IObjectCreationOperation;
            if (operation is null || !SameSymbol(operation.Constructor, constructorSymbol)
                || operation.Arguments.Length != creation.Operation.Arguments.Length + 1
                || !creation.Operation.Arguments.Zip(operation.Arguments).All(pair =>
                    StaticDependencyMappingSemantics.SameArgumentBinding(pair.First, pair.Second))
                || operation.Arguments[^1] is not
                {
                    Parameter: { } destination, InConversion.IsIdentity: true,
                    Value: IParameterReferenceOperation supplied
                }
                || destination.Ordinal != parameter.Ordinal || !SameSymbol(supplied.Parameter, creation.Parameter))
                blockers.Add(Block("caller-binding:" + creation.Node.SpanStart,
                    "Candidate creation must preserve original ordered bindings and pass exactly the declared caller parameter."));
            changes.Add(Change(StaticDependencyClosureChangeKind.ConstructionArgument, creation.Document, creation.Node,
                _constructorSymbol.GetDocumentationCommentId(), constructorId, "Append the declared caller parameter after all original argument evaluations."));
        }
        return blockers.Count == 0 ? (changes, blockers) : ([], blockers);
    }

    private bool HasUniquePath(Document document) => document.FilePath is { } path && Path.IsPathFullyQualified(path)
        && _preparation.Original.Projects.SelectMany(project => project.Documents).Count(item => item.FilePath == path) == 1;

    private StaticDependencySite Block(string code, string message) =>
        StaticDependencyPreparation.CreateBlocker(_request.Selection, code, message);

    private static StaticDependencyClosureChange Change(StaticDependencyClosureChangeKind kind, Document document,
        SyntaxNode node, string? originalId, string candidateId, string detail) => new(
        $"closure:{kind}:{document.FilePath}:{node.SpanStart}", kind, document.Project.FilePath!, document.FilePath!,
        node.SpanStart, node.Span.Length, originalId, candidateId, detail);

    internal static bool SameSymbol(ISymbol? before, ISymbol? after) => before is not null && after is not null
        && before.Kind == after.Kind && before.MetadataName == after.MetadataName
        && before.GetDocumentationCommentId() == after.GetDocumentationCommentId()
        && before.ContainingAssembly?.Identity.ToString() == after.ContainingAssembly?.Identity.ToString()
        && (before is not IParameterSymbol parameter || after is IParameterSymbol changed
            && parameter.Ordinal == changed.Ordinal && SameSymbol(parameter.ContainingSymbol, changed.ContainingSymbol));

    private static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

    private static bool IsReflectionBoundary(IMethodSymbol method) =>
        method.ContainingNamespace.ToDisplayString().StartsWith("System.Reflection", StringComparison.Ordinal)
        || method.ContainingType.ToDisplayString() is "System.Activator" or "System.Type"
            or "System.Runtime.Serialization.FormatterServices" or "System.Runtime.CompilerServices.RuntimeHelpers";

    private sealed record Creation(Document Document, ObjectCreationExpressionSyntax Node,
        IObjectCreationOperation Operation, IParameterSymbol Parameter, SyntaxAnnotation Annotation);
}
