using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>Prepares dependency narrowing changes without applying or rendering them.</summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class NarrowDependencyService : IRtService
{
    private readonly ConsumerNarrowerService _consumerNarrower;
    private readonly ISymbolResolutionService _symbols;

    public NarrowDependencyService()
        : this(new ConsumerNarrowerService(), new SymbolResolutionService())
    {
    }

    public NarrowDependencyService(
        ConsumerNarrowerService consumerNarrower,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();
        _consumerNarrower = consumerNarrower;
    }

    /// <summary>Calculates the new interface and compatible consumer rewrites without I/O.</summary>
    internal async Task<Result<Preparation, RtError>> PrepareAsync(
        Solution solution,
        string sourceInterfaceSpec,
        string[] memberNames,
        string newInterfaceName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var interfaceResult = await ResolveInterfaceAsync(solution, sourceInterfaceSpec);
        INamedTypeSymbol? interfaceSymbol = null;
        RtError? resolutionError = null;
        interfaceResult.Match(value => interfaceSymbol = value, error => resolutionError = error);
        if (resolutionError is not null)
            return Result<Preparation, RtError>.Err(resolutionError);

        var resolvedInterface = interfaceSymbol!;

        if (string.IsNullOrWhiteSpace(newInterfaceName))
            return Result<Preparation, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A new interface name is required.",
                new Dictionary<string, string> { ["newName"] = newInterfaceName }));

        var memberError = ValidateMemberNames(resolvedInterface, memberNames);
        if (memberError is not null)
            return Result<Preparation, RtError>.Err(memberError);

        var selectedMembers = GetSelectedMembers(resolvedInterface, memberNames);
        cancellationToken.ThrowIfCancellationRequested();
        var existingNarrow = await FindExistingInterfaceAsync(
            solution,
            newInterfaceName,
            cancellationToken);
        var updatedSolution = solution;
        var newFileCreated = false;

        if (existingNarrow is null)
        {
            var targetNamespace = resolvedInterface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var interfaceText = InterfaceTextBuilder.BuildInterfaceText(
                targetNamespace,
                newInterfaceName,
                selectedMembers);
            var owningProject = FindOwningProject(solution, resolvedInterface)
                                ?? throw new InvalidOperationException(
                                    $"Cannot find project containing '{resolvedInterface.Name}'.");
            var interfaceSourceText = SourceText.From(interfaceText, Encoding.UTF8);
            updatedSolution = updatedSolution.AddDocument(
                DocumentId.CreateNewId(owningProject.Id),
                newInterfaceName + ".cs",
                interfaceSourceText,
                filePath: ResolveNewInterfaceFilePath(resolvedInterface, newInterfaceName));
            newFileCreated = true;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var narrowResult = await _consumerNarrower.NarrowConsumersAsync(
            solution,
            resolvedInterface,
            selectedMembers,
            newInterfaceName,
            updatedSolution,
            cancellationToken);

        var narrowedConsumers = narrowResult.NarrowedConsumers
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var skippedConsumers = narrowResult.SkippedConsumers
            .OrderBy(consumer => consumer.Name, StringComparer.Ordinal)
            .Select(consumer => new NarrowDependencySkippedConsumer(consumer.Name, consumer.Reason))
            .ToArray();
        var diFollowUp = narrowedConsumers.Length == 0
            ? null
            : $"DI registration(s) for '{newInterfaceName}' must be updated manually. " +
              "Register the concrete implementation against the new narrower interface in your composition root.";

        return Result<Preparation, RtError>.Ok(new Preparation(
            narrowResult.Solution,
            resolvedInterface.Name,
            newInterfaceName,
            newFileCreated,
            existingNarrow is not null,
            narrowedConsumers,
            skippedConsumers,
            diFollowUp));
    }

    private RtError? ValidateMemberNames(INamedTypeSymbol interfaceSymbol, string[] memberNames)
    {
        if (memberNames.Length == 0 || memberNames.Any(string.IsNullOrWhiteSpace))
            return new RtError(
                RtErrorKind.InvalidInput,
                $"At least one member from interface '{interfaceSymbol.Name}' is required.",
                new Dictionary<string, string> { ["interface"] = interfaceSymbol.Name });

        var available = interfaceSymbol.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared)
            .Select(member => member.Name)
            .ToHashSet(StringComparer.Ordinal);
        var invalid = memberNames.Where(name => !available.Contains(name)).ToArray();
        if (invalid.Length == 0)
            return null;

        return new RtError(
            RtErrorKind.InvalidInput,
            $"Member(s) [{string.Join(", ", invalid)}] not found on interface '{interfaceSymbol.Name}'. " +
            $"Available members: [{string.Join(", ", available.OrderBy(name => name, StringComparer.Ordinal))}].",
            new Dictionary<string, string>
            {
                ["interface"] = interfaceSymbol.Name,
                ["invalidMembers"] = string.Join(",", invalid),
                ["availableMembers"] = string.Join(",", available.OrderBy(name => name, StringComparer.Ordinal))
            });
    }

    private async Task<Result<INamedTypeSymbol, RtError>> ResolveInterfaceAsync(
        Solution solution,
        string interfaceSpec)
    {
        if (string.IsNullOrWhiteSpace(interfaceSpec))
            return Result<INamedTypeSymbol, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                "A source interface is required.",
                new Dictionary<string, string> { ["interface"] = interfaceSpec }));

        var resolved = LooksFullyQualified(interfaceSpec)
            ? await _symbols.TryFindSymbolByFqnAsync(solution, interfaceSpec, "type")
            : await _symbols.TryFindSymbolAsync(solution, interfaceSpec, "type");
        ISymbol? symbol = null;
        RtError? error = null;
        resolved.Match(value => symbol = value, failure => error = failure);
        if (error is not null)
            return Result<INamedTypeSymbol, RtError>.Err(error);

        if (symbol is not INamedTypeSymbol namedType)
            return Result<INamedTypeSymbol, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"'{interfaceSpec}' is not a named type.",
                new Dictionary<string, string> { ["interface"] = interfaceSpec }));

        if (namedType.TypeKind != TypeKind.Interface)
            return Result<INamedTypeSymbol, RtError>.Err(new RtError(
                RtErrorKind.InvalidInput,
                $"'{namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' is not an interface.",
                new Dictionary<string, string> { ["interface"] = interfaceSpec }));

        return Result<INamedTypeSymbol, RtError>.Ok(namedType);
    }

    private static List<ISymbol> GetSelectedMembers(INamedTypeSymbol interfaceSymbol, string[] memberNames)
    {
        var nameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
        return interfaceSymbol.GetMembers()
            .Where(member => !member.IsImplicitlyDeclared && nameSet.Contains(member.Name))
            .ToList();
    }

    private static async Task<INamedTypeSymbol?> FindExistingInterfaceAsync(
        Solution solution,
        string newInterfaceName,
        CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var found = await SymbolFinder.FindDeclarationsAsync(
                project,
                newInterfaceName,
                false,
                SymbolFilter.Type,
                cancellationToken);
            var match = found
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(type => type.TypeKind == TypeKind.Interface && type.Name == newInterfaceName);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static Project? FindOwningProject(Solution solution, INamedTypeSymbol interfaceSymbol)
    {
        var sourceFilePath = interfaceSymbol.Locations
            .Where(location => location.IsInSource)
            .Select(location => location.SourceTree?.FilePath)
            .FirstOrDefault(path => !string.IsNullOrEmpty(path));
        return string.IsNullOrEmpty(sourceFilePath)
            ? null
            : solution.Projects.FirstOrDefault(project => project.Documents.Any(document =>
                string.Equals(document.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)));
    }

    private static string ResolveNewInterfaceFilePath(INamedTypeSymbol interfaceSymbol, string newInterfaceName)
    {
        var sourceFilePath = interfaceSymbol.Locations
            .Where(location => location.IsInSource)
            .Select(location => location.SourceTree?.FilePath)
            .FirstOrDefault(path => !string.IsNullOrEmpty(path));
        var directory = string.IsNullOrEmpty(sourceFilePath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(sourceFilePath)!;
        return Path.Combine(directory, newInterfaceName + ".cs");
    }

    private static bool LooksFullyQualified(string symbolSpec) =>
        symbolSpec.StartsWith("global::", StringComparison.Ordinal) || symbolSpec.Contains('.', StringComparison.Ordinal);

    internal sealed record Preparation(
        Solution Solution,
        string SourceInterface,
        string NewInterfaceName,
        bool InterfaceCreated,
        bool InterfaceReused,
        IReadOnlyList<string> NarrowedConsumers,
        IReadOnlyList<NarrowDependencySkippedConsumer> SkippedConsumers,
        string? DiRegistrationFollowUp);
}
