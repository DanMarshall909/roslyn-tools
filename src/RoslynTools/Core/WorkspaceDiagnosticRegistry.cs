using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace RoslynTools.Core;

/// <summary>Retains workspace events throughout acquisition and deferred compilation, independently of text rendering.</summary>
internal static class WorkspaceDiagnosticRegistry
{
    private static readonly ConditionalWeakTable<Workspace, Entry> Entries = new();

    internal static void Capture(Workspace workspace)
    {
        var entry = Entries.GetValue(workspace, _ => new Entry());
        lock (entry)
        {
            if (entry.Registered)
                return;
            workspace.RegisterWorkspaceFailedHandler(arguments => Record(workspace, arguments.Diagnostic));
            entry.Registered = true;
        }
    }

    internal static void Record(Workspace workspace, WorkspaceDiagnostic diagnostic)
    {
        var entry = Entries.GetValue(workspace, _ => new Entry());
        lock (entry)
        {
            entry.Original.Add(diagnostic);
            entry.Structured.Add(new WorkspaceAcquisitionDiagnostic(
                $"workspace.{entry.Structured.Count + 1}",
                diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? DiagnosticSeverity.Error : DiagnosticSeverity.Warning,
                diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                    ? CompletenessDiagnosticImpact.RequiredInputIncomplete
                    : CompletenessDiagnosticImpact.Unknown,
                "workspace", diagnostic.Message));
        }
    }

    internal static void Record(Workspace workspace, WorkspaceAcquisitionDiagnostic diagnostic)
    {
        var entry = Entries.GetValue(workspace, _ => new Entry());
        lock (entry)
            entry.Structured.Add(diagnostic);
    }

    internal static IReadOnlyList<WorkspaceDiagnostic> Get(Workspace workspace)
    {
        if (!Entries.TryGetValue(workspace, out var entry))
            return [];
        lock (entry)
            return entry.Original.ToImmutableArray();
    }

    internal static ImmutableArray<WorkspaceAcquisitionDiagnostic> GetStructured(Workspace workspace)
    {
        if (!Entries.TryGetValue(workspace, out var entry))
            return [];
        lock (entry)
            return entry.Structured.ToImmutableArray();
    }

    private sealed class Entry
    {
        internal bool Registered { get; set; }
        internal List<WorkspaceDiagnostic> Original { get; } = [];
        internal List<WorkspaceAcquisitionDiagnostic> Structured { get; } = [];
    }
}
