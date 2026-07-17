using System.CommandLine;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.Extensions.DependencyInjection;
using RoslynTools.Commands.Results;
using RoslynTools.Core;
using RoslynTools.Core.Output;

namespace RoslynTools.Commands;

/// <summary>Probes one registration method and reports implementations hidden from static analysis.</summary>
internal sealed class ProbeDiCommand : IRtCommand, IAnalyzeCommand, IStructuredOutputCommand
{
    private readonly IWorkspaceSessionFactory _sessionFactory;

    public ProbeDiCommand(IWorkspaceSessionFactory sessionFactory)
    {
        _sessionFactory = sessionFactory;
    }

    public static RtCommandInfo Info { get; } = new(
        "probe-di",
        false,
        true,
        "analyze",
        WhenToUse:
        "Auditing assembly-scanning dependency injection after a successful build. This explicitly executes only the selected registration method and never boots the application host.",
        Examples:
        [
            "rt analyze probe-di --entrypoint Bootstrap.RegisterServices --solution MyApp.slnx"
        ],
        SeeAlso: ["add-dynamic-dependency", "find-unused"]);

    public Command Build()
    {
        var entrypointOption = new Option<string>("--entrypoint", "Registration method in Type.Method form")
        {
            IsRequired = true
        };
        var binder = RtCommandFlagBinder.Create().WithSolution();
        var command = new Command("probe-di", "Execute one DI registration method and report invisible registrations");
        command.Add(entrypointOption);
        binder.AddTo(command);

        command.SetHandler(async context =>
        {
            var request = binder.Bind(context.ParseResult, flags => new Request(
                context.ParseResult.GetValueForOption(entrypointOption)!,
                flags.SolutionPath));
            var solution = await _sessionFactory.GetSolutionAsync(request.SolutionPath);
            var result = await ProbeAsync(solution, request.Entrypoint);
            RtResultRenderer.Write(RtResult<ProbeDiResult>.Ok(result));
        });

        return command;
    }

    private static async Task<ProbeDiResult> ProbeAsync(Solution solution, string entrypoint)
    {
        var separator = entrypoint.LastIndexOf('.');
        if (separator <= 0 || separator == entrypoint.Length - 1)
            throw new ArgumentException("--entrypoint must use the form Type.Method.", nameof(entrypoint));

        var typeName = entrypoint[..separator];
        var methodName = entrypoint[(separator + 1)..];
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

            var findings = new List<ProbeDiFinding>();
            foreach (var descriptor in services)
            {
                if (descriptor.ImplementationType == null)
                {
                    if (descriptor.ImplementationFactory != null)
                        findings.Add(ProbeDiFinding.Unprobeable(descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name));
                    continue;
                }

                var implementationName = descriptor.ImplementationType.FullName ?? descriptor.ImplementationType.Name;
                var symbol = await FindTypeAsync(solution, implementationName);
                if (symbol == null || await HasStaticReferenceAsync(solution, symbol) || HasReflectiveUseAnnotation(symbol)
                    || await HasIlLinkDescriptorAsync(solution, implementationName))
                    continue;

                findings.Add(ProbeDiFinding.Invisible(
                    descriptor.ServiceType.FullName ?? descriptor.ServiceType.Name,
                    implementationName,
                    entrypoint,
                    solution.FilePath));
            }

            return new ProbeDiResult { Findings = findings };
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static async Task<(Project Project, IMethodSymbol Method)> FindEntrypointAsync(
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
                           .SingleOrDefault(candidate => candidate.ToDisplayString() == typeName || candidate.Name == typeName);
            var method = type?.GetMembers(methodName).OfType<IMethodSymbol>().SingleOrDefault();
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
        symbol.GetAttributes().Any(attribute => attribute.AttributeClass?.Name == "UsedImplicitlyAttribute");

    private static async Task<bool> HasIlLinkDescriptorAsync(Solution solution, string metadataName)
    {
        foreach (var document in solution.Projects.SelectMany(project => project.AdditionalDocuments))
        {
            if (!document.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = await document.GetTextAsync();
            if (text.ToString().Contains(metadataName, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private sealed record Request(string Entrypoint, string? SolutionPath) : ISupportsSolutionOption;
}

internal sealed record ProbeDiFinding
{
    public required string ServiceType { get; init; }
    public string? ImplementationType { get; init; }
    public required string Status { get; init; }
    public required string Reason { get; init; }
    public SuggestedFix? SuggestedFix { get; init; }

    internal static ProbeDiFinding Unprobeable(string serviceType) => new()
    {
        ServiceType = serviceType,
        Status = "unprobeable",
        Reason = "The registration uses an implementation factory and has no ImplementationType."
    };

    internal static ProbeDiFinding Invisible(
        string serviceType,
        string implementationType,
        string root,
        string? solutionPath) => new()
    {
        ServiceType = serviceType,
        ImplementationType = implementationType,
        Status = "invisible",
        Reason = "The implementation is registered at runtime but has no source reference or reflective-use annotation.",
        SuggestedFix = new SuggestedFix
        {
            Tool = "rt",
            Ref = "add-dynamic-dependency",
            Description = "Add a durable static reference at the composition root.",
            CommandLine = $"rt refactor add-dynamic-dependency --type \"{implementationType}\" --root \"{root}\" --solution \"{solutionPath}\""
        }
    };
}

internal sealed record ProbeDiResult : IRtOutput, IRtJsonOutput
{
    [OutputField(OutputLevel.Llm, "findings")]
    public required IReadOnlyList<ProbeDiFinding> Findings { get; init; }

    public void RenderText(TextWriter writer)
    {
        for (var index = 0; index < Findings.Count; index++)
        {
            var finding = Findings[index];
            writer.Write($"{finding.Status,-12} {finding.ImplementationType ?? finding.ServiceType} — {finding.Reason}");
            if (finding.SuggestedFix != null)
            {
                writer.WriteLine();
                finding.SuggestedFix.RenderText(writer);
            }
            if (index < Findings.Count - 1)
                writer.WriteLine();
        }
    }

    public void RenderJson(TextWriter writer)
    {
        writer.Write(JsonSerializer.Serialize(new
        {
            findings = Findings.Select(finding => new
            {
                serviceType = finding.ServiceType,
                implementationType = finding.ImplementationType,
                status = finding.Status,
                reason = finding.Reason,
                suggestedFix = finding.SuggestedFix?.AsJsonObject()
            })
        }, SymbolFormatter.DefaultJsonOptions));
    }
}
