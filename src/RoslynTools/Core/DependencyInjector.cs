using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// DependencyInjectionStatus
/// </summary>
internal enum DependencyInjectionStatus
{
    ClassNotFound,
    AlreadyInjected,
    Applied,
    Unsupported
}

/// <summary>
///     Rewrites a target class to inject a new dependency: adds a private readonly field,
///     and either creates a constructor or updates existing constructors safely.
/// </summary>
internal sealed class DependencyInjector : CSharpSyntaxRewriter
{
    private readonly string _depType;
    private readonly ITypeSymbol? _depTypeSymbol;
    private readonly string _fieldName;
    private readonly string _paramName;
    private readonly SemanticModel? _semanticModel;
    private readonly string _targetClass;
    private readonly INamedTypeSymbol? _targetTypeSymbol;

    public DependencyInjector(
        string targetClass,
        string depType,
        string fieldName,
        string paramName,
        SemanticModel? semanticModel = null,
        ITypeSymbol? dependencyTypeSymbol = null,
        INamedTypeSymbol? targetTypeSymbol = null)
    {
        _targetClass = targetClass;
        _targetTypeSymbol = targetTypeSymbol;
        _depType = depType;
        _depTypeSymbol = dependencyTypeSymbol;
        _fieldName = fieldName;
        _paramName = paramName;
        _semanticModel = semanticModel;
    }

    public DependencyInjectionStatus Status { get; private set; } = DependencyInjectionStatus.ClassNotFound;

    public bool Applied => Status == DependencyInjectionStatus.Applied;

    public string? ValidationError { get; private set; }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (!IsTargetClass(node))
            return base.VisitClassDeclaration(node);

