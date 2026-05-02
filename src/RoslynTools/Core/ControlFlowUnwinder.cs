using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Core;

// ── Enums ────────────────────────────────────────────────────────────────────

/// <summary>The control-flow pattern detected in a statement block.</summary>
internal enum ControlFlowPattern
{
    /// <summary>No control-flow escape — extraction is unblocked.</summary>
    None,

    /// <summary>
    /// Block is a single if-statement whose consequent is a <c>return &lt;value&gt;</c>.
    /// Extractable as a bool-returning guard method.
    /// </summary>
    GuardReturn,

    /// <summary>
    /// Block has an early <c>return;</c> (no value) inside a guard if — extractable as a bool guard.
    /// </summary>
    EarlyReturnVoid,

    /// <summary>
    /// Block contains one or more statements plus a trailing <c>return value;</c>.
    /// Extractable by changing the extracted method return type to match the returned value.
    /// </summary>
    ValueReturn,

    /// <summary>Block contains a <c>break</c> that exits a surrounding loop.</summary>
    BreakEscape,

    /// <summary>Block contains a <c>continue</c> that jumps to the next loop iteration.</summary>
    ContinueEscape,
}

/// <summary>The recommended extraction strategy for a detected pattern.</summary>
internal enum ControlFlowStrategy
{
    /// <summary>No strategy needed — block has no control-flow escape.</summary>
    NotNeeded,

    /// <summary>
    /// Extract the block as a <c>bool</c>-returning method; replace the call site with
    /// <c>if (NewMethod(...)) return [value];</c>.
    /// </summary>
    ExtractAsBoolGuard,

    /// <summary>
    /// Extract the block, inferring the correct return type from the return expression.
    /// The extracted method returns that type; the call site becomes
    /// <c>return NewMethod(...);</c>.
    /// </summary>
    ExtractWithReturnType,

    /// <summary>
    /// Block cannot be automatically refactored. Log proposed manual alternatives
    /// and leave the duplicate as-is.
    /// </summary>
    LogAlternatives,
}

// ── Result record ─────────────────────────────────────────────────────────────

/// <summary>Result of <see cref="ControlFlowUnwinder.Classify"/>.</summary>
/// <param name="Pattern">The detected control-flow pattern.</param>
/// <param name="Strategy">The recommended extraction strategy.</param>
/// <param name="Description">Human-readable description of the strategy.</param>
/// <param name="AlternativeSuggestions">
/// For <see cref="ControlFlowStrategy.LogAlternatives"/>: one or more suggested
/// manual refactoring alternatives.
/// </param>
internal sealed record ControlFlowClassification(
    ControlFlowPattern Pattern,
    ControlFlowStrategy Strategy,
    string Description,
    IReadOnlyList<string> AlternativeSuggestions);

// ── Classifier ───────────────────────────────────────────────────────────────

/// <summary>
/// Analyses a statement window for control-flow escape patterns and proposes
/// extraction strategies.
///
/// Pattern priority (first match wins):
///   1. break / continue → BreakEscape / ContinueEscape → LogAlternatives
///   2. Single if-statement with void return → EarlyReturnVoid → ExtractAsBoolGuard
///   3. Single if-statement with value return → GuardReturn → ExtractAsBoolGuard
///   4. Multi-statement block where all returns are void inside if-guards → EarlyReturnVoid
///   5. Any return statement → ValueReturn → ExtractWithReturnType
///   6. No escape → None → NotNeeded
/// </summary>
internal static class ControlFlowUnwinder
{
    private static readonly IReadOnlyList<string> BreakAlternatives = new[]
    {
        "Extract the loop body including the break into a separate method that returns bool to signal early exit.",
        "Restructure the loop to avoid break before extracting.",
    };

    private static readonly IReadOnlyList<string> ContinueAlternatives = new[]
    {
        "Invert the guard condition to skip the block rather than continue.",
        "Extract the non-continue path as a separate method.",
    };

