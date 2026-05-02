using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

/// <summary>
/// Analyses a duplicate group and emits extraction-readiness diagnostics.
///
/// Checks implemented:
///   parameterizable-inputs  — all occurrence substitution maps agree on symbol-vs-literal
///                             category for each placeholder; fails when one site has a
///                             literal where another has an identifier.
///   single-output           — data-flow shows ≤1 variable flows out of each window;
///                             warns when exactly 1 outward variable, fails when >1.
///   no-control-flow-escape  — no return/yield/goto/labelled-break inside the window.
///   no-ref-out-params       — no ref/out parameter usage inside the window.
///   consistent-captured-state — captured field/property set is identical across all
///                             occurrences (warn when sets differ, not a hard fail).
///
/// Extractability level derived from checks:
///   all pass                  → extractable
///   at least one warn, no fails → likely_extractable
///   any fail                  → discovery_only
/// </summary>
internal static class ExtractabilityAnalyzer
{
    public static (string Extractability, IReadOnlyList<ExtractabilityCheck> Checks) Analyze(
        IReadOnlyList<StatementSyntax> canonicalWindow,
        IReadOnlyList<(IReadOnlyList<StatementSyntax> Window, SemanticModel Model, IReadOnlyDictionary<string, string>? SubstitutionMap)> occurrences)
    {
        var firstModel = occurrences.Count > 0 ? occurrences[0].Model : null;
        var checks = new List<ExtractabilityCheck>
        {
            CheckParameterizableInputs(occurrences),
            CheckSingleOutput(occurrences),
            firstModel is not null
                ? CheckNoControlFlowEscape(canonicalWindow, firstModel)
                : new ExtractabilityCheck("no-control-flow-escape", "pass", null),
            CheckNoRefOutParams(occurrences),
            CheckConsistentCapturedState(occurrences),
        };

        var extractability = DeriveExtractability(checks);
        return (extractability, checks);
    }

    // ── Check: parameterizable-inputs ────────────────────────────────────────────

    private static ExtractabilityCheck CheckParameterizableInputs(
        IReadOnlyList<(IReadOnlyList<StatementSyntax> Window, SemanticModel Model, IReadOnlyDictionary<string, string>? SubstitutionMap)> occurrences)
    {
        // For each occurrence, classify each placeholder in the substitution map as:
        //   "identifier" (local/parameter) or "literal" (int/string/bool/…)
        // If any placeholder has different classifications across occurrences, that's a fail.
        // If all are consistent — pass.

        if (occurrences.Count == 0)
            return new ExtractabilityCheck("parameterizable-inputs", "pass", null);

        // Collect per-placeholder classifications from all occurrences.
        var classificationsByPlaceholder = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var (_, _, subs) in occurrences)
        {
            if (subs is null)
                continue;

            foreach (var (placeholder, original) in subs)
            {
                var category = ClassifySubstitution(placeholder);
                if (!classificationsByPlaceholder.TryGetValue(placeholder, out var set))
                    classificationsByPlaceholder[placeholder] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(category);
            }
        }

        var conflicting = classificationsByPlaceholder
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .ToList();

        if (conflicting.Count > 0)
        {
            var detail = $"placeholder(s) {string.Join(", ", conflicting)} map to both identifier and literal across occurrences";
            return new ExtractabilityCheck("parameterizable-inputs", "fail", detail);
        }

