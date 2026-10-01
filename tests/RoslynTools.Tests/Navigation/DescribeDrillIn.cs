using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

/// <summary>
///     Spike for US-135: the per-member signature emitted by <see cref="SignatureFormatter.Signature" />
///     must round-trip — appended to its type's FQN it resolves back to exactly that member via
///     <see cref="SymbolLocator.TryFindMemberBySignatureAsync" />. This is the contract US-136 builds on.
/// </summary>
public class DescribeDrillIn
{
    private static AdhocProjectBuilder Fixture()
    {
        var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", """
                                            namespace TestApp;

                                            public class Calculator
                                            {
                                                public int Add(int a, int b) => a + b;
                                                public double Add(double a, double b) => a + b;
                                                public string Describe(string label) => label;
                                                public void Reset() { }
                                            }
                                            """);
        return project;
    }

    [Fact]
    public void signature_includes_return_type_and_param_types_without_param_names()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var calc = FindCalculator(sln);
            var intAdd = calc.GetMembers("Add").OfType<IMethodSymbol>()
                .First(m => m.Parameters[0].Type.SpecialType == SpecialType.System_Int32);

            SignatureFormatter.Signature(intAdd).ShouldBe("int Add(int, int)");
            SignatureFormatter.MatchSignature(intAdd).ShouldBe("Add(int, int)");
        }
    }

    [Fact]
    public async Task emitted_signature_roundtrips_back_to_its_member()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var calc = FindCalculator(sln);

            foreach (var method in calc.GetMembers().OfType<IMethodSymbol>()
                         .Where(m => m.MethodKind == MethodKind.Ordinary))
            {
                var input = "TestApp.Calculator." + SignatureFormatter.MatchSignature(method);

                var result = await SymbolLocator.TryFindMemberBySignatureAsync(sln, input);

                result.IsOk.ShouldBeTrue($"expected '{input}' to resolve");
                var resolved = result.Match(s => s, _ => null!);
                SymbolEqualityComparer.Default.Equals(resolved, method)
                    .ShouldBeTrue($"'{input}' resolved to the wrong member");
            }
        }
    }

    [Fact]
    [Trait("Concern", "Overloads")]
    public async Task overload_resolves_by_parameter_types()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var intResult = await SymbolLocator.TryFindMemberBySignatureAsync(
                sln, "TestApp.Calculator.Add(int, int)");
            var doubleResult = await SymbolLocator.TryFindMemberBySignatureAsync(
                sln, "TestApp.Calculator.Add(double, double)");

            intResult.Match(m => ((IMethodSymbol)m).Parameters[0].Type.SpecialType, _ => default)
                .ShouldBe(SpecialType.System_Int32);
            doubleResult.Match(m => ((IMethodSymbol)m).Parameters[0].Type.SpecialType, _ => default)
                .ShouldBe(SpecialType.System_Double);
        }
    }

    [Fact]
    [Trait("Concern", "Overloads")]
    public async Task ambiguous_name_only_returns_ambiguous_match_with_candidate_signatures()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await SymbolLocator.TryFindMemberBySignatureAsync(
                sln, "TestApp.Calculator.Add");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => RtErrorKind.NotFound, e => e.Kind).ShouldBe(RtErrorKind.AmbiguousMatch);
            var message = result.Match(_ => "", e => e.Message);
            message.ShouldContain("int Add(int, int)");
            message.ShouldContain("double Add(double, double)");
        }
    }

    [Fact]
    [Trait("Concern", "Overloads")]
    public async Task many_name_only_overloads_return_bounded_complete_signatures()
    {
        var members = string.Join(Environment.NewLine,
            Enumerable.Range(0, 80).Select(index =>
                $"public sealed class T{index:D3} {{ }} public void Mix(T{index:D3} value) {{ }}"));
        using var project = new AdhocProjectBuilder();
        project.AddSource("Calculator.cs", $$"""
            namespace TestApp;
            public class Calculator
            {
                {{members}}
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var result = await SymbolLocator.TryFindMemberBySignatureAsync(
                solution, "TestApp.Calculator.Mix");
            var error = result.Match<RtError?>(_ => null, failure => failure);

            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
            error.Message.ShouldStartWith(
                "'TestApp.Calculator.Mix' is ambiguous on type 'TestApp.Calculator'. Candidates:");
            error.Context.ShouldNotBeNull();
            error.Context!["candidateCount"].ShouldBe("80");
            var displayed = int.Parse(error.Context["displayedCandidateCount"],
                CultureInfo.InvariantCulture);
            displayed.ShouldBeInRange(1, 12);
            error.Context["candidatesTruncated"].ShouldBe("true");
            var candidates = error.Context["candidates"].Split(';');
            candidates.Length.ShouldBe(displayed);
            var expected = Enumerable.Range(0, displayed)
                .Select(index => $"void Mix(T{index:D3})")
                .ToArray();
            candidates.SequenceEqual(expected, StringComparer.Ordinal).ShouldBeTrue();
            foreach (var signature in expected)
                error.Message.ShouldContain(signature);
            error.Message.ShouldContain("Pass an explicit parameter signature.");
            Encoding.UTF8.GetByteCount(error.Message).ShouldBeLessThanOrEqualTo(2_048);
            Encoding.UTF8.GetByteCount(error.Context["candidates"])
                .ShouldBeLessThanOrEqualTo(4_096);
        }
    }

