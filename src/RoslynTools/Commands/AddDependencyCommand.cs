using System.CommandLine;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class AddDependencyCommand : IRtCommand
{
    private AddDependencyCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "add-dependency",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["ad"],
        WhenToUse: "Injecting a new constructor dependency into a class without manually editing the constructor and field declarations. Prefer this over a manual edit when the class has multiple constructors or when you want to be confident the field naming convention (_camelCase) is applied consistently.",
        Examples:
        [
            "rt add-dependency --type BookingService --dependency IEmailService --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt ad --type OrderController --dependency IPaymentGateway --field _payment --dry-run",
            "rt add-dependency --type ReportGenerator --dependency IRepository --solution RoslynTools.slnx",
        ],
        KnownIssues:
        [
            "Invoking via `cmd //c` from bash on Windows crashes during workspace load — see ai-improvements row #13.",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Target class name") { IsRequired = true };
        var depOpt = new Option<string>("--dependency", "Dependency type to inject (e.g. IEmailService)") { IsRequired = true };
        var fieldOpt = new Option<string?>("--field", "Field name override (default: derived from type)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("add-dependency", "Inject a constructor dependency into a class");
        cmd.AddAlias("ad");
        cmd.Add(typeOpt);
        cmd.Add(depOpt);
        cmd.Add(fieldOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, dep, field, solution, dryRun) =>
        {
            await ExecuteAsync(type, dep, field, solution, dryRun);
        }, typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string depType, string? fieldName, string? solutionPath, bool dryRun)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = solution;
            var resolvedField = fieldName ?? DeriveFieldName(depType);
            var paramName = resolvedField.TrimStart('_');

            bool found = false;
            foreach (var project in solution.Projects)
            {
                foreach (var doc in project.Documents)
                {
                    if (doc.FilePath == null) continue;

                    var root = await doc.GetSyntaxRootAsync();
                    if (root == null) continue;

                    var hasTarget = root.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .Any(c => c.Identifier.Text == typeName);

                    if (!hasTarget) continue;

                    var injector = new DependencyInjector(typeName, depType, resolvedField, paramName);
                    var newRoot = injector.Visit(root);

                    if (!injector.Applied) continue;

                    solution = solution.WithDocumentSyntaxRoot(doc.Id, newRoot);
                    found = true;
                }
            }

            if (!found)
            {
                Console.Error.WriteLine($"Class '{typeName}' not found in solution.");
                return;
            }

            await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
            Console.Error.WriteLine($"Injected {depType} into {typeName}.");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Field name derivation: IEmailService → _emailService
    // ──────────────────────────────────────────────────────────────────────────

    private static string DeriveFieldName(string depType)
    {
        // Strip leading 'I' from interface names: IEmailService → EmailService
        var name = depType;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name[1..];

        // Convert first char to lowercase: EmailService → emailService
        name = char.ToLower(name[0]) + name[1..];

        // Prefix with _: emailService → _emailService
        return "_" + name;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Syntax rewriter: adds field + ctor param to target class
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class DependencyInjector : CSharpSyntaxRewriter
    {
        private readonly string _targetClass;
        private readonly string _depType;
        private readonly string _fieldName;
        private readonly string _paramName;

        public bool Applied { get; private set; }

        public DependencyInjector(string targetClass, string depType, string fieldName, string paramName)
        {
            _targetClass = targetClass;
            _depType = depType;
            _fieldName = fieldName;
            _paramName = paramName;
        }

        public override Microsoft.CodeAnalysis.SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node)
        {
            if (node.Identifier.Text != _targetClass)
                return base.VisitClassDeclaration(node);

            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node)!;

            // Skip if the field already exists
            if (visited.Members.OfType<FieldDeclarationSyntax>()
                .Any(f => f.Declaration.Variables.Any(v => v.Identifier.Text == _fieldName)))
                return visited;

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
                    break;
            }
            members = members.Insert(insertIndex, fieldDecl);
            visited = visited.WithMembers(members);

            var constructors = visited.Members.OfType<ConstructorDeclarationSyntax>().ToList();

            if (constructors.Count == 0)
            {
                // Generate a new constructor
                var ctorText =
                    $"    public {visited.Identifier.Text}({_depType} {_paramName})\n" +
                    $"    {{\n" +
                    $"        {_fieldName} = {_paramName};\n" +
                    $"    }}\n";
                var ctorDecl = SyntaxFactory.ParseMemberDeclaration(ctorText)!;
                members = visited.Members.Insert(insertIndex + 1, ctorDecl);
                visited = visited.WithMembers(members);
            }
            else
            {
                // Add parameter + assignment to existing constructors
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

                if (replacements.Count > 0)
                    visited = visited.ReplaceNodes(
                        replacements.Select(r => r.Orig),
                        (orig, _) => replacements.First(r => ReferenceEquals(r.Orig, orig)).Repl);
            }

            Applied = true;
            return visited;
        }
    }
}