    /// <summary>
    /// Classify the control-flow pattern present in <paramref name="statements"/>.
    /// Uses syntax analysis only — no SemanticModel required.
    /// </summary>
    public static ControlFlowClassification Classify(IReadOnlyList<StatementSyntax> statements)
    {
        if (statements.Count == 0)
            return Make(ControlFlowPattern.None, ControlFlowStrategy.NotNeeded,
                "No statements — nothing to classify.");

        // 1. Break / continue take priority — they escape outer loop structure and
        //    cannot be automatically refactored.
        if (ContainsBreak(statements))
            return MakeLogAlternatives(ControlFlowPattern.BreakEscape,
                "Block contains a break statement that exits a surrounding loop. " +
                "Cannot automatically extract.",
                BreakAlternatives);

        if (ContainsContinue(statements))
            return MakeLogAlternatives(ControlFlowPattern.ContinueEscape,
                "Block contains a continue statement that jumps to the next loop iteration. " +
                "Cannot automatically extract.",
                ContinueAlternatives);

        // 2 & 3. Single if-statement with a return body → guard pattern
        if (statements.Count == 1 && statements[0] is IfStatementSyntax ifStmt)
        {
            var guardReturn = ClassifyGuardIf(ifStmt);
            if (guardReturn != null)
                return guardReturn;
        }

        // 4. Multi-statement block: check if all returns are void inside guard-ifs
        if (ContainsReturn(statements) && AllReturnsAreVoidGuards(statements))
        {
            return Make(ControlFlowPattern.EarlyReturnVoid, ControlFlowStrategy.ExtractAsBoolGuard,
                "Block contains void guard clauses (if (cond) return;). " +
                "Extract as a bool-returning method; call site becomes 'if (NewMethod(...)) return;'.");
        }

        // 5. Any return statement anywhere in the block
        if (ContainsReturn(statements))
        {
            return Make(ControlFlowPattern.ValueReturn, ControlFlowStrategy.ExtractWithReturnType,
                "Block contains a return statement. Extract with a matching return type; " +
                "the call site becomes 'return NewMethod(...);'.");
        }

        // 6. No escape
        return Make(ControlFlowPattern.None, ControlFlowStrategy.NotNeeded,
            "No control-flow escape detected.");
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static ControlFlowClassification? ClassifyGuardIf(IfStatementSyntax ifStmt)
    {
        // Must be a simple if with no else clause
        if (ifStmt.Else != null)
            return null;

        // Body must be a single return statement (either expression or block)
        ReturnStatementSyntax? ret = null;

        if (ifStmt.Statement is ReturnStatementSyntax directReturn)
        {
            ret = directReturn;
        }
        else if (ifStmt.Statement is BlockSyntax block)
        {
            var nonEmpty = block.Statements
                .Where(s => s is not EmptyStatementSyntax)
                .ToList();
            if (nonEmpty.Count == 1 && nonEmpty[0] is ReturnStatementSyntax blockReturn)
                ret = blockReturn;
        }

        if (ret == null)
            return null;

        // Void return: if (cond) return;
        if (ret.Expression == null)
            return Make(ControlFlowPattern.EarlyReturnVoid, ControlFlowStrategy.ExtractAsBoolGuard,
                "Block is a void guard clause (if (cond) return;). " +
                "Extract as a bool-returning method; call site becomes 'if (NewMethod(...)) return;'.");

        // Value return: if (cond) return value;
        return Make(ControlFlowPattern.GuardReturn, ControlFlowStrategy.ExtractAsBoolGuard,
            "Block is a guard clause (if (cond) return value;). " +
            "Extract as a bool-returning method; call site becomes 'if (!NewMethod(...)) return value;'.");
    }

    /// <summary>
    /// Returns true if every return statement in the block is a void return (no expression)
    /// directly inside an if-statement with no else. This means the block is a series of
    /// guard clauses (if (x) return;) mixed with regular statements.
    /// </summary>
    private static bool AllReturnsAreVoidGuards(IReadOnlyList<StatementSyntax> statements)
    {
        foreach (var stmt in statements)
        {
            foreach (var ret in stmt.DescendantNodesAndSelf().OfType<ReturnStatementSyntax>())
            {
                // Each return must have no expression (void)
                if (ret.Expression != null)
                    return false;

                // Each void return must be directly inside an if with no else
                if (ret.Parent is not IfStatementSyntax parentIf || parentIf.Else != null)
                    if (ret.Parent is not BlockSyntax parentBlock ||
                        parentBlock.Parent is not IfStatementSyntax grandIf ||
                        grandIf.Else != null)
                        return false;
            }
        }

        return true;
    }

    private static bool ContainsBreak(IReadOnlyList<StatementSyntax> statements)
        => statements.Any(s => s.DescendantNodesAndSelf()
            .OfType<BreakStatementSyntax>().Any());

    private static bool ContainsContinue(IReadOnlyList<StatementSyntax> statements)
        => statements.Any(s => s.DescendantNodesAndSelf()
            .OfType<ContinueStatementSyntax>().Any());

    private static bool ContainsReturn(IReadOnlyList<StatementSyntax> statements)
        => statements.Any(s => s.DescendantNodesAndSelf()
            .OfType<ReturnStatementSyntax>().Any());

    private static ControlFlowClassification Make(
        ControlFlowPattern pattern,
        ControlFlowStrategy strategy,
        string description)
        => new(pattern, strategy, description, Array.Empty<string>());

    private static ControlFlowClassification MakeLogAlternatives(
        ControlFlowPattern pattern,
        string description,
        IReadOnlyList<string> alternatives)
        => new(pattern, ControlFlowStrategy.LogAlternatives, description, alternatives);
}