    [Fact]
    [Trait("Concern", "Overloads")]
    public async Task long_member_name_does_not_expand_ambiguity_message()
    {
        var typeName = new string('界', 500);
        using var project = new AdhocProjectBuilder();
        project.AddSource("LongNames.cs", $$"""
            namespace TestApp;
            public class {{typeName}}
            {
                public void Mix(int value) { }
                public void Mix(string value) { }
            }
            """);
        var (workspace, solution) = project.Build();
        using (workspace)
        {
            var result = await SymbolLocator.TryFindMemberBySignatureAsync(
                solution, $"TestApp.{typeName}.Mix");
            var error = result.Match<RtError?>(_ => null, failure => failure);

            error.ShouldNotBeNull();
            error.Kind.ShouldBe(RtErrorKind.AmbiguousMatch);
            error.Context.ShouldNotBeNull();
            error.Context!["candidateCount"].ShouldBe("2");
            error.Context["symbolTruncated"].ShouldBe("true");
            Encoding.UTF8.GetByteCount(error.Context["symbol"]).ShouldBeLessThanOrEqualTo(256);
            Encoding.UTF8.GetByteCount(error.Context["type"]).ShouldBeLessThanOrEqualTo(256);
            Encoding.UTF8.GetByteCount(error.Message).ShouldBeLessThanOrEqualTo(2_048);
        }
    }

    [Fact]
    [Trait("Concern", "ErrorHandling")]
    public async Task unknown_member_signature_returns_not_found()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await SymbolLocator.TryFindMemberBySignatureAsync(
                sln, "TestApp.Calculator.Multiply(int, int)");

            result.IsErr.ShouldBeTrue();
            result.Match(_ => RtErrorKind.AmbiguousMatch, e => e.Kind).ShouldBe(RtErrorKind.NotFound);
        }
    }

    [Fact]
    [Trait("Concern", "ErrorHandling")]
    public async Task unknown_containing_type_returns_not_found()
    {
        using var project = Fixture();
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var result = await SymbolLocator.TryFindMemberBySignatureAsync(
                sln, "TestApp.NoSuchType.Add(int, int)");

            result.IsErr.ShouldBeTrue();
        }
    }

    private static INamedTypeSymbol FindCalculator(Solution sln)
    {
        var sym = SymbolLocator.FindTypeByFqnAsync(sln, "TestApp.Calculator")
            .GetAwaiter().GetResult();
        return (INamedTypeSymbol)sym;
    }
}
