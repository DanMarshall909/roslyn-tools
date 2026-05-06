using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

[SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance class created by rt destatic-class migration.")]
internal class ImplementInterfaceService : IRtService
{
    private readonly ILogger<ImplementInterfaceService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;

    public ImplementInterfaceService()
        : this(new RtConsoleLogger<ImplementInterfaceService>(), new SolutionChangeWriter())
    {
    }

    public ImplementInterfaceService(ILogger<ImplementInterfaceService> logger, ISolutionChangeWriter solutionChangeWriter)
    {
        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
    }
    internal async Task RunAsync(Solution sln, string typeName, string? interfaceName, bool dryRun)
    {
        var originalSolution = sln;

        var classSymbol = await FindClassSymbolAsync(sln, typeName)
            ?? throw new InvalidOperationException($"Class '{typeName}' not found in solution.");

        var interfacesToImplement = await CollectInterfacesToImplementAsync(sln, classSymbol, interfaceName);
        if (interfacesToImplement.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"'{typeName}' does not implement any interfaces.");
            return;
        }

        var docId = SymbolDocumentLocator.FindDocumentId(sln, classSymbol)
            ?? throw new InvalidOperationException("Cannot locate source document for the target class.");

        var unimplementedMembers = CollectUnimplementedMembers(classSymbol, interfacesToImplement);
        if (unimplementedMembers.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, $"'{typeName}' already implements all interface members.");
            return;
        }

        sln = await ApplyStubsAsync(sln, typeName, docId, classSymbol, unimplementedMembers);
        _logger.RtInfo("Adding {Count} stub(s) to '{TypeName}'...", unimplementedMembers.Count, typeName);
        await _solutionChangeWriter.ApplyChangesAsync(originalSolution, sln, dryRun);
    }

    private async Task<Solution> ApplyStubsAsync(
        Solution sln,
        string typeName,
        DocumentId docId,
        INamedTypeSymbol classSymbol,
        List<ISymbol> unimplementedMembers)
    {
        var stubs = unimplementedMembers.Select(BuildStub).OfType<MemberDeclarationSyntax>().ToList();
        if (stubs.Count == 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result, "No stubs to generate.");
            return sln;
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
        root = RoslynSyntax.EnsureUsing(root, "System");
        return sln.WithDocumentSyntaxRoot(docId, root);
    }

    private async Task<INamedTypeSymbol?> FindClassSymbolAsync(Solution sln, string typeName)
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

    private async Task<List<INamedTypeSymbol>> CollectInterfacesToImplementAsync(
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

    private List<ISymbol> CollectUnimplementedMembers(
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

    private bool ClassAlreadyHasMember(INamedTypeSymbol classSymbol, ISymbol interfaceMember)
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

    private bool ParameterTypesMatch(
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> a,
        System.Collections.Immutable.ImmutableArray<IParameterSymbol> b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            var aName = a[i].Type.MetadataName;
            var bName = b[i].Type.MetadataName;
            if (aName != bName)
                return false;
        }

        return true;
    }

    private MemberDeclarationSyntax? BuildStub(ISymbol member)
    {
        return member switch
        {
            IMethodSymbol method => BuildMethodStub(method),
            IPropertySymbol property => BuildPropertyStub(property),
            _ => null
        };
    }

    private MemberDeclarationSyntax BuildMethodStub(IMethodSymbol method)
    {
        var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var paramList = BuildParamList(method);
        var typeParams = BuildTypeParamList(method);

        var sb = new StringBuilder();
        sb.AppendF($"public {returnType} {method.Name}{typeParams}({paramList})");
        sb.AppendLine();
        sb.AppendLine("{");
        sb.AppendLine("    throw new NotImplementedException();");
        sb.Append('}');

        var member = SyntaxFactory.ParseMemberDeclaration(sb.ToString())
            ?? throw new InvalidOperationException($"Failed to parse method stub for '{method.Name}'.");

        return member;
    }

    private MemberDeclarationSyntax BuildPropertyStub(IPropertySymbol property)
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

    private string BuildParamList(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return $"{typeName} {p.Name}";
        }));
    }

    private string BuildTypeParamList(IMethodSymbol method)
    {
        if (method.TypeParameters.Length == 0)
            return string.Empty;

        return "<" + string.Join(", ", method.TypeParameters.Select(tp => tp.Name)) + ">";
    }

}
