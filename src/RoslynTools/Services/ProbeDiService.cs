using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Status of a registration the probe examined.
/// </summary>
internal static class ProbeDiStatus
{
    /// <summary>Registered at runtime, but nothing in source refers to the implementation.</summary>
    internal const string Invisible = "invisible";

    /// <summary>Registered through a factory delegate, so no ImplementationType is exposed to read.</summary>
    internal const string Unprobeable = "unprobeable";
}

/// <summary>
/// One registration the probe observed, and whether static analysis can see the implementation.
/// </summary>
internal sealed record ProbeDiRegistration(string ServiceType, string? ImplementationType, string Status);

/// <summary>
/// Executes a single DI registration method and reports which registered implementations carry
/// no static reference and no reflective-use annotation.
///
/// This is a generator, not part of the analysis path: it exists so a maintainer can backfill
/// annotations once (see rt refactor add-dynamic-dependency), after which purely-static
/// find-unused stays correct on its own. find-unused never calls it, and never executes user code.
///
/// Blast radius is deliberately narrow: the named registration method is invoked against a fresh
/// ServiceCollection. The application host is never booted.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Resolved from DI as an IRtService instance, so the entry point stays an instance method.")]
internal sealed class ProbeDiService : IRtService
{
    /// <summary>Attribute marking a type as used by something static analysis cannot see.</summary>
    private const string UsedImplicitlyAttribute = "UsedImplicitlyAttribute";

    private const string IlLinkDescriptorExtension = ".xml";

    /// <summary>
    /// Probes <paramref name="entrypoint"/> (in Type.Method form) and returns every registration
    /// whose implementation is invisible to static analysis, plus those that cannot be probed.
    /// </summary>
    internal async Task<IReadOnlyList<ProbeDiRegistration>> ProbeAsync(Solution solution, string entrypoint)
    {
        var (typeName, methodName) = SplitEntrypoint(entrypoint);
        var (project, entrypointSymbol) = await FindEntrypointAsync(solution, typeName, methodName);

        var assemblyPath = project.OutputFilePath;
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
            throw new InvalidOperationException(
                $"The built assembly for project '{project.Name}' was not found. Build the solution before running probe-di.");

        var loadContext = new AssemblyLoadContext($"rt-probe-di-{Guid.NewGuid():N}", isCollectible: true);
        var directory = Path.GetDirectoryName(assemblyPath)!;
        loadContext.Resolving += (_, name) =>
        {
            var dependency = Path.Combine(directory, $"{name.Name}.dll");
            return File.Exists(dependency) ? loadContext.LoadFromAssemblyPath(dependency) : null;
        };

        try
        {
            var services = InvokeRegistration(loadContext, assemblyPath, entrypointSymbol, entrypoint, methodName);
            return await ClassifyAsync(solution, services);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    /// <summary>
    /// Splits "Namespace.Type.Method" on its last dot. Anything without a type and a method part
    /// is a user input error, not a bug.
    /// </summary>
    internal static (string TypeName, string MethodName) SplitEntrypoint(string entrypoint)
    {
        var separator = entrypoint.LastIndexOf('.');
        if (separator <= 0 || separator == entrypoint.Length - 1)
            throw new ArgumentException("--entrypoint must use the form Type.Method.", nameof(entrypoint));

        return (entrypoint[..separator], entrypoint[(separator + 1)..]);
    }

    private static ServiceCollection InvokeRegistration(
        AssemblyLoadContext loadContext,
        string assemblyPath,
        IMethodSymbol entrypointSymbol,
        string entrypoint,
        string methodName)
    {
        var assembly = loadContext.LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var runtimeType = assembly.GetType(entrypointSymbol.ContainingType.ToDisplayString(), throwOnError: true)!;
        var methods = runtimeType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(method => method.Name == methodName)
            .Where(method => method.GetParameters() is [{ ParameterType: var parameterType }]
                             && typeof(IServiceCollection).IsAssignableFrom(parameterType))
            .ToArray();

        if (methods.Length != 1)
            throw new InvalidOperationException(
                $"'{entrypoint}' must identify one static method whose only parameter is IServiceCollection.");

        var services = new ServiceCollection();
        methods[0].Invoke(null, [services]);
        return services;
    }

    private static async Task<IReadOnlyList<ProbeDiRegistration>> ClassifyAsync(
        Solution solution,
        ServiceCollection services)
    {
        var findings = new List<ProbeDiRegistration>();
        foreach (var descriptor in services)
        {
            var serviceType = descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name;

            // A factory registration exposes no ImplementationType, so there is nothing to look up.
            // Report it rather than skipping: silence would read as "nothing to see here".
            if (descriptor.ImplementationType == null)
            {
                if (descriptor.ImplementationFactory != null)
                    findings.Add(new ProbeDiRegistration(serviceType, null, ProbeDiStatus.Unprobeable));
                continue;
            }

            var implementationName = descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name;
            var symbol = await FindTypeAsync(solution, implementationName);
            if (symbol == null
                || await HasStaticReferenceAsync(solution, symbol)
                || HasReflectiveUseAnnotation(symbol)
                || await HasIlLinkDescriptorAsync(solution, implementationName))
                continue;

            findings.Add(new ProbeDiRegistration(serviceType, implementationName, ProbeDiStatus.Invisible));
        }

        return findings;
    }

    internal static async Task<(Project Project, IMethodSymbol Method)> FindEntrypointAsync(
        Solution solution,
        string typeName,
        string methodName)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null)
                continue;

            var type = compilation.GetTypeByMetadataName(typeName)
                       ?? compilation.GetSymbolsWithName(typeName.Split('.').Last(), SymbolFilter.Type)
                           .OfType<INamedTypeSymbol>()
                           .FirstOrDefault(candidate =>
                               candidate.ToDisplayString() == typeName || candidate.Name == typeName);
            var method = type?.GetMembers(methodName).OfType<IMethodSymbol>().FirstOrDefault();
            if (method != null)
                return (project, method);
        }

        throw new InvalidOperationException($"Registration entrypoint '{typeName}.{methodName}' was not found.");
    }

    private static async Task<INamedTypeSymbol?> FindTypeAsync(Solution solution, string metadataName)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync();
            var type = compilation?.GetTypeByMetadataName(metadataName);
            if (type != null)
                return type;
        }

        return null;
    }

    private static async Task<bool> HasStaticReferenceAsync(Solution solution, INamedTypeSymbol symbol)
    {
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution);
        return references.Any(reference => reference.Locations.Any());
    }

    private static bool HasReflectiveUseAnnotation(INamedTypeSymbol symbol) =>
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == UsedImplicitlyAttribute);

    private static async Task<bool> HasIlLinkDescriptorAsync(Solution solution, string metadataName)
    {
        foreach (var document in solution.Projects.SelectMany(project => project.AdditionalDocuments))
        {
            if (!document.Name.EndsWith(IlLinkDescriptorExtension, StringComparison.OrdinalIgnoreCase))
                continue;

            var text = await document.GetTextAsync();
            if (text.ToString().Contains(metadataName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
