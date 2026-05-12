using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

internal static class InterfaceTextBuilder
{
    internal static string BuildInterfaceText(
        string namespaceName,
        string interfaceName,
        IEnumerable<ISymbol> members)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(namespaceName))
        {
            sb.AppendLineF($"namespace {namespaceName};");
            sb.AppendLine();
        }

        sb.AppendLineF($"public interface {interfaceName}");
        sb.AppendLine("{");

        foreach (var member in members)
            switch (member)
            {
                case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary:
                    var returnType = method.ReturnType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var paramList = BuildParamList(method);
                    sb.AppendLineF($"    {returnType} {method.Name}({paramList});");
                    break;

                case IPropertySymbol property:
                    var propType = property.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    var accessors = BuildAccessors(property);
                    sb.AppendLineF($"    {propType} {property.Name} {{ {accessors} }}");
                    break;

                case IEventSymbol eventSymbol:
                    var eventType = eventSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                    sb.AppendLineF($"    event {eventType} {eventSymbol.Name};");
                    break;
            }

        sb.AppendLine("}");
        return sb.ToString();
    }

    internal static string BuildParamList(IMethodSymbol method)
    {
        return string.Join(", ", method.Parameters.Select(p =>
        {
            var typeName = p.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
            return $"{typeName} {p.Name}";
        }));
    }

    internal static string BuildAccessors(IPropertySymbol property)
    {
        var parts = new List<string>();
        if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
            parts.Add("get;");
        if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public })
            parts.Add("set;");
        return string.Join(" ", parts);
    }
}
