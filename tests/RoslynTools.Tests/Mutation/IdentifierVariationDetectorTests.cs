using RoslynTools.Core;

namespace RoslynTools.Tests.Mutation;

/// <summary>
/// Unit tests for IdentifierVariationDetector (US-045 Phase 2).
/// These tests verify the detector identifies which identifier positions vary
/// across structural duplicates by comparing PatternMatch.Substitutions.
/// </summary>
public class IdentifierVariationDetectorTests
{
    // ──────────────────────────────────────────────────────────────────────────
    //  Single identifier difference across two matches
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Detects_single_varying_identifier_position()
    {
        // Pattern body uses identifiers [optA, optB] (in traversal order)
        // Match 1: [optA → optA, optB → optC]  ← optB varies (index 1)
        var patternIdentifiers = new List<string> { "optA", "optB" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["optA"] = "optA", ["optB"] = "optC" }
        };

        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Single(varyingPositions);
        Assert.Contains(1, varyingPositions);
    }

    [Fact]
    public void No_varying_positions_when_all_identifiers_are_identical()
    {
        // Both matches use the exact same identifiers — no parameterization needed
        var patternIdentifiers = new List<string> { "x", "y" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["x"] = "x", ["y"] = "y" },
            new() { ["x"] = "x", ["y"] = "y" }
        };

        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Empty(varyingPositions);
    }

    [Fact]
    public void Detects_multiple_varying_identifier_positions()
    {
        // Pattern: [serviceA, repoA, loggerA]
        // Match 1: serviceA → serviceB, repoA → repoB, loggerA → loggerA (unchanged)
        var patternIdentifiers = new List<string> { "serviceA", "repoA", "loggerA" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["serviceA"] = "serviceB", ["repoA"] = "repoB", ["loggerA"] = "loggerA" }
        };

        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Equal(2, varyingPositions.Count);
        Assert.Contains(0, varyingPositions); // serviceA vs serviceB
        Assert.Contains(1, varyingPositions); // repoA vs repoB
        Assert.DoesNotContain(2, varyingPositions); // loggerA unchanged
    }

    [Fact]
    public void Handles_empty_pattern_identifiers()
    {
        var patternIdentifiers = new List<string>();
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["x"] = "y" }
        };

        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Empty(varyingPositions);
    }

    [Fact]
    public void Handles_empty_match_substitutions()
    {
        var patternIdentifiers = new List<string> { "x", "y" };
        var matchSubstitutions = new List<Dictionary<string, string>>();

        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Empty(varyingPositions);
    }

    [Fact]
    public void Detects_variation_when_any_match_differs()
    {
        // Two matches: first same, second differs at position 0
        var patternIdentifiers = new List<string> { "fieldA", "fieldB" };
        var matchSubstitutions = new List<Dictionary<string, string>>
        {
            new() { ["fieldA"] = "fieldA", ["fieldB"] = "fieldB" },  // same
            new() { ["fieldA"] = "fieldC", ["fieldB"] = "fieldB" }   // position 0 differs
        };

        var varyingPositions = IdentifierVariationDetector.FindVaryingPositions(patternIdentifiers, matchSubstitutions);

        Assert.Single(varyingPositions);
        Assert.Contains(0, varyingPositions);
    }
}
