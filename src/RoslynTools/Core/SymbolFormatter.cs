using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>
///     Shared helpers for formatting Roslyn symbols into JSON-serializable dictionaries.
///     Extracted from InspectCommand, OutlineCommand, FindImplementationsCommand to eliminate duplication.
/// </summary>
internal static class SymbolFormatter
{
    // UnicodeRanges.All lets non-ASCII chars (arrows, CJK, etc.) appear as-is in CLI output
    // while still escaping control chars (U+0000-U+001F) as required by RFC 8259.
    // Unlike UnsafeRelaxedJsonEscaping, this produces spec-compliant JSON.
    internal static readonly JsonSerializerOptions DefaultJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    // Used by --strict-json: escapes all non-ASCII and control characters as \uXXXX.
    // Maximally portable for piping into tools that don't handle raw Unicode.
    internal static readonly JsonSerializerOptions StrictJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Default
    };

    /// <summary>
    ///     Returns a list of source-location dictionaries (file + line) for the given symbol.
    /// </summary>
    internal static List<Dictionary<string, object?>> ExtractSourceLocations(ISymbol symbol)
    {
        return symbol.Locations
            .Where(l => l.IsInSource)
            .Select(l =>
            {
                var span = l.GetLineSpan();
                return new Dictionary<string, object?>
                {
                    ["file"] = span.Path,
                    ["line"] = span.StartLinePosition.Line + 1
                };
            })
            .ToList();
    }

    /// <summary>
    ///     Returns a list of member dictionaries for the given named type.
    ///     Each entry includes "lineFrom" / "lineTo" pulled from the symbol's first source location
    ///     (1-indexed; null when the symbol has no source location, e.g. compiler-generated members).
    ///     When <paramref name="includeStaticFlag" /> is true each entry also includes an "isStatic" key.
    /// </summary>
    internal static List<Dictionary<string, object?>> ExtractMembers(
        INamedTypeSymbol type,
        bool includeStaticFlag = true)
    {
        return type.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Select(m =>
            {
                var (lineFrom, lineTo) = GetSourceLineRange(m);
                var entry = new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["kind"] = m.Kind.ToString(),
                    ["signature"] = m.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat),
                    ["accessibility"] = m.DeclaredAccessibility.ToString(),
                    ["lineFrom"] = lineFrom,
                    ["lineTo"] = lineTo
                };
                if (includeStaticFlag)
                    entry["isStatic"] = m.IsStatic;
                var attrs = m.GetAttributes()
                    .Where(a => a.AttributeClass != null)
                    .Select(FormatAttribute)
                    .ToList();
                if (attrs.Count > 0)
                    entry["attributes"] = attrs;
                return entry;
            })
            .ToList();

        static string FormatAttribute(AttributeData attr)
        {
            var name = attr.AttributeClass!.Name;
            if (name.EndsWith("Attribute", StringComparison.Ordinal) && name.Length > 9)
                name = name[..^9];
            var parts = new List<string>();
            foreach (var arg in attr.ConstructorArguments)
                parts.Add(FormatConstant(arg));
            foreach (var (key, val) in attr.NamedArguments)
                parts.Add($"{key} = {FormatConstant(val)}");
            return parts.Count == 0 ? name : $"{name}({string.Join(", ", parts)})";
        }

        static string FormatConstant(TypedConstant c)
        {
            if (c.IsNull) return "null";
            if (c.Kind == TypedConstantKind.Array)
                return $"new[] {{ {string.Join(", ", c.Values.Select(FormatConstant))} }}";
            return c.Value is string s ? $"\"{s}\"" : c.Value?.ToString() ?? "null";
        }
    }

    /// <summary>
    ///     Returns the 1-indexed start and end line of the symbol's first declaration. Uses
    ///     <see cref="ISymbol.DeclaringSyntaxReferences" /> rather than <see cref="ISymbol.Locations" />
    ///     because Locations point only at the identifier token (so end == start), whereas the
    ///     declaring syntax node spans the entire declaration including its body.
    ///     Both values are null when the symbol has no source declaration (compiler-generated, etc.).
    /// </summary>
    internal static (int? LineFrom, int? LineTo) GetSourceLineRange(ISymbol symbol)
    {
        var syntaxRef = symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (syntaxRef is null) return (null, null);
        var span = syntaxRef.GetSyntax().GetLocation().GetLineSpan();
        return (span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    /// <summary>
    ///     Populates <paramref name="result" /> with "baseType" and "interfaces" keys from <paramref name="type" />.
    ///     "baseType" is omitted when the base is System.Object.
    ///     "interfaces" is omitted when the list is empty.
    /// </summary>
    internal static void AddTypeHierarchyInfo(
        Dictionary<string, object?> result,
        INamedTypeSymbol type)
    {
        if (type.BaseType != null && type.BaseType.SpecialType != SpecialType.System_Object)
            result["baseType"] = type.BaseType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);

        var interfaces = type.Interfaces
            .Select(i => i.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat))
            .ToList();
        if (interfaces.Count > 0)
            result["interfaces"] = interfaces;
    }
}
