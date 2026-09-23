using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>Compatibility carrier while session factories migrate to typed acquisition results.</summary>
internal static class WorkspaceAcquisitionRegistry
{
    private static readonly ConditionalWeakTable<Workspace, Entry> Entries = new();

    internal static void Declare(Workspace workspace, SemanticInputManifest manifest, WorkspaceEvaluatorProvenance? provenance = null)
    {
        var entry = Entries.GetValue(workspace, _ => new Entry());
        lock (entry)
        {
            if (entry.Manifest is not null)
                throw new InvalidOperationException("Workspace request scope was already declared.");
            entry.Manifest = manifest;
            entry.Provenance = provenance;
        }
    }

    internal static void Attach(Workspace workspace, WorkspaceAcquisitionReceipt receipt)
    {
        if (!ReferenceEquals(workspace, receipt.Solution.Workspace))
            throw new ArgumentException("Acquisition evidence belongs to a different workspace.", nameof(receipt));
        var entry = Entries.GetValue(workspace, _ => new Entry());
        lock (entry)
        {
            if (entry.Manifest is not null && !ReferenceEquals(entry.Manifest, receipt.Manifest))
                throw new InvalidOperationException("Acquisition evidence cannot replace the declared request manifest.");
            entry.Manifest = receipt.Manifest;
            entry.Receipt = receipt;
        }
    }

    internal static void RefineDeclaration(
        Workspace workspace,
        SemanticInputManifest current,
        SemanticInputManifest refined,
        WorkspaceEvaluatorProvenance? provenance = null)
    {
        var entry = Entries.GetValue(workspace, _ => new Entry());
        lock (entry)
        {
            if (!ReferenceEquals(entry.Manifest, current) || entry.Receipt is not null)
                throw new InvalidOperationException(
                    "Only the active acquisition collector can refine an unattached workspace manifest.");
            entry.Manifest = refined;
            if (provenance is not null)
                entry.Provenance = provenance;
        }
    }

    internal static (SemanticInputManifest? Manifest, WorkspaceAcquisitionReceipt? Receipt) Get(Workspace workspace)
    {
        if (!Entries.TryGetValue(workspace, out var entry))
            return (null, null);
        lock (entry)
            return (entry.Manifest, entry.Receipt);
    }

    internal static WorkspaceEvaluatorProvenance? GetProvenance(Workspace workspace)
    {
        if (!Entries.TryGetValue(workspace, out var entry))
            return null;
        lock (entry)
            return entry.Receipt?.Provenance ?? entry.Provenance;
    }

    private sealed class Entry
    {
        internal SemanticInputManifest? Manifest { get; set; }
        internal WorkspaceAcquisitionReceipt? Receipt { get; set; }
        internal WorkspaceEvaluatorProvenance? Provenance { get; set; }
    }
}
