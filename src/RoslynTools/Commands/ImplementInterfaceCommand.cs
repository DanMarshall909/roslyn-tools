using System.CommandLine;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class ImplementInterfaceCommand : IRtCommand
{
    private ImplementInterfaceCommand() { }

    public static RtCommandInfo Info { get; } = new(
        Name: "implement-interface",
        Mutates: true,
        JsonOutput: false,
        Category: "Mutation",
        Aliases: ["ii"],
        WhenToUse: "Generating throw-stub implementations for all unimplemented interface members in a class. Use after extract-interface or when a class is added to an interface's base list and needs skeleton stubs before real implementations are written.",
        Examples:
        [
            "rt implement-interface --type BookingRepository --interface IBookingRepository --solution C:\\code\\libraries\\ProjectX.Libraries.sln",
            "rt ii --type EmailSender --dry-run",
            "rt implement-interface --type HeroApiClient --solution RoslynTools.slnx",
        ]
    );

    public static Command Build()
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

            var classSymbol = await FindClassSymbolAsync(sln, typeName)
                ?? throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

            var interfacesToImplement = await CollectInterfacesToImplementAsync(sln, classSymbol, interfaceName);
            if (interfacesToImplement.Count == 0)
            {
                Console.WriteLine($"'{typeName}' does not implement any interfaces.");
                return;
            }

            var docId = await FindDocumentIdAsync(sln, classSymbol)
                ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

            var unimplementedMembers = CollectUnimplementedMembers(classSymbol, interfacesToImplement);
            if (unimplementedMembers.Count == 0)
            {
                Console.WriteLine($"'{typeName}' already implements all interface members.");
                return;
            }

            var stubs = unimplementedMembers.Select(BuildStub).OfType<MemberDeclarationSyntax>().ToList();
            if (stubs.Count == 0)
            {
                Console.WriteLine("No stubs to generate.");
                return;
            }

            var document = sln.GetDocument(docId)!;
            var root = (CompilationUnitSyntax)(await document.GetSyntaxRootAsync())!;

            var syntaxRef = classSymbol.DeclaringSyntaxReferences.FirstOrDefault()
                ?? throw new InvalidOperationException($"No source declaration found for '{typeName}'.");

            var classDecl = root.FindNode(syntaxRef.Span) as ClassDeclarationSyntax
                ?? throw new InvalidOperationException($"Class declaration '{typeName}' not found in syntax tree.");

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
            root = EnsureUsing(root, "System");
            sln = sln.WithDocumentSyntaxRoot(docId, root);

            Console.Error.WriteLine($"Adding {stubs.Count} stub(s) to '{typeName}'...");
            await SolutionWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
        }
    }

    private static async Task<INamedTypeSymbol?> FindClassSymbolAsync(Solution sln, string typeName)
    {
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
                    return namedType;
                }
            }
        }

        return null;
    }

    private static async Task<List<INamedTypeSymbol>> CollectInterfacesToImplementAsync(
        Solution sln, INamedTypeSymbol classSymbol, string? interfaceName)
    {
        var result = new List<INamedTypeSymbol>();

        if (!string.IsNullOrWhiteSpace(interfaceName))
        {
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

            result.Add(targetInterface);
        }
        else
        {
            result.AddRange(classSymbol.Interfaces);
        }

        return result;
    }

    private static async Task<DocumentId?> FindDocumentIdAsync(Solution sln, INamedTypeSymbol classSymbol)
    {
        var location = classSymbol.Locations.FirstOrDefault(l => l.IsInSource)
            ?? throw new InvalidOperationException($"No source location found for '{classSymbol.Name}'.");

        foreach (var project in sln.Projects)
        {
            foreach (var doc in project.Documents)
            {
                if (doc.FilePath != null &&
                    string.Equals(doc.FilePath, location.SourceTree?.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    return doc.Id;
                }
            }
        }

        return null;
    }

    private static List<ISymbol> CollectUnimplementedMembers(
        INamedTypeSymbol classSymbol, List<INamedTypeSymbol> interfacesToImplement)
    {
        var unimplemented = new List<ISymbol>();
        foreach (var iface in interfacesToImplement)
        {
            foreach (var member in iface.GetMembers())
            {
                if (member is not IMethodSymbol and not IPropertySymbol)
                    continue;
                if (member is IMethodSymbol ms && ms.MethodKind != MethodKind.Ordinary)
                    continue;

                var implementation = classSymbol.FindImplementationForInterfaceMember(member);
                if (implementation != null)
                    continue;

                if (ClassAlreadyHasMember(classSymbol, member))
                    continue;

                unimplemented.Add(member);
            }
        }

        return unimplemented;
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
