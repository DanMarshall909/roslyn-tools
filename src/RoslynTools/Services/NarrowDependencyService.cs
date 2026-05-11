using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using RoslynTools.Core;

namespace RoslynTools.Services;

/// <summary>
/// Manages dependencies within a solution by narrowing their scope.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class NarrowDependencyService : IRtService
{
    private readonly ConsumerNarrowerService _consumerNarrower;
    private readonly InterfaceAnalyzerService _interfaceAnalyzer;
    private readonly ILogger<NarrowDependencyService> _logger;
    private readonly ISolutionChangeWriter _solutionChangeWriter;
    private readonly ISymbolResolutionService _symbols;

    public NarrowDependencyService()
        : this(
            new RtConsoleLogger<NarrowDependencyService>(),
            new InterfaceAnalyzerService(),
            new ConsumerNarrowerService(),
            new SolutionChangeWriter())
    {
    }

    public NarrowDependencyService(
        ILogger<NarrowDependencyService> logger,
        InterfaceAnalyzerService interfaceAnalyzer,
        ConsumerNarrowerService consumerNarrower,
        ISolutionChangeWriter solutionChangeWriter,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _logger = logger;
        _solutionChangeWriter = solutionChangeWriter;
        _interfaceAnalyzer = interfaceAnalyzer;
        _consumerNarrower = consumerNarrower;
    }

    /// <summary>
    /// Asynchronously executes dependency resolution operations on a solution.
    /// </summary>
    internal async Task ExecuteAsync(
        Solution solution,
        string sourceInterfaceSpec,
        string[] memberNames,
        string newInterfaceName,
        bool dryRun)
    {
        // Resolve the source interface symbol
        var interfaceSymbol = await ResolveInterfaceAsync(solution, sourceInterfaceSpec);

        // Validate that all requested member names exist on the source interface
        ValidateMemberNames(interfaceSymbol, memberNames);

        // Collect the selected member symbols
        var selectedMembers = GetSelectedMembers(interfaceSymbol, memberNames);

        // Check if the target interface already exists and matches the desired shape
        var existingNarrow = await FindExistingInterfaceAsync(solution, newInterfaceName, selectedMembers);

        // Determine source file location for the new interface
        var interfaceFilePath = ResolveNewInterfaceFilePath(interfaceSymbol, newInterfaceName);

        var updatedSolution = solution;
        var newFileCreated = false;

        if (existingNarrow is null)
        {
            // Build new interface text via InterfaceAnalyzerService
            var targetNamespace = interfaceSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
            var interfaceText =
                _interfaceAnalyzer.BuildInterfaceText(targetNamespace, newInterfaceName, selectedMembers);

            // Find owning project
            var owningProject = FindOwningProject(solution, interfaceSymbol)
                                ?? throw new InvalidOperationException(
                                    $"Cannot find project containing '{interfaceSymbol.Name}'.");

            var interfaceSourceText = SourceText.From(interfaceText, Encoding.UTF8);
            updatedSolution = updatedSolution.AddDocument(
                DocumentId.CreateNewId(owningProject.Id),
                newInterfaceName + ".cs",
                interfaceSourceText,
                filePath: interfaceFilePath);

            newFileCreated = true;
        }

        // Analyze consumers and narrow those that only use the selected subset
        var narrowResult = await _consumerNarrower.NarrowConsumersAsync(
            solution,
            interfaceSymbol,
            selectedMembers,
            newInterfaceName,
            updatedSolution);

        updatedSolution = narrowResult.Solution;

        // Report results
        PrintReport(narrowResult, newInterfaceName, newFileCreated, existingNarrow, dryRun);

        // Apply or preview
        _logger.RtInfo("Applying narrow-dependency '{Name}' -> '{NewInterfaceName}'...", interfaceSymbol.Name,
            newInterfaceName);
        await _solutionChangeWriter.ApplyChangesAsync(solution, updatedSolution, dryRun);
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    private void ValidateMemberNames(INamedTypeSymbol interfaceSymbol, string[] memberNames)
    {
        var available = interfaceSymbol.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared)
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);

        var invalid = memberNames.Where(n => !available.Contains(n)).ToList();
        if (invalid.Count > 0)
            throw new InvalidOperationException(
                $"Member(s) [{string.Join(", ", invalid)}] not found on interface '{interfaceSymbol.Name}'. " +
                $"Available members: [{string.Join(", ", available.OrderBy(n => n, StringComparer.Ordinal))}].");
    }

    // -------------------------------------------------------------------------
    // Symbol helpers
    // -------------------------------------------------------------------------

    private async Task<INamedTypeSymbol> ResolveInterfaceAsync(Solution solution, string interfaceSpec)
    {
        var symbol = LooksFullyQualified(interfaceSpec)
            ? await _symbols.FindSymbolByFqnAsync(solution, interfaceSpec, "type")
            : await _symbols.FindSymbolAsync(solution, interfaceSpec, "type");

        if (symbol is not INamedTypeSymbol namedType)
            throw new InvalidOperationException($"'{interfaceSpec}' is not a named type.");

        if (namedType.TypeKind != TypeKind.Interface)
            throw new InvalidOperationException(
                $"'{namedType.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat)}' is not an interface.");

        return namedType;
    }

    private List<ISymbol> GetSelectedMembers(INamedTypeSymbol interfaceSymbol, string[] memberNames)
    {
        var nameSet = new HashSet<string>(memberNames, StringComparer.Ordinal);
        return interfaceSymbol.GetMembers()
            .Where(m => !m.IsImplicitlyDeclared && nameSet.Contains(m.Name))
            .ToList();
    }

    private async Task<INamedTypeSymbol?> FindExistingInterfaceAsync(
        Solution solution,
        string newInterfaceName,
        List<ISymbol> selectedMembers)
    {
        // Try to find a type with the new name in the solution
        foreach (var project in solution.Projects)
        {
            var found = await SymbolFinder.FindDeclarationsAsync(
                project, newInterfaceName, false, SymbolFilter.Type);

            var match = found
                .OfType<INamedTypeSymbol>()
                .FirstOrDefault(t => t.TypeKind == TypeKind.Interface && t.Name == newInterfaceName);

            if (match is not null)
                return match;
        }

        return null;
    }

    private Project? FindOwningProject(Solution solution, INamedTypeSymbol interfaceSymbol)
    {
        var sourceFilePath = interfaceSymbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        if (string.IsNullOrEmpty(sourceFilePath))
            return null;

        return solution.Projects.FirstOrDefault(p =>
            p.Documents.Any(d =>
                string.Equals(d.FilePath, sourceFilePath, StringComparison.OrdinalIgnoreCase)));
    }

    private string ResolveNewInterfaceFilePath(INamedTypeSymbol interfaceSymbol, string newInterfaceName)
    {
        var sourceFilePath = interfaceSymbol.Locations
            .Where(l => l.IsInSource)
            .Select(l => l.SourceTree?.FilePath)
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

        var dir = string.IsNullOrEmpty(sourceFilePath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(sourceFilePath)!;

        return Path.Combine(dir, newInterfaceName + ".cs");
    }

    // -------------------------------------------------------------------------
    // Reporting
    // -------------------------------------------------------------------------

    private void PrintReport(
        ConsumerNarrowerService.NarrowResult result,
        string newInterfaceName,
        bool newFileCreated,
        INamedTypeSymbol? reusedInterface,
        bool dryRun)
    {
        var modeLabel = dryRun ? "[dry-run] " : string.Empty;

        if (reusedInterface is not null)
            RtConsole.WriteLine(RtOutputContext.Result,
                $"{modeLabel}Reusing existing interface '{newInterfaceName}' -- shape matches selected members.");
        else
            RtConsole.WriteLine(RtOutputContext.Result,
                $"{modeLabel}New interface '{newInterfaceName}' will be created.");

        if (result.NarrowedConsumers.Count > 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result,
                $"\n{modeLabel}Narrowed {result.NarrowedConsumers.Count} consumer(s):");
            foreach (var consumer in result.NarrowedConsumers)
                RtConsole.WriteLine(RtOutputContext.Result, $"  - {consumer}");

            RtConsole.WriteLine(RtOutputContext.Result,
                $"\nNOTE: DI registration(s) for '{newInterfaceName}' must be updated manually. " +
                "Register the concrete implementation against the new narrower interface in your composition root.");
        }

        if (result.SkippedConsumers.Count > 0)
        {
            RtConsole.WriteLine(RtOutputContext.Result,
                $"\n{modeLabel}Skipped {result.SkippedConsumers.Count} consumer(s):");
            foreach (var (name, reason) in result.SkippedConsumers)
                RtConsole.WriteLine(RtOutputContext.Result, $"  - {name}: skipped ({reason})");
        }
    }

    // -------------------------------------------------------------------------
    // Utility
    // -------------------------------------------------------------------------

    private bool LooksFullyQualified(string symbolSpec)
    {
        return symbolSpec.StartsWith("global::", StringComparison.Ordinal)
               || symbolSpec.Contains('.', StringComparison.Ordinal);
    }
}
