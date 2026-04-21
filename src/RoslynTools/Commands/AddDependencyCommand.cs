using System.CommandLine;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Commands;

internal sealed class AddDependencyCommand : IRtCommand
{
    private static readonly SymbolDisplayFormat TypeDisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    private sealed record ResolvedTarget(
        Document Document,
        SyntaxNode Root,
        SemanticModel SemanticModel,
        ClassDeclarationSyntax Declaration,
        INamedTypeSymbol Symbol);

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
        cmd.AddAll(typeOpt, depOpt, fieldOpt, solutionOpt, dryRunOpt);

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
            await ExecuteCoreAsync(solution, typeName, depType, fieldName, dryRun);
        }
    }

    internal static async Task ExecuteCoreAsync(Solution solution, string typeName, string depType, string? fieldName, bool dryRun)
    {
        var originalSolution = solution;
        var resolvedField = fieldName ?? DeriveFieldName(depType);
        var paramName = resolvedField.TrimStart('_');

        var target = await ResolveTargetClassAsync(solution, typeName);
        if (target is null)
            return;

        var dependencyTypeSymbol = ResolveDependencyTypeSymbol(target.SemanticModel, target.Declaration, depType);
        var injector = new DependencyInjector(
            typeName,
            depType,
            resolvedField,
            paramName,
            target.SemanticModel,
            dependencyTypeSymbol,
            target.Symbol);

        var newRoot = injector.Visit(target.Root);

        switch (injector.Status)
        {
            case DependencyInjectionStatus.ClassNotFound:
                Console.Error.WriteLine($"Class '{typeName}' not found in solution.");
                Environment.ExitCode = 1;
                return;

            case DependencyInjectionStatus.AlreadyInjected:
                Console.Error.WriteLine($"Dependency '{depType}' is already injected into '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
                return;

            case DependencyInjectionStatus.Unsupported:
                Console.Error.WriteLine(injector.ValidationError ??
                    $"Cannot inject '{depType}' into '{target.Symbol.ToDisplayString(TypeDisplayFormat)}'.");
                Environment.ExitCode = 1;
                return;

            case DependencyInjectionStatus.Applied:
                solution = solution.WithDocumentSyntaxRoot(target.Document.Id, newRoot!);
                await SolutionWriter.ApplyChangesAsync(originalSolution, solution, dryRun);
                Console.Error.WriteLine($"Injected {depType} into {target.Symbol.ToDisplayString(TypeDisplayFormat)}.");
                return;

            default:
                throw new InvalidOperationException($"Unknown dependency injection status '{injector.Status}'.");
        }
    }

    private static async Task<ResolvedTarget?> ResolveTargetClassAsync(Solution solution, string typeName)
    {
        var normalizedTypeName = NormalizeTypeName(typeName);
        var isQualifiedName = normalizedTypeName.Contains('.', StringComparison.Ordinal);
        var matches = new List<ResolvedTarget>();

        foreach (var project in solution.Projects)
        {
            foreach (var document in project.Documents)
            {
                var root = await document.GetSyntaxRootAsync();
                if (root is null)
                    continue;

                var semanticModel = await document.GetSemanticModelAsync();
                if (semanticModel is null)
                    continue;

                foreach (var declaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                {
                    if (semanticModel.GetDeclaredSymbol(declaration) is not INamedTypeSymbol symbol)
                        continue;

                    if (!IsTypeMatch(symbol, normalizedTypeName, isQualifiedName))
                        continue;

                    matches.Add(new ResolvedTarget(document, root, semanticModel, declaration, symbol));
                }
            }
        }

        if (matches.Count == 0)
        {
            Console.Error.WriteLine($"Class '{typeName}' not found in solution.");
            Environment.ExitCode = 1;
            return null;
        }

        if (matches.Count > 1)
        {
            var candidateList = string.Join(", ", matches
                .Select(m => m.Symbol.ToDisplayString(TypeDisplayFormat))
                .Distinct(StringComparer.Ordinal));
            Console.Error.WriteLine($"Class '{typeName}' is ambiguous across the loaded solution: {candidateList}. Pass a fully qualified type name.");
            Environment.ExitCode = 1;
            return null;
        }

        return matches[0];
    }

    private static ITypeSymbol? ResolveDependencyTypeSymbol(
        SemanticModel semanticModel,
        ClassDeclarationSyntax declaration,
        string depType)
    {
        var typeSyntax = SyntaxFactory.ParseTypeName(depType);
        var speculative = semanticModel.GetSpeculativeTypeInfo(
            declaration.OpenBraceToken.SpanStart,
            typeSyntax,
            SpeculativeBindingOption.BindAsTypeOrNamespace);

        if (speculative.Type is not null)
            return speculative.Type;

        return semanticModel.LookupNamespacesAndTypes(declaration.Identifier.SpanStart, name: depType)
            .OfType<ITypeSymbol>()
            .FirstOrDefault();
    }

    private static bool IsTypeMatch(INamedTypeSymbol symbol, string normalizedTypeName, bool isQualifiedName)
    {
        if (isQualifiedName)
            return NormalizeTypeName(symbol.ToDisplayString(TypeDisplayFormat)) == normalizedTypeName;

        return symbol.Name == normalizedTypeName;
    }

    private static string NormalizeTypeName(string typeName) =>
        typeName.Replace("global::", string.Empty, StringComparison.Ordinal).Trim();

    private static string DeriveFieldName(string depType)
    {
        var name = depType;
        if (name.Length > 1 && name[0] == 'I' && char.IsUpper(name[1]))
            name = name[1..];

        name = char.ToLower(name[0], CultureInfo.InvariantCulture) + name[1..];
        return "_" + name;
    }
}
