using Microsoft.CodeAnalysis;
using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public sealed class FindUnusedExternalDispatch
{
    [Fact]
    public async Task excludes_external_overrides_including_slots_inherited_through_local_base_types()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Readers.cs", """
            using System.IO;
            public class Reader : TextReader
            {
                public override int Read() => -1;
                public void Orphan() { }
            }
            public sealed class DerivedReader : Reader
            {
                public override int Read() => -1;
            }
            """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldNotContain(finding => finding.Name == "Read");
        findings.ShouldContain(finding => finding.Name == "Orphan");
    }

    [Fact]
    public async Task excludes_roslyn_visitor_callbacks_without_direct_calls()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Visitors.cs", """
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CSharp;
            using Microsoft.CodeAnalysis.CSharp.Syntax;
            public sealed class Rewriter : CSharpSyntaxRewriter
            {
                public override SyntaxToken VisitToken(SyntaxToken token) => token;
            }
            public sealed class Walker : CSharpSyntaxWalker
            {
                public override void VisitVariableDeclarator(VariableDeclaratorSyntax node) { }
            }
            """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldNotContain(finding => finding.Name == "VisitToken");
        findings.ShouldNotContain(finding => finding.Name == "VisitVariableDeclarator");
    }

    [Fact]
    public async Task excludes_actual_external_interface_implementations_but_preserves_unrelated_overloads()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Implementations.cs", """
            using System;
            public sealed class Implicit : IDisposable, IComparable<int>
            {
                public void Dispose() { }
                public void Dispose(int unused) { }
                public int CompareTo(int other) => 0;
                public int CompareTo(string other) => 0;
            }
            public sealed class Explicit : IDisposable
            {
                void IDisposable.Dispose() { }
            }
            """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.Count(finding => finding.Name == "Dispose").ShouldBe(1);
        findings.Count(finding => finding.Name == "CompareTo").ShouldBe(1);
        findings.ShouldNotContain(finding => finding.Name == "System.IDisposable.Dispose");
    }

    [Fact]
    public async Task still_reports_unused_source_local_interface_implementations_and_overrides()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Local.cs", """
            public interface ILocal { void Execute(); }
            public sealed class Implementation : ILocal
            {
                public void Execute() { }
            }
            public class LocalBase
            {
                public virtual void Work() { }
            }
            public sealed class LocalDerived : LocalBase
            {
                public override void Work() { }
            }
            """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldContain(finding => finding.Name == "Execute");
        findings.Count(finding => finding.Name == "Work").ShouldBe(2);
    }

    [Fact]
    public async Task source_project_reference_does_not_become_an_external_dispatch_contract()
    {
        using var builder = new AdhocProjectBuilder("Contracts");
        builder.AddSource("ILocal.cs", "public interface ILocal { void Execute(); }");
        builder.AddProject("Implementation")
            .ReferenceProject("Contracts")
            .AddSource("Local.cs", """
                public sealed class Local : ILocal
                {
                    public void Execute() { }
                }
                """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldContain(finding => finding.Name == "Execute");
    }

    [Fact]
    public async Task source_local_interface_calls_still_keep_the_implementation_live()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Local.cs", """
            public interface ILocal { void Execute(); }
            public sealed class Local : ILocal
            {
                public void Execute() { }
            }
            public static class Consumer
            {
                public static void Invoke(ILocal instance) => instance.Execute();
            }
            """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldNotContain(finding => finding.Name == "Execute");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task inherited_source_methods_implement_external_contracts_on_derived_types(bool separateProject)
    {
        using var builder = new AdhocProjectBuilder("Bases");
        builder.AddSource("Bases.cs", """
            public class ContractBase
            {
                public void Dispose() { }
                public void Ordinary() { }
            }
            public class GenericBase<T>
            {
                public int CompareTo(T other) => 0;
            }
            """);
        const string implementations = """
            public sealed class Consumer : ContractBase, System.IDisposable { }
            public static class Container
            {
                public sealed class GenericConsumer : GenericBase<int>, System.IComparable<int> { }
            }
            """;
        if (separateProject)
            builder.AddProject("Consumers")
                .ReferenceProject("Bases")
                .AddSource("Consumers.cs", implementations);
        else
            builder.AddSource("Consumers.cs", implementations);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldNotContain(finding => finding.Name == "Dispose");
        findings.ShouldNotContain(finding => finding.Name == "CompareTo");
        findings.ShouldContain(finding => finding.Name == "Ordinary");
    }

    [Fact]
    public async Task an_external_interface_on_one_derived_type_does_not_hide_an_unrelated_sibling_override()
    {
        using var builder = new AdhocProjectBuilder();
        builder.AddSource("Base.cs", """
            public class ContractBase
            {
                public virtual void Dispose() { }
            }
            public sealed class Consumer : ContractBase, System.IDisposable
            {
                public override void Dispose() { }
            }
            """);
        builder.AddSource("Sibling.cs", """
            public sealed class UnrelatedSibling : ContractBase
            {
                public override void Dispose() { }
            }
            """);

        var findings = await AnalyzeMethodsAsync(builder);

        findings.ShouldContain(finding => finding.Name == "Dispose" && finding.FilePath != null
            && finding.FilePath.EndsWith("Sibling.cs", StringComparison.Ordinal));
    }

    private static async Task<List<UnusedSymbol>> AnalyzeMethodsAsync(AdhocProjectBuilder builder)
    {
        var (workspace, solution) = builder.Build();
        using (workspace)
        {
            foreach (var project in solution.Projects)
            {
                var compilation = (await project.GetCompilationAsync()).ShouldNotBeNull();
                compilation.GetDiagnostics().ShouldNotContain(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            }

            return await new FindUnusedService().AnalyzeAsync(solution, "methods");
        }
    }
}
