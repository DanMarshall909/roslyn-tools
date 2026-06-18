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
            var member = await SymbolLocator.TryFindMemberBySignatureAsync(solution, symbol);
            return member.Map(sym => (object)BuildInspection(sym, solution));
        }

        var resolved = await SymbolLocator.TryFindSymbolByFqnAsync(solution, symbol, kind);
        return resolved.Map(sym =>
            !withLocations && sym is INamedTypeSymbol type
                ? (object)BuildCompact(type)
                : BuildInspection(sym, solution));
    }

    /// <summary>
    ///     Builds the default compact describe shape for a named type: fqn, type kind, and one
    ///     canonical signature per member (no line numbers or locations).
    /// </summary>
    internal CompactDescription BuildCompact(INamedTypeSymbol type) =>
        new(
            type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
            type.TypeKind.ToString(),
            SignatureFormatter.TypeMemberSignatures(type));

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
    internal async Task<List<Dictionary<string, object?>>> GetSourceAsync(ISymbol symbol)
    {
        var results = new List<Dictionary<string, object?>>();

        foreach (var syntaxRef in symbol.DeclaringSyntaxReferences)
        {
            var node = await syntaxRef.GetSyntaxAsync();
            var lineSpan = node.SyntaxTree.GetLineSpan(node.Span);

            results.Add(new Dictionary<string, object?>
            {
                ["fqn"] = symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                ["file"] = syntaxRef.SyntaxTree.FilePath,
                ["lineFrom"] = lineSpan.StartLinePosition.Line + 1,
                ["lineTo"] = lineSpan.EndLinePosition.Line + 1,
                ["source"] = node.ToFullString().TrimEnd()
            });
        }

        return results;
    }
}