        var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;
        var fieldExists = visited.Members.OfType<FieldDeclarationSyntax>()
            .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == _fieldName));

        var constructors = visited.Members.OfType<ConstructorDeclarationSyntax>().ToList();
        var replacementMap = new Dictionary<ConstructorDeclarationSyntax, ConstructorDeclarationSyntax>();

        foreach (var ctor in constructors)
        {
            if (ctor.Initializer?.IsKind(SyntaxKind.ThisConstructorInitializer) == true)
                continue;

            var matchingParameter = FindMatchingParameter(ctor);
            var hasAssignment = matchingParameter is not null &&
                                HasFieldAssignment(ctor, matchingParameter.Identifier.Text);

            if (ctor.ExpressionBody is not null && (matchingParameter is null || !hasAssignment))
            {
                Status = DependencyInjectionStatus.Unsupported;
                ValidationError =
                    $"Constructor '{ctor.Identifier.Text}' uses expression-bodied syntax. Re-run after converting it to a block-bodied constructor so '{_fieldName}' can be assigned safely.";
                return visited;
            }

            if (matchingParameter is null)
            {
                var rewritten = AddParameterAndAssignment(ctor);
                replacementMap.Add(ctor, rewritten);
                continue;
            }

            if (!hasAssignment)
            {
                var rewritten = AddAssignment(ctor, matchingParameter.Identifier.Text);
                replacementMap.Add(ctor, rewritten);
            }
        }

        var needsNewConstructor = constructors.Count == 0;
        var needsField = !fieldExists;
        var needsConstructorUpdates = replacementMap.Count > 0;

        if (!needsField && !needsNewConstructor && !needsConstructorUpdates)
        {
            Status = DependencyInjectionStatus.AlreadyInjected;
            return visited;
        }

        if (needsConstructorUpdates)
            visited = visited.ReplaceNodes(
                replacementMap.Keys,
                (original, _) => replacementMap[original]);

        var members = visited.Members;
        var fieldInsertIndex = GetFieldInsertIndex(members);

        if (needsField)
        {
            var fieldDecl = SyntaxFactory.ParseMemberDeclaration(
                $"    private readonly {_depType} {_fieldName};{Environment.NewLine}")!;
            members = members.Insert(fieldInsertIndex, fieldDecl);
            visited = visited.WithMembers(members);
        }

        if (needsNewConstructor)
        {
            var ctorText =
                $"    public {visited.Identifier.Text}({_depType} {_paramName}){Environment.NewLine}" +
                $"    {{{Environment.NewLine}" +
                $"        {_fieldName} = {_paramName};{Environment.NewLine}" +
                $"    }}{Environment.NewLine}";
            var ctorDecl = SyntaxFactory.ParseMemberDeclaration(ctorText)!;
            var ctorInsertIndex = needsField ? fieldInsertIndex + 1 : fieldInsertIndex;
            visited = visited.WithMembers(visited.Members.Insert(ctorInsertIndex, ctorDecl));
        }

        Status = DependencyInjectionStatus.Applied;
        return visited;
    }

    private bool IsTargetClass(ClassDeclarationSyntax node)
    {
        if (_targetTypeSymbol is null)
            return node.Identifier.Text == _targetClass;

        if (_semanticModel?.GetDeclaredSymbol(node) is not INamedTypeSymbol classSymbol)
            return false;

        return SymbolEqualityComparer.Default.Equals(classSymbol, _targetTypeSymbol);
    }

    private ParameterSyntax? FindMatchingParameter(ConstructorDeclarationSyntax ctor)
    {
        return ctor.ParameterList.Parameters.FirstOrDefault(ParameterMatchesDependency);
    }

    private bool ParameterMatchesDependency(ParameterSyntax parameter)
    {
        if (parameter.Type is null)
            return false;

        if (_semanticModel is not null && _depTypeSymbol is not null)
        {
            var parameterType = _semanticModel.GetTypeInfo(parameter.Type).Type;
            if (parameterType is not null &&
                parameterType.TypeKind != TypeKind.Error &&
                _depTypeSymbol.TypeKind != TypeKind.Error &&
                SymbolEqualityComparer.Default.Equals(parameterType, _depTypeSymbol))
                return true;
        }

        return NormalizeTypeName(parameter.Type.ToString()) == NormalizeTypeName(_depType);
    }

    private bool HasFieldAssignment(ConstructorDeclarationSyntax ctor, string paramName)
    {
        if (ctor.Body is not null)
            return ctor.Body.DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Any(a => IsFieldAssignment(a, paramName));

        if (ctor.ExpressionBody?.Expression is AssignmentExpressionSyntax assignment)
            return IsFieldAssignment(assignment, paramName);

        return false;
    }

    private bool IsFieldAssignment(AssignmentExpressionSyntax assignment, string paramName)
    {
        var leftName = assignment.Left switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.Text,
            MemberAccessExpressionSyntax memberAccess when memberAccess.Expression is ThisExpressionSyntax =>
                memberAccess.Name.Identifier.Text,
            _ => null
        };

        var rightName = assignment.Right as IdentifierNameSyntax;

        return leftName == _fieldName &&
               rightName?.Identifier.Text == paramName;
    }

    private ConstructorDeclarationSyntax AddParameterAndAssignment(ConstructorDeclarationSyntax ctor)
    {
        var newParam = SyntaxFactory.ParseParameterList($"({_depType} {_paramName})").Parameters[0];

        var existing = ctor.ParameterList.Parameters;
        if (existing.Count > 0)
        {
            var openParenTrailing = ctor.ParameterList.OpenParenToken.TrailingTrivia;
            var isMultiLine = openParenTrailing.Any(t => t.ToFullString().Contains('\n'));
            if (isMultiLine)
            {
                // newline lives in the open-paren trailing trivia; indentation in the param's leading trivia
                var indent = existing[existing.Count - 1].GetLeadingTrivia();
                newParam = newParam.WithLeadingTrivia(openParenTrailing.AddRange(indent));
            }
            else
            {
                newParam = newParam.WithLeadingTrivia(SyntaxFactory.Space);
            }
        }

        var rewritten = ctor.WithParameterList(ctor.ParameterList.AddParameters(newParam));
        return AddAssignment(rewritten, _paramName);
    }

    private ConstructorDeclarationSyntax AddAssignment(ConstructorDeclarationSyntax ctor, string paramName)
    {
        if (ctor.Body is null)
            throw new InvalidOperationException(
                "Cannot add a dependency assignment to a constructor without a block body.");

        var assignment = SyntaxFactory.ParseStatement($"        {_fieldName} = {paramName};{Environment.NewLine}");
        return ctor.WithBody(ctor.Body.WithStatements(ctor.Body.Statements.Insert(0, assignment)));
    }

    private static int GetFieldInsertIndex(SyntaxList<MemberDeclarationSyntax> members)
    {
        var insertIndex = 0;
        for (var i = 0; i < members.Count; i++)
            if (members[i] is FieldDeclarationSyntax)
                insertIndex = i + 1;
            else
                break;

        return insertIndex;
    }

    private static string NormalizeTypeName(string typeName)
    {
        return typeName.Replace("global::", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
