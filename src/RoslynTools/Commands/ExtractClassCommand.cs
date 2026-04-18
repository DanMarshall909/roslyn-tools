using System.CommandLine;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ExtractClassCommand : IRtCommand
{
    private ExtractClassCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "extract-class",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["ec"],
        WhenToUse: "Moving a named set of members out of an oversized class into a new dedicated class file. When --static is not set, the command also wires the new class into the source class as a constructor-injected dependency, making it immediately usable without manual plumbing.",
        Examples:
        [
            "rt extract-class --type AdminController --members ExportCsv,ImportCsv --new-type CsvController --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt ec --type ReportService --members BuildReport,FormatReport --new-type ReportFormatter --static --dry-run",
            "rt extract-class --type ProductService --members ValidateProduct --new-type ProductValidator --solution RoslynTools.slnx --output-dir src/Validators",
        ]
    );

    public static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Source class name") { IsRequired = true };
        var membersOpt = new Option<string>("--members", "Comma-separated member names to extract") { IsRequired = true };
        var newTypeOpt = new Option<string>("--new-type", "Name for the new class") { IsRequired = true };
        var staticOpt = new Option<bool>("--static", "Make the new class static (no injection)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var outputDirOpt = new Option<string?>("--output-dir", "Directory for new file");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("extract-class", "Move named members from a class into a new class file");
        cmd.AddAlias("ec");
        cmd.AddAll(typeOpt, membersOpt, newTypeOpt, staticOpt, solutionOpt, outputDirOpt, dryRunOpt);

        cmd.SetHandler(async (type, members, newType, isStatic, solution, outputDir, dryRun) =>
        {
            await ExecuteAsync(type, members, newType, isStatic, solution, outputDir, dryRun);
        }, typeOpt, membersOpt, newTypeOpt, staticOpt, solutionOpt, outputDirOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string typeName,
        string members,
        string newTypeName,
        bool isStatic,
        string? solutionPath,
        string? outputDir,
        bool dryRun)
    {
        var (workspace, solution) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = solution;

            // Find the document containing the source class
            Document? sourceDocument = null;
            ClassDeclarationSyntax? classDecl = null;
            Microsoft.CodeAnalysis.SyntaxNode? root = null;

            foreach (var project in solution.Projects)
            {
                foreach (var doc in project.Documents)
                {
                    if (doc.FilePath == null) continue;
                    var docRoot = await doc.GetSyntaxRootAsync();
                    if (docRoot == null) continue;

                    var found = docRoot.DescendantNodes()
                        .OfType<ClassDeclarationSyntax>()
                        .FirstOrDefault(c => c.Identifier.Text == typeName);

                    if (found != null)
                    {
                        sourceDocument = doc;
                        classDecl = found;
                        root = docRoot;
                        break;
                    }
                }
                if (sourceDocument != null) break;
            }

            if (sourceDocument == null || classDecl == null || root == null)
            {
                Console.Error.WriteLine($"Class '{typeName}' not found in solution.");
                return;
            }

            // Parse member names
            var memberNames = members.Split(',')
                .Select(m => m.Trim())
                .Where(m => !string.IsNullOrEmpty(m))
                .ToHashSet(StringComparer.Ordinal);

            // Find the member syntax nodes to extract
            var membersToExtract = classDecl.Members
                .Where(m => m switch
                {
                    MethodDeclarationSyntax method => memberNames.Contains(method.Identifier.Text),
                    FieldDeclarationSyntax field => field.Declaration.Variables
                        .Any(v => memberNames.Contains(v.Identifier.Text)),
                    PropertyDeclarationSyntax prop => memberNames.Contains(prop.Identifier.Text),
                    _ => false
                })
                .ToList();

            if (membersToExtract.Count == 0)
            {
                Console.Error.WriteLine($"None of the specified members ({members}) were found in '{typeName}'.");
                return;
            }

            // Detect namespace from source class
            var namespaceName = classDecl.Ancestors()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Select(n => n.Name.ToString())
                .FirstOrDefault() ?? string.Empty;

            // Build new class content
            var newClassText = BuildNewClassText(namespaceName, newTypeName, isStatic, membersToExtract);

            // Remove extracted members from source class
            var updatedClassDecl = classDecl.RemoveNodes(membersToExtract, SyntaxRemoveOptions.KeepNoTrivia)!;
            var newRoot = root.ReplaceNode(classDecl, updatedClassDecl);
            solution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, newRoot);

            // If not static, inject the new type as a dependency into the source class
            if (!isStatic)
            {
                // Re-fetch the updated document/root
                var updatedDoc = solution.GetDocument(sourceDocument.Id)!;
                var updatedRoot = await updatedDoc.GetSyntaxRootAsync();
                if (updatedRoot != null)
                {
                    var fieldName = DeriveFieldName(newTypeName);
                    var paramName = fieldName.TrimStart('_');
                    var injector = new DependencyInjector(typeName, newTypeName, fieldName, paramName);
                    var injectedRoot = injector.Visit(updatedRoot);
                    solution = solution.WithDocumentSyntaxRoot(sourceDocument.Id, injectedRoot);
                }
            }

            // Determine output path for new file
            var sourceDir = outputDir ?? Path.GetDirectoryName(sourceDocument.FilePath)!;
            var newFileName = newTypeName + ".cs";
            var newFilePath = Path.Combine(sourceDir, newFileName);

            // Add new file document to solution
            var owningProject = solution.Projects
                .FirstOrDefault(p => p.Documents.Any(d => d.Id == sourceDocument.Id))
                ?? throw new InvalidOperationException("Cannot find project containing the class.");

            var newSourceText = SourceText.From(newClassText, Encoding.UTF8);
            var finalSolution = solution.AddDocument(
                DocumentId.CreateNewId(owningProject.Id),
                newFileName,
                newSourceText,
                filePath: newFilePath);

            Console.Error.WriteLine($"Extracting '{string.Join(", ", memberNames)}' from '{typeName}' into '{newTypeName}'...");
            await SolutionWriter.ApplyChangesAsync(originalSolution, finalSolution, dryRun);
        }
    }

    private static string BuildNewClassText(
        string namespaceName,
        string newTypeName,
        bool isStatic,
        List<MemberDeclarationSyntax> membersToExtract)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        var staticKeyword = isStatic ? "static " : string.Empty;
        sb.AppendLine($"public {staticKeyword}class {newTypeName}");
        sb.AppendLine("{");

        foreach (var member in membersToExtract)
        {
            // Normalize leading/trailing whitespace for clean formatting
            var text = member.ToFullString().TrimEnd();
            // Ensure each member has 4-space indent
            foreach (var line in text.Split('\n'))
            {
                var trimmedLine = line.TrimEnd('\r');
                if (string.IsNullOrWhiteSpace(trimmedLine))
                    sb.AppendLine();
                else
                    sb.AppendLine($"    {trimmedLine.TrimStart()}");
            }
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Field name derivation: LogHelper → _logHelper  (no 'I' stripping)
    // ──────────────────────────────────────────────────────────────────────────

    private static string DeriveFieldName(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return "_dependency";
        var name = char.ToLower(typeName[0]) + typeName[1..];
        return "_" + name;
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  Dependency injection rewriter (mirrors AddDependencyCommand.DependencyInjector)
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class DependencyInjector : CSharpSyntaxRewriter
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

            return visited;
        }
    }
}
