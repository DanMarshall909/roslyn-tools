using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

/// <summary>
///     Unit tests for IdentifierVariationDetector (US-045 Phase 2).
///     These tests verify the detector identifies which identifier positions vary
///     across structural duplicates by comparing PatternMatch.Substitutions.
/// </summary>
public class IdentifierVariationDetector
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Single identifier difference across two matches
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void detects_single_varying_identifier_position()
    {
        // Pattern body uses identifiers [optA, optB] (in traversal order)
        // Match 1: [optA → optA, optB → optC]  ← optB varies (index 1)
        var patternIdentifiers = new List<string> { "optA", "optB" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["optA"] = "optA", ["optB"] = "optC" }
        };

        var varyingPositions = Core.IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Single(varyingPositions);
        Assert.Contains(1, varyingPositions);
    }

    [Fact]
    public void reports_no_varying_positions_when_all_identifiers_are_identical()
    {
        // Both matches use the exact same identifiers — no parameterization needed
        var patternIdentifiers = new List<string> { "x", "y" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["x"] = "x", ["y"] = "y" },
            new() { ["x"] = "x", ["y"] = "y" }
        };

        var varyingPositions = Core.IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Empty(varyingPositions);
    }

    [Fact]
    public void detects_multiple_varying_identifier_positions()
    {
        // Pattern: [serviceA, repoA, loggerA]
        // Match 1: serviceA → serviceB, repoA → repoB, loggerA → loggerA (unchanged)
        var patternIdentifiers = new List<string> { "serviceA", "repoA", "loggerA" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["serviceA"] = "serviceB", ["repoA"] = "repoB", ["loggerA"] = "loggerA" }
        };

        var varyingPositions = Core.IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Equal(2, varyingPositions.Count);
        Assert.Contains(0, varyingPositions); // serviceA vs serviceB
        Assert.Contains(1, varyingPositions); // repoA vs repoB
        Assert.DoesNotContain(2, varyingPositions); // loggerA unchanged
    }

    [Fact]
    public void handles_empty_pattern_identifiers()
    {
        var patternIdentifiers = new List<string>();
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["x"] = "y" }
        };

        var varyingPositions = Core.IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Empty(varyingPositions);
    }

    [Fact]
    public void handles_empty_match_substitutions()
    {
        var patternIdentifiers = new List<string> { "x", "y" };
        var matchSubstitutions = new List<Dictionary<string, string>>();

        var varyingPositions = Core.IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Empty(varyingPositions);
    }

    [Fact]
    public void detects_variation_when_any_match_differs()
    {
        // Two matches: first same, second differs at position 0
        var patternIdentifiers = new List<string> { "fieldA", "fieldB" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["fieldA"] = "fieldA", ["fieldB"] = "fieldB" }, // same
            new() { ["fieldA"] = "fieldC", ["fieldB"] = "fieldB" } // position 0 differs
        };

        var varyingPositions = Core.IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Single(varyingPositions);
        Assert.Contains(0, varyingPositions);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  v1 scope filter: member-access expression side and invocation targets
    //  are excluded so 'object' parameters are not generated for them
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void extracts_ordered_identifiers_skips_member_access_expression_side()
    {
        // _svc.DoWork() — _svc is the expression side of member access; must be skipped
        // Console.WriteLine is a member access but Console is a type, not parameterizable
        // The only bare identifier is the argument (none here, so result is empty)
        const string code = """
                            class C {
                                void M() {
                                    _svc.DoWork();
                                }
                            }
                            """;
        var tree = CSharpSyntaxTree.ParseText(code);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();
        var statements = method.Body!.Statements;

        var identifiers = Core.IdentifierVariationDetector.ExtractOrderedIdentifiers(statements);

        // _svc must NOT appear — it's the expression side of a MemberAccessExpression
        Assert.DoesNotContain("_svc", identifiers);
    }

    [Fact]
    public void extracts_ordered_identifiers_skips_invocation_target()
    {
        // DirectCall() — DirectCall is the direct invocation target; must be skipped
        const string code = """
                            class C {
                                void M() {
                                    DirectCall();
                                }
                            }
                            """;
        var tree = CSharpSyntaxTree.ParseText(code);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();
        var statements = method.Body!.Statements;

        var identifiers = Core.IdentifierVariationDetector.ExtractOrderedIdentifiers(statements);

        // DirectCall must NOT appear — it's a direct invocation target
        Assert.DoesNotContain("DirectCall", identifiers);
    }

    [Fact]
    public void extracts_ordered_identifiers_includes_bare_argument_identifiers()
    {
        // Console.WriteLine(_fieldA) — _fieldA is a bare argument, must be included
        const string code = """
                            class C {
                                void M() {
                                    System.Console.WriteLine(_fieldA);
                                }
                            }
                            """;
        var tree = CSharpSyntaxTree.ParseText(code);
        var method = tree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .First();
        var statements = method.Body!.Statements;

        var identifiers = Core.IdentifierVariationDetector.ExtractOrderedIdentifiers(statements);

        // _fieldA IS a bare argument — it should be included
        Assert.Contains("_fieldA", identifiers);
    }
}
