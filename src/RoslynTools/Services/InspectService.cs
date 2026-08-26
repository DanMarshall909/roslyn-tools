using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Provides inspection capabilities for the codebase.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class InspectService : IRtService
{
    private readonly ISymbolResolutionService _symbols;

    public InspectService(ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();
    }

    /// <summary>
    ///     Resolves <paramref name="symbol" /> and builds its inspection, returning a typed
    ///     <see cref="Result{T,TError}" />: a routine not-found / ambiguous lookup surfaces as
    ///     <see cref="RtError" /> rather than an exception. Dispatch (US-135):
    ///     <list type="bullet">
    ///         <item>A member signature input (contains '(') drills in to a single member and returns
    ///         the full verbose detail <see cref="Dictionary{TKey,TValue}" /> from <see cref="BuildInspection" />.</item>
    ///         <item>A named type returns the compact <see cref="CompactDescription" /> by default;
    ///         <paramref name="withLocations" /> restores the legacy verbose dictionary.</item>
    ///         <item>Any other symbol (method/property/field/namespace by name) returns the verbose dictionary.</item>
    ///     </list>
    /// </summary>
    internal async Task<Result<object, RtError>> Describe(
        Solution solution,
        string symbol,
        string? kind,
        bool withLocations = false)
    {
        if (symbol.Contains('('))
        {
            var member = await _symbols.TryFindMemberBySignatureAsync(solution, symbol);
            return member.Map(sym => (object)BuildInspection(sym, solution));
        }

        var solutionAssemblyNames = solution.Projects
            .Select(p => p.AssemblyName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var resolved = await _symbols.TryFindSymbolByFqnAsync(solution, symbol, kind);
        return resolved.Map(sym =>
            !withLocations && sym is INamedTypeSymbol type
                ? (object)BuildCompact(type, solutionAssemblyNames)
                : BuildInspection(sym, solution));
    }

    /// <summary>
    ///     Builds the default compact describe shape for a named type: fqn, type kind, and one
    ///     canonical signature per member (no line numbers or locations).
    ///     <paramref name="solutionAssemblyNames" />, when supplied, filters
    ///     <see cref="CompactDescription.RelatedSymbolFqns" /> to solution-internal types only.
    /// </summary>
    internal CompactDescription BuildCompact(
        INamedTypeSymbol type,
        HashSet<string>? solutionAssemblyNames = null)
    {
        var fqn = type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        return new CompactDescription(fqn, type.TypeKind.ToString(), SignatureFormatter.TypeMemberSignatures(type))
        {
            RelatedSymbolFqns = ExtractRelatedSymbolFqns(type, fqn, solutionAssemblyNames)
        };
    }

    /// <summary>
    ///     Extracts FQNs of solution-internal types that <paramref name="type" /> depends on,
    ///     ranked by importance: constructor params → public method signatures → properties → fields.
    ///     Generic wrapper types (e.g. <c>IReadOnlyList&lt;T&gt;</c>) are unwrapped to their type
    ///     arguments. Returns at most 5 distinct FQNs.
    /// </summary>
    private static IReadOnlyList<string> ExtractRelatedSymbolFqns(
        INamedTypeSymbol type,
        string selfFqn,
        HashSet<string>? solutionAssemblyNames)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        void AddIfSolution(INamedTypeSymbol named)
        {
            var fqn = named.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            if (fqn == selfFqn) return;
            if (solutionAssemblyNames != null &&
                !solutionAssemblyNames.Contains(named.ContainingAssembly?.Name ?? ""))
                return;
            if (seen.Add(fqn))
                result.Add(fqn);
        }

        void TryAdd(ITypeSymbol? t)
        {
            switch (t)
            {
                case INamedTypeSymbol { SpecialType: SpecialType.None, IsGenericType: false } named:
                    AddIfSolution(named);
                    break;
                case INamedTypeSymbol { SpecialType: SpecialType.None, IsGenericType: true } generic:
                    foreach (var arg in generic.TypeArguments)
                        TryAdd(arg);
                    break;
                case IArrayTypeSymbol arr:
                    TryAdd(arr.ElementType);
                    break;
            }
        }

        // Priority 1: Constructor parameters (injected dependencies — highest signal)
        foreach (var ctor in type.Constructors.Where(c => !c.IsImplicitlyDeclared))
            foreach (var p in ctor.Parameters)
                TryAdd(p.Type);

        // Priority 2: Public method return types and parameters
        foreach (var method in type.GetMembers()
                                   .OfType<IMethodSymbol>()
                                   .Where(m => m.DeclaredAccessibility == Accessibility.Public
                                            && m.MethodKind == MethodKind.Ordinary))
        {
            TryAdd(method.ReturnType);
            foreach (var p in method.Parameters)
                TryAdd(p.Type);
        }

        // Priority 3: Properties
        foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
            TryAdd(prop.Type);

        // Priority 4: Fields (stored collaborators)
        foreach (var field in type.GetMembers()
                                   .OfType<IFieldSymbol>()
                                   .Where(f => !f.IsImplicitlyDeclared))
            TryAdd(field.Type);

        return [.. result.Take(5)];
    }

    /// <summary>
    /// Builds an inspection for a symbol in a solution
    /// </summary>
    internal Dictionary<string, object?> BuildInspection(ISymbol symbol, Solution solution)
    {
        var result = new Dictionary<string, object?>
        {
            ["name"] = symbol.Name,
            ["kind"] = symbol.Kind.ToString(),
            ["fqn"] = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            ["accessibility"] = symbol.DeclaredAccessibility.ToString(),
            ["isStatic"] = symbol.IsStatic,
            ["isAbstract"] = symbol.IsAbstract,
            ["isSealed"] = symbol.IsSealed
        };

        result["locations"] = SymbolFormatter.ExtractSourceLocations(symbol);
        result["namespace"] = symbol.ContainingNamespace?.ToDisplayString();
        result["containingType"] = symbol.ContainingType?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        if (symbol is INamedTypeSymbol type)
        {
            result["typeKind"] = type.TypeKind.ToString();

            SymbolFormatter.AddTypeHierarchyInfo(result, type);

            var typeParams = type.TypeParameters
                .Select(tp => tp.Name)
                .ToList();
            if (typeParams.Count > 0)
                result["typeParameters"] = typeParams;

            result["members"] = SymbolFormatter.ExtractMembers(type);

            var constructors = type.Constructors
                .Where(c => !c.IsImplicitlyDeclared)
                .Select(c => new Dictionary<string, object?>
                {
                    ["signature"] = c.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["parameters"] = c.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
                    }).ToList()
                })
                .ToList();
            if (constructors.Count > 0)
                result["constructors"] = constructors;
        }
        else if (symbol is IMethodSymbol method)
        {
            result["returnType"] = method.ReturnType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            result["parameters"] = method.Parameters.Select(p => new Dictionary<string, object?>
            {
                ["name"] = p.Name,
                ["type"] = p.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)
            }).ToList();
        }
        else if (symbol is IPropertySymbol prop)
        {
            result["type"] = prop.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            result["hasGetter"] = prop.GetMethod != null;
            result["hasSetter"] = prop.SetMethod != null;
        }
        else if (symbol is IFieldSymbol field)
        {
            result["type"] = field.Type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
            result["isConst"] = field.IsConst;
            result["isReadOnly"] = field.IsReadOnly;
            if (field.HasConstantValue)
                result["constantValue"] = field.ConstantValue;
        }

        return result;
    }

    /// <summary>
    /// Gets the source code for a symbol asynchronously
    /// </summary>
    internal async Task<List<SourceDeclaration>> GetSourceAsync(ISymbol symbol)
    {
        var results = new List<SourceDeclaration>();

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var node = await syntaxRef.GetSyntaxAsync();
            var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);

            results.Add(new SourceDeclaration(
                symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                syntaxRef.SyntaxTree.FilePath,
                lineSpan.StartLinePosition.Line + 1,
                lineSpan.EndLinePosition.Line + 1,
                node.ToFullString().TrimEnd()));
        }

        return results;
    }
}
