using System.CommandLine;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal static class ImplementInterfaceCommand
{
    internal static Command Build()
    {
        var typeOpt = new Option<string>("--type", "Class to add interface stubs to") { IsRequired = true };
        var interfaceOpt = new Option<string?>("--interface", "Interface to implement (optional; auto-detected if omitted)");
        var solutionOpt = new Option<string?>("--solution", "Path to .sln/.slnx");
        var dryRunOpt = new Option<bool>("--dry-run", "Preview without writing");

        var cmd = new Command("implement-interface", "Generate throw-stub implementations for unimplemented interface members");
        cmd.AddAlias("ii");
        cmd.Add(typeOpt);
        cmd.Add(interfaceOpt);
        cmd.Add(solutionOpt);
        cmd.Add(dryRunOpt);

        cmd.SetHandler(async (type, iface, solution, dryRun) =>
        {
            await ExecuteAsync(type, iface, solution, dryRun);
        }, typeOpt, interfaceOpt, solutionOpt, dryRunOpt);

        return cmd;
    }

    internal static async Task ExecuteAsync(string typeName, string? interfaceName, string? solutionPath, bool dryRun)
    {
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(solutionPath);
        using (workspace)
        {
            var originalSolution = sln;

            // Find the class symbol
            INamedTypeSymbol? classSymbol = null;
            DocumentId? docId = null;

            foreach (var project in sln.Projects)
            {
                var compilation = await project.GetCompilationAsync();
                if (compilation == null) continue;

                foreach (var symbol in compilation.GetSymbolsWithName(typeName, SymbolFilter.Type))
                {
                    if (symbol is INamedTypeSymbol namedType &&
                        namedType.TypeKind == TypeKind.Class &&
                        namedType.Name == typeName)
                    {
                        classSymbol = namedType;
                        break;
                    }
                }

                if (classSymbol != null) break;
            }

            if (classSymbol == null)
                throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

            // Collect interfaces to implement
            var interfacesToImplement = new List<INamedTypeSymbol>();
            if (!string.IsNullOrWhiteSpace(interfaceName))
            {
                // Find the specified interface by name
                INamedTypeSymbol? targetInterface = null;
                foreach (var project in sln.Projects)
                {
                    var compilation = await project.GetCompilationAsync();
                    if (compilation == null) continue;

                    foreach (var symbol in compilation.GetSymbolsWithName(interfaceName, SymbolFilter.Type))
                    {
                        if (symbol is INamedTypeSymbol namedType &&
                            namedType.TypeKind == TypeKind.Interface &&
                            namedType.Name == interfaceName)
                        {
                            targetInterface = namedType;
                            break;
                        }
                    }

                    if (targetInterface != null) break;
                }

                if (targetInterface == null)
                    throw new InvalidOperationException($"Interface '{interfaceName}' not found in solution.");

                interfacesToImplement.Add(targetInterface);
            }
            else
            {
                // Use all directly declared interfaces
                interfacesToImplement.AddRange(classSymbol.Interfaces);
            }

            if (interfacesToImplement.Count == 0)
            {
                Console.WriteLine($"'{typeName}' does not implement any interfaces.");
                return;
            }

            // Locate the source document for the class
            var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource);
            if (location == null)
                throw new InvalidOperationException($"No source location found for '{typeName}'.");

            foreach (var project in sln.Projects)
            {
                foreach (var doc in project.Documents)
                {
                    if (doc.FilePath != null &&
                        string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        docId = doc.Id;
                        break;
                    }
                }

                if (docId != null) break;
            }

            if (docId == null)
                throw new InvalidOperationException("Cannot locate source document for the target class.");

            // Collect all unimplemented members across specified interfaces
            var unimplementedMembers = new List<ISymbol>();
            foreach (var iface in interfacesToImplement)
            {
                foreach (var member in iface.GetMembers())
                {
                    // Skip constructors, events, indexers for now
                    if (member is not IMethodSymbol and not IPropertySymbol)
                        continue;
                    if (member is IMethodSymbol ms && ms.MethodKind != MethodKind.Ordinary)
                        continue;

                    // First try Roslyn's interface implementation lookup
                    var implementation = classSymbol.FindImplementationForInterfaceMember(member);
                    if (implementation != null)
                        continue;

                    // If the class doesn't formally declare the interface, also check by
                    // matching name + compatible signature so we don't duplicate existing members.
                    if (ClassAlreadyHasMember(classSymbol, member))
                        continue;

                    unimplementedMembers.Add(member);
                }
            }

            if (unimplementedMembers.Count == 0)
            {
                Console.WriteLine($"'{typeName}' already implements all interface members.");
                return;
            }

            // Build stub text for each unimplemented member
            var stubs = new List<MemberDeclarationSyntax>();
            foreach (var member in unimplementedMembers)
            {
                var stub = BuildStub(member);
                if (stub != null)
                    stubs.Add(stub);
            }

            if (stubs.Count == 0)
            {
                Console.WriteLine("No stubs to generate.");
                return;
            }

            // Modify the document
            var document = sln.GetDocument(docId)!;
            var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync())!;

            // Find the class declaration syntax
            var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault();
            if (syntaxRef == null)
                throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

            var classDecl = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax
                ?? throw new InvalidOperationException($"Class declaration '{typeName}' not found in syntax tree.");

            // Insert all stubs into the class body (append before closing brace)
            var newMembers = classDecl.Members;
            foreach (var stub in stubs)
            {
                var indented = stub.WithLeadingTrivia(SyntaxFactory.TriviaList(
                    SyntaxFactory.LineFeed,
                    SyntaxFactory.Whitespace("    ")));
                newMembers = newMembers.Add(indented);
            }

            var newClassDecl = classDecl.WithMembers(newMembers);
            root = root.ReplaceNode(classDecl, newClassDecl);

            // Ensure `using System;` is present for NotImplementedException
            root = EnsureUsing(root, "System");

            sln = sln.WithDocumentSyntaxRoot(docId, root);

            Console.Error.WriteLine($"Adding {stubs.Count} stub(s) to '{typeName}'...");
            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }

    /// <summary>
    /// Returns true if the class already has a member matching the interface member's
    /// name and parameter count — used when the class does not formally declare the
    /// interface so FindImplementationForInterfaceMember returns null.
    /// </summary>
    private static bool ClassAlreadyHasMember(INamedTypeSymbol classSymbol, ISymbol interfaceMember)
    {
        return interfaceMember switch
        {
            IMethodSymbol ifaceMethod => classSymbol.GetMembers(ifaceMethod.Name)
                .OfType<IMethodSymbol>()
                .Where(m => m.MethodKind == MethodKind.Ordinary)
                .Any(m => m.Parameters.Length == ifaceMethod.Parameters.Length &&
                          ParameterTypesMatch(m.Parameters, ifaceMethod.Parameters)),

            IPropertySymbol ifaceProp => classSymbol.GetMembers(ifaceProp.Name)
                .OfType<IPropertySymbol>()
                .Any(),

            _ => false
        };
    }

    private static bool ParameterTypesMatch(
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> a,
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            // Use the metadata name for the most stable comparison
            var aName = a[i].Type.MetadataName;
            var bName = b[i].Type.MetadataName;
            if (aName != bName)
                return false;
        }

        return true;
    }

    private static MemberDeclarationSyntax? BuildStub(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => BuildMethodStub(method),
            IPropertySymbol property => BuildPropertyStub(property),
            _ => null
        };
    }

    private static MemberDeclarationSyntax BuildMethodStub(IMethodSymbol method)
    {
        var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var paramList = BuildParamList(method);
        var typeParams = BuildTypeParamList(method);

        var sb = new StringBuilder();
        sb.Append($"public {returnType} {method.Name}{typeParams}({paramList})");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("    throw new NotImplementedException();");
        sb.Append("}");

        var member = SyntaxFactory.ParseMemberDeclaration(sb.ToString())
            ?? throw new InvalidOperationException($"Failed to parse method stub for '{method.Name}'.");

        return member;
    }

    private static MemberDeclarationSyntax BuildPropertyStub(IPropertySymbol property)
    {
        var typeName = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        string accessors;
        if (property.SetMethod != null)
        {
            accessors = "get => throw new NotImplementedException(); set => throw new NotImplementedException();";
        }
        else
        {
            accessors = "get => throw new NotImplementedException();";
        }

        var text = $"public {typeName} {property.Name} {{ {accessors} }}";

        var member = SyntaxFactory.ParseMemberDeclaration(text)
            ?? throw new InvalidOperationException($"Failed to parse property stub for '{property.Name}'.");

        return member;
    }

    private static string BuildParamList(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return $"{typeName} {p.Name}";
        }));
    }

    private static string BuildTypeParamList(IMethodSymbol method)
    {
        if (method.TypeParameters.Length == 0)
            return string.Empty;

        return "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">";
    }

    private static CompilationUnitSyntax EnsureUsing(CompilationUnitSyntax root, string ns)
    {
        var existing = root.Usings
            .Select(u => u.Name?.ToString().Trim())
            .ToHashSet(StringComparer.Ordinal);

        if (existing.Contains(ns))
            return root;

        var newUsing = SyntaxFactory.UsingDirective(
            SyntaxFactory.ParseName(" " + ns))
            .WithTrailingTrivia(SyntaxFactory.LineFeed);

        return root.AddUsings(newUsing);
    }
}
