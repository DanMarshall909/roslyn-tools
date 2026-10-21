using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace RoslynTools.Tests.Architecture;

/// <summary>Host trust-scope acquisition must share the expected-failure classification.</summary>
public sealed class RepositoryTrustBoundaryContracts
{
    [Fact]
    public void all_host_trust_scope_calls_use_the_shared_expected_error_filter()
    {
        var src = LocateSrcDirectory();
        var violations = new List<string>();
        var callCount = 0;
        foreach (var host in new[] { "RoslynTools.Cli", "RoslynTools.Mcp" })
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(src, host), "*.cs", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(src, file);
                if (relativePath.Split(Path.DirectorySeparatorChar).Any(part => part is "obj" or "bin")
                    || file.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase))
                    continue;

                var calls = FindScopeCalls(File.ReadAllText(file));
                callCount += calls.Length;
                violations.AddRange(calls.Where(call => !HasExpectedErrorGuard(call)).Select(call =>
                    $"{relativePath}({call.GetLocation().GetLineSpan().StartLinePosition.Line + 1})"));
            }
        }

        Assert.True(callCount > 0, "No host trust-scope calls were found; check the architecture detector.");
        Assert.True(violations.Count == 0,
            "Every host EnterCliScope/EnterTrustScope call must execute inside a try block guarded by "
            + "catch (Exception exception) when (RepositoryTrustErrors.IsExpected(exception)). "
            + "Do not duplicate the expected-exception list in hosts.\n" + string.Join("\n", violations));
    }

    [Theory]
    [InlineData("try { CALL; } catch (Exception ex) when (ex is IOException) { }")]
    [InlineData("try { CALL; } catch (IOException ex) when (RepositoryTrustErrors.IsExpected(ex)) { }")]
    [InlineData("CALL; try { Other(); } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { }")]
    [InlineData("try { CALL; } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(other)) { }")]
    [InlineData("try { CALL; } catch (Exception ex) when (OtherErrors.IsExpected(ex)) { }")]
    [InlineData("try { CALL; } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex) || true) { }")]
    [InlineData("try { Other(); } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { CALL; }")]
    [InlineData("try { Other(); } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { } finally { CALL; }")]
    [InlineData("try { Action deferred = () => { CALL; }; } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { }")]
    [InlineData("try { void Deferred() { CALL; } } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { }")]
    public void detector_rejects_missing_or_ineffective_guards(string body)
    {
        foreach (var expression in ScopeExpressions)
        {
            var call = Assert.Single(FindScopeCalls(Wrap(body.Replace("CALL", expression, StringComparison.Ordinal))));
            Assert.False(HasExpectedErrorGuard(call), expression + ": " + body);
        }
    }

    [Theory]
    [InlineData("try { CALL; } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { }")]
    [InlineData("try { CALL; } catch (System.Exception error) when (RepositoryTrustErrors.IsExpected(error)) { }")]
    [InlineData("try { if (enabled) { CALL; } } catch (global::System.Exception failure) when (RepositoryTrustErrors.IsExpected(failure)) { }")]
    [InlineData("Action deferred = () => { try { CALL; } catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { } };")]
    public void detector_accepts_shared_filters_guarding_the_actual_call(string body)
    {
        foreach (var expression in ScopeExpressions)
        {
            var call = Assert.Single(FindScopeCalls(Wrap(body.Replace("CALL", expression, StringComparison.Ordinal))));
            Assert.True(HasExpectedErrorGuard(call), expression + ": " + body);
        }
    }

    [Fact]
    public void detector_checks_every_call_in_a_file()
    {
        var calls = FindScopeCalls(Wrap("""
            try { RepositoryTrustPolicy.EnterCliScope(); }
            catch (Exception ex) when (RepositoryTrustErrors.IsExpected(ex)) { }
            host.EnterTrustScope();
            """));

        Assert.Equal(2, calls.Length);
        Assert.Single(calls.Where(call => !HasExpectedErrorGuard(call)));
    }

    private static readonly string[] ScopeExpressions =
        ["RepositoryTrustPolicy.EnterCliScope()", "host.EnterTrustScope()", "EnterCliScope()", "EnterTrustScope()"];

    private static string Wrap(string body) => "class Host { void Invoke() { " + body + " } }";

    // Deliberately syntax-based, like the neighboring architecture locks: recognize the
    // scope method names regardless of receiver, but require the canonical classifier
    // spelling. This is a structural guard, not proof of catch-body rendering semantics.
    private static InvocationExpressionSyntax[] FindScopeCalls(string source) =>
        CSharpSyntaxTree.ParseText(source).GetCompilationUnitRoot().DescendantNodes()
            .OfType<InvocationExpressionSyntax>().Where(call => (call.Expression switch
            {
                MemberAccessExpressionSyntax member => member.Name.Identifier.ValueText,
                MemberBindingExpressionSyntax member => member.Name.Identifier.ValueText,
                IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
                _ => null
            }) is "EnterCliScope" or "EnterTrustScope").ToArray();

    private static bool HasExpectedErrorGuard(InvocationExpressionSyntax call)
    {
        foreach (var ancestor in call.Ancestors())
        {
            // A surrounding try does not protect later execution of a captured delegate
            // or local function. A try inside that executable body still qualifies.
            if (ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax or BaseMethodDeclarationSyntax)
                break;
            if (ancestor is TryStatementSyntax statement && statement.Block.Span.Contains(call.Span)
                && statement.Catches.Any(UsesSharedExpectedErrorFilter))
                return true;
        }

        return false;
    }

    private static bool UsesSharedExpectedErrorFilter(CatchClauseSyntax clause)
    {
        if (clause.Declaration is not { } declaration
            || declaration.Type.ToString() is not ("Exception" or "System.Exception" or "global::System.Exception")
            || declaration.Identifier.IsMissing
            || clause.Filter?.FilterExpression is not InvocationExpressionSyntax filter
            || filter.Expression is not MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "RepositoryTrustErrors" },
                Name.Identifier.ValueText: "IsExpected"
            }
            || filter.ArgumentList.Arguments.Count != 1)
            return false;

        return filter.ArgumentList.Arguments[0].Expression is IdentifierNameSyntax argument
               && argument.Identifier.ValueText == declaration.Identifier.ValueText;
    }

    private static string LocateSrcDirectory()
    {
        for (var directory = AppContext.BaseDirectory; directory is not null; directory = Path.GetDirectoryName(directory))
        {
            if (File.Exists(Path.Combine(directory, "RoslynTools.slnx")))
                return Path.Combine(directory, "src");
        }

        throw new InvalidOperationException("Could not locate RoslynTools.slnx from " + AppContext.BaseDirectory);
    }
}