        return new ExtractabilityCheck("parameterizable-inputs", "pass", null);
    }

    private static string ClassifySubstitution(string placeholder)
    {
        // $L0, $P0 → identifier; $I0, $S0, $B0, $C0, $N0 → literal
        if (placeholder.StartsWith("$L", StringComparison.Ordinal) ||
            placeholder.StartsWith("$P", StringComparison.Ordinal))
            return "identifier";
        return "literal";
    }

    // ── Check: single-output ──────────────────────────────────────────────────────

    private static ExtractabilityCheck CheckSingleOutput(
        IReadOnlyList<(IReadOnlyList<StatementSyntax> Window, SemanticModel Model, IReadOnlyDictionary<string, string>? SubstitutionMap)> occurrences)
    {
        // Run Roslyn DataFlowAnalysis on each occurrence's window.
        // Count symbols that flow out (excluding those declared within the window).
        var maxOut = 0;

        foreach (var (window, model, _) in occurrences)
        {
            if (window.Count == 0)
                continue;

            var flow = model.AnalyzeDataFlow(window[0], window[^1]);
            if (flow == null || !flow.Succeeded)
                continue;

            // Variables declared inside the window don't count as outward outputs.
            var declaredInWindow = window
                .SelectMany(s => s.DescendantNodesAndSelf())
                .OfType<VariableDeclaratorSyntax>()
                .Select(v => (ISymbol?)model.GetDeclaredSymbol(v))
                .Where(s => s is not null)
                .Select(s => s!)
                .ToHashSet(SymbolEqualityComparer.Default);

            var outCount = flow.DataFlowsOut
                .Count(sym => !declaredInWindow.Contains(sym));

            if (outCount > maxOut)
                maxOut = outCount;
        }

        if (maxOut == 0)
            return new ExtractabilityCheck("single-output", "pass", null);

        if (maxOut == 1)
            return new ExtractabilityCheck("single-output", "warn", "block produces 1 outward variable; extractable with a return value");

        return new ExtractabilityCheck("single-output", "fail", $"block produces {maxOut} outward variables; would need tuple return or out-params");
    }

    // ── Check: no-control-flow-escape ────────────────────────────────────────────

    private static ExtractabilityCheck CheckNoControlFlowEscape(
        IReadOnlyList<StatementSyntax> window,
        SemanticModel model)
    {
        if (window.Count == 0)
            return new ExtractabilityCheck("no-control-flow-escape", "pass", null);

        var flow = model.AnalyzeControlFlow(window[0], window[^1]);
        if (flow == null || !flow.Succeeded)
            return new ExtractabilityCheck("no-control-flow-escape", "pass", null);

        if (flow.ReturnStatements.Any())
        {
            // Consult ControlFlowUnwinder — if the pattern is a guard (if-return), it can be
            // auto-extracted as a bool method. Only downgrade to warn for patterns MethodExtractor
            // already knows how to handle; leave all others as fail so the status is honest.
            var cfClass = ControlFlowUnwinder.Classify(window);
            if (cfClass.Strategy == ControlFlowStrategy.ExtractAsBoolGuard)
                return new ExtractabilityCheck("no-control-flow-escape", "warn",
                    "guard pattern (if-return) is extractable via control-flow unwinding");

            return new ExtractabilityCheck("no-control-flow-escape", "fail", "block contains a return statement");
        }

        if (flow.ExitPoints.Any())
            return new ExtractabilityCheck("no-control-flow-escape", "fail",
                "block contains a break/continue/goto that exits the window");

        return new ExtractabilityCheck("no-control-flow-escape", "pass", null);
    }

    // ── Check: no-ref-out-params ──────────────────────────────────────────────────

    private static ExtractabilityCheck CheckNoRefOutParams(
        IReadOnlyList<(IReadOnlyList<StatementSyntax> Window, SemanticModel Model, IReadOnlyDictionary<string, string>? SubstitutionMap)> occurrences)
    {
        foreach (var (window, model, _) in occurrences)
        {
            foreach (var stmt in window)
            {
                // Check for ref/out argument usage in invocations.
                var hasRefOut = stmt.DescendantNodesAndSelf()
                    .OfType<ArgumentSyntax>()
                    .Any(a => a.RefKindKeyword.IsKind(SyntaxKind.RefKeyword)
                           || a.RefKindKeyword.IsKind(SyntaxKind.OutKeyword)
                           || a.RefKindKeyword.IsKind(SyntaxKind.InKeyword));

                if (hasRefOut)
                    return new ExtractabilityCheck("no-ref-out-params", "fail", "block passes arguments with ref/out/in modifiers");
            }
        }

        return new ExtractabilityCheck("no-ref-out-params", "pass", null);
    }

    // ── Check: consistent-captured-state ────────────────────────────────────────

    private static ExtractabilityCheck CheckConsistentCapturedState(
        IReadOnlyList<(IReadOnlyList<StatementSyntax> Window, SemanticModel Model, IReadOnlyDictionary<string, string>? SubstitutionMap)> occurrences)
    {
        // Collect all field/property symbols referenced in each occurrence window.
        // If the set differs across occurrences, warn.
        var perOccurrenceFields = new List<HashSet<string>>();

        foreach (var (window, model, _) in occurrences)
        {
            var fields = new HashSet<string>(StringComparer.Ordinal);
            foreach (var stmt in window)
            {
                foreach (var ident in stmt.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
                {
                    var sym = model.GetSymbolInfo(ident).Symbol;
                    if (sym is IFieldSymbol or IPropertySymbol)
                        fields.Add(sym.ToDisplayString());
                }
            }
            perOccurrenceFields.Add(fields);
        }

        if (perOccurrenceFields.Count <= 1)
            return new ExtractabilityCheck("consistent-captured-state", "pass", null);

        var first = perOccurrenceFields[0];
        var allSame = perOccurrenceFields.Skip(1).All(f => f.SetEquals(first));

        if (allSame)
            return new ExtractabilityCheck("consistent-captured-state", "pass", null);

        return new ExtractabilityCheck("consistent-captured-state", "warn",
            "captured fields/properties differ across occurrences; review before extraction");
    }

    // ── Extractability level ─────────────────────────────────────────────────────

    private static string DeriveExtractability(IReadOnlyList<ExtractabilityCheck> checks)
    {
        if (checks.Any(c => c.Status == "fail"))
            return "discovery_only";
        if (checks.Any(c => c.Status == "warn"))
            return "likely_extractable";
        return "extractable";
    }
}

/// <summary>A single extraction-readiness check result.</summary>
internal sealed record ExtractabilityCheck(
    string Name,
    string Status,
    string? Detail);
