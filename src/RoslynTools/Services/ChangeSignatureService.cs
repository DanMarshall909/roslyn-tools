using System.Diagnostics.CodeAnalysis;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Rename;

namespace RoslynTools.Services;

/// <summary>
/// Provides methods to rename, reorder, and add parameters in method signatures.
/// </summary>
[SuppressMessage("Performance", "CA1822:Mark members as static",
    Justification = "Instance class created by rt destatic-class migration.")]
internal class ChangeSignatureService : IRtService
{
    private readonly AddParamService _addParam;
    private readonly RemoveParamService _removeParam;
    private readonly ReorderParamsService _reorderParams;
    private readonly ISymbolResolutionService _symbols;

    public ChangeSignatureService()
        : this(new ReorderParamsService(), new AddParamService(), new RemoveParamService())
    {
    }

    public ChangeSignatureService(
        ReorderParamsService reorderParams,
        AddParamService addParam,
        RemoveParamService removeParam,
        ISymbolResolutionService? symbols = null)
    {
        _symbols = symbols ?? new SymbolResolutionService();

        _reorderParams = reorderParams;
        _addParam = addParam;
        _removeParam = removeParam;
    }

    /// <summary>
    /// Renames a parameter in a method signature.
    /// </summary>
    internal async Task<Solution> RenameParamAsync(
        Solution solution,
        string methodSpec,
        string oldName,
        string newName)
    {
        var methodSymbol = await _symbols.FindMethodBySpecAsync(solution, methodSpec);

        var parameter = methodSymbol.Parameters.FirstOrDefault(param => param.Name == oldName)
                        ?? throw new InvalidOperationException(
                            $"Parameter '{oldName}' was not found on '{methodSpec}'.");

        if (methodSymbol.Parameters.Any(param => param.Name == newName))
            throw new InvalidOperationException(
                $"Method '{methodSpec}' already has a parameter named '{newName}'.");

        var options = new SymbolRenameOptions(
            false,
            false,
            false,
            false);

        return await Renamer.RenameSymbolAsync(solution, parameter, options, newName);
    }

    internal Task<Solution> ReorderParamsAsync(
        Solution solution,
        string methodSpec,
        string[] newOrder)
    {
        return _reorderParams.ReorderParamsAsync(solution, methodSpec, newOrder);
    }

    /// <summary>
    /// Adds a new parameter to a method signature.
    /// </summary>
    internal Task<Solution> AddParamAsync(
        Solution solution,
        string methodSpec,
        string paramSpec,
        string? afterParam)
    {
        return _addParam.AddParamAsync(solution, methodSpec, paramSpec, afterParam);
    }

    /// <summary>
    /// Removes a parameter from a method signature.
    /// </summary>
    internal Task<Solution> RemoveParamAsync(
        Solution solution,
        string methodSpec,
        string paramName,
        bool force)
    {
        return _removeParam.RemoveParamAsync(solution, methodSpec, paramName, force);
    }
}
