using System.CommandLine;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class ExtractInterfaceCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to extract interface from") { IsRequired = true };
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var outputDirOpt = new Option<string?>("--output-dir", "Directory for the new interface file");
        var namespaceOpt = new Option<string?>("--namespace", "Namespace override for the interface");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("extract-interface", "Generate IXxx interface from a class's public members");
        cmd.AddAlias("ei");
        cmd.Add(typeOpt);
        cmd.Add(solutionOpt);
        cmd.Add(outputDirOpt);
        cmd.Add(namespaceOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, solution, outputDir, ns, dryRun) =>
        {
            await ExecuteAsync(type, solution, outputDir, ns, dryRun);
        }, typeOpt, solutionOpt, outputDirOpt, namespaceOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(
        string typeName,
        string? solutionPath,
        string? outputDir,
        string? ns,
        bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            // Find the class symbol
            var classSymbol = await SymbolLocator.FindSymbolAsync(sln, typeName, kind: "type") as INamedTypeSymbol
                ?? throw new InvalidOperationException($"'{typeName}' is not a named type.");

            if (classSymbol.TypeKind != TypeKind.Class)
                throw new InvalidOperationException($"'{typeName}' must be a class, but is {classSymbol.TypeKind}.");

            var interfaceName = "I" + classSymbol.Name;
            var targetNamespace = ns ?? classSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

            // Find the source file of the class
            var classFilePath = classSymbol.Locations
                .Where(l => l.IsInSource)
                .Select(l => l.SourceTree?.FilePath)
                .FirstOrDefault(p => !string.IsNullOrEmpty(p));

            if (string.IsNullOrEmpty(classFilePath))
                throw new InvalidOperationException($"Cannot determine source file for '{typeName}'.");

            // Determine interface file path
            var classDir = outputDir ?? Path.GetDirectoryName(classFilePath)!;
            var interfaceFileName = interfaceName + ".cs";
            var interfaceFilePath = Path.Combine(classDir, interfaceFileName);

            // Collect public non-static methods and properties
            var publicMethods = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m =>
                    m.DeclaredAccessibility == Accessibility.Public &&
                    !m.IsStatic &&
                    m.MethodKind == MethodKind.Ordinary)
                .ToList();

            var publicProperties = classSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(p =>
                    p.DeclaredAccessibility == Accessibility.Public &&
                    !p.IsStatic)
                .ToList();

            // Build interface source text
            var interfaceText = BuildInterfaceText(targetNamespace, interfaceName, publicMethods, publicProperties);

            // Build updated class text with : IInterfaceName added
            var classDocument = sln.Projects
                .SelectMany(p => p.Documents)
                .FirstOrDefault(d => string.Equals(d.FilePath, classFilePath, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException($"Document for '{classFilePath}' not found in solution.");

            var updatedClassSolution = await AddInterfaceToClassAsync(sln, classDocument, classSymbol, interfaceName);

            // Add the new interface document to the project that owns the class
            var owningProject = updatedClassSolution.Projects
                .FirstOrDefault(p => p.Documents.Any(d =>
                    string.Equals(d.FilePath, classFilePath, StringComparison.OrdinalIgnoreCase)))
                ?? throw new InvalidOperationException("Cannot find project containing the class.");

            var interfaceSourceText = SourceText.From(interfaceText, Encoding.UTF8);
            var finalSolution = updatedClassSolution.AddDocument(
                DocumentId.CreateNewId(owningProject.Id),
                interfaceFileName,
                interfaceSourceText,
                filePath: interfaceFilePath);

            Console.Error.WriteLine($"Extracting interface '{interfaceName}' from '{classSymbol.Name}'...");
            await SolutionWriter.ApplyChangesAsync(sln, finalSolution, dryRun);
        }
    }

    private static string BuildInterfaceText(
        string namespaceName,
        string interfaceName,
        List<IMethodSymbol> methods,
        List<IPropertySymbol> properties)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLine($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLine($"public interface {interfaceName}");
        sb.AppendLine("{");

        foreach (var method in methods)
        {
            var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var paramList = BuildParamList(method);
            sb.AppendLine($"    {returnType} {method.Name}({paramList});");
        }

        foreach (var property in properties)
        {
            var typeName = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            var accessors = BuildAccessors(property);
            sb.AppendLine($"    {typeName} {property.Name} {{ {accessors} }}");
        }

        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string BuildParamList(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return $"{typeName} {p.Name}";
        }));
    }

    private static string BuildAccessors(IPropertySymbol property)
    {
        var parts = new List<string>();
        if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            parts.Add("get;");
        if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            parts.Add("set;");
        return string.Join(" ", parts);
    }

    private static async Task<Solution> AddInterfaceToClassAsync(
        Solution solution,
        Document classDocument,
        INamedTypeSymbol classSymbol,
        string interfaceName)
    {
        var root = await classDocument.GetSyntaxRootAsync()
            ?? throw new InvalidOperationException("Cannot get syntax root.");

        // Find the class declaration by name
        var classDecl = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == classSymbol.Name)
            ?? throw new InvalidOperationException($"Class declaration '{classSymbol.Name}' not found in syntax tree.");

        // Build the interface type syntax with a leading space so output is "ClassName : IFoo"
        var interfaceTypeName = SyntaxFactory.SimpleBaseType(
            SyntaxFactory.IdentifierName(interfaceName));

        ClassDeclarationSyntax updatedClass;

        if (classDecl.BaseList is null)
        {
            // No existing base list — create ": IFoo" with leading/trailing spaces
            var colonToken = SyntaxFactory.Token(
                SyntaxFactory.TriviaList(SyntaxFactory.Space),
                SyntaxKind.ColonToken,
                SyntaxFactory.TriviaList(SyntaxFactory.Space));

            var baseList = SyntaxFactory.BaseList(
                colonToken,
                SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(interfaceTypeName));

            // Strip trailing whitespace from identifier so we don't double-space
            var identifierToken = classDecl.Identifier
                .WithTrailingTrivia(SyntaxFactory.TriviaList());
            var classWithCleanId = classDecl.WithIdentifier(identifierToken);
            updatedClass = classWithCleanId.WithBaseList(baseList);
        }
        else
        {
            // Append to existing base list
            var newTypes = classDecl.BaseList.Types.Add(interfaceTypeName);
            updatedClass = classDecl.WithBaseList(classDecl.BaseList.WithTypes(newTypes));
        }

        var newRoot = root.ReplaceNode(classDecl, updatedClass);
        return solution.WithDocumentSyntaxRoot(classDocument.Id, newRoot);
    }
}
