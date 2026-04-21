using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// Rewrites a target class to inject a new dependency: adds a private readonly field,
/// and either creates a constructor or appends the parameter to existing constructors.
/// </summary>
internal sealed class DependencyInjector : CSharpSyntaxRewriter
{
    private readonly string _targetClass;
    private readonly string _depType;
    private readonly string _fieldName;
    private readonly string _paramName;

    public DependencyInjector(string targetClass, string depType, string fieldName, string paramName)
    {
        _targetClass = targetClass;
        _depType = depType;
        _fieldName = fieldName;
        _paramName = paramName;
    }

    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
    {
        if (node.Identifier.Text != _targetClass)
            return base.VisitClassDeclaration(node);

        var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

        // Skip if the field already exists
        // Stryker disable all
        if (visited.Members.OfType<FieldDeclarationSyntax>()
            .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == _fieldName)))
            return visited;
        // Stryker restore all

        // Add the field after existing fields
        var fieldDecl = SyntaxFactory.ParseMemberDeclaration(
            $"    private readonly {_depType} {_fieldName};\n")!;

        var members = visited.Members;
        int insertIndex = 0;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] is FieldDeclarationSyntax)
                insertIndex = i + 1;
            else
                // Stryker disable once StatementMutation
                break;
        }
        members = members.Insert(insertIndex, fieldDecl);
        visited = visited.WithMembers(members);

        var constructors = visited.Members.OfType<ConstructorDeclarationSyntax>().ToList();

        if (constructors.Count == 0)
        {
            // Stryker disable all
            var ctorText =
                $"    public {visited.Identifier.Text}({_depType} {_paramName})\n" +
                $"    {{\n" +
                $"        {_fieldName} = {_paramName};\n" +
                $"    }}\n";
            // Stryker restore all
            var ctorDecl = SyntaxFactory.ParseMemberDeclaration(ctorText)!;
            members = visited.Members.Insert(insertIndex + 1, ctorDecl);
            visited = visited.WithMembers(members);
        }
        else
        {
            var replacements = new List<(ConstructorDeclarationSyntax Orig, ConstructorDeclarationSyntax Repl)>();
            foreach (var ctor in constructors)
            {
                if (ctor.ParameterList.Parameters.Any(p => p.Type?.ToString().Contains(_depType) == true))
                    continue;

                var newParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier(_paramName))
                    .WithType(SyntaxFactory.ParseTypeName(_depType + " "));
                var newCtor = ctor.WithParameterList(ctor.ParameterList.AddParameters(newParam));
                var assignment = SyntaxFactory.ParseStatement($"        {_fieldName} = {_paramName};\n");
                if (newCtor.Body != null)
                    newCtor = newCtor.WithBody(newCtor.Body.WithStatements(newCtor.Body.Statements.Insert(0, assignment)));
                replacements.Add((ctor, newCtor));
            }

            // Stryker disable all
            if (replacements.Count > 0)
                visited = visited.ReplaceNodes(
                    replacements.Select(r => r.Orig),
                    (orig, _) => replacements.First(r => ReferenceEquals(r.Orig, orig)).Repl);
            // Stryker restore all
        }

        return visited;
    }
}
