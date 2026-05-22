using RoslynTools.Core;
using Shouldly;

namespace RoslynTools.Tests.Mutation;

/// <summary>
///     Integration tests for US-049 Phase 6: generic type-parameter parameterization.
///     Verify that --replace-duplicates detects duplicate groups that differ only in
///     generic type arguments and emits a generic extracted method (e.g. Method&lt;T&gt;)
///     rather than erasing to object parameters.
///     v1 scope: user-defined type arguments only (both sides are IdentifierNameSyntax
///     so PatternMatcher structural match succeeds). Predefined keyword types (int, string)
///     are out of scope — PatternMatcher RawKind mismatch means they never reach this path.
///     Constraint inference (where T : ...) is deferred (AC4 — see KnownIssues on the command).
/// </summary>
public class GenericParameterization
{
    private static string FilePath(AdhocProjectBuilder project, string relativePath)
    {
        return Path.Combine(project.ProjectDir, relativePath);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC2: Single varying type argument -> extracted method gets <T>
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task emits_generic_method_for_single_varying_type_argument()
    {
        // A uses List<MyType>; B uses List<OtherType>.
        // Structurally identical modulo the type argument.
        // Expected: extracted method signature contains <T> and uses List<T> in body.
        // B's call site passes explicit type argument <OtherType>.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  using System.Collections.Generic;
                                  namespace TestApp;

                                  public class MyType { }

                                  public class A
                                  {
                                      public void Run()
                                      {
                                          var list = new List<MyType>();
                                          list.Add(new MyType());
                                      }
                                  }
                                  """);
        project.AddSource("B.cs", """
                                  using System.Collections.Generic;
                                  namespace TestApp;

                                  public class OtherType { }

                                  public class B
                                  {
                                      public void Run()
                                      {
                                          var list = new List<OtherType>();
                                          list.Add(new OtherType());
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "BuildList",
                10,
                11,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Extracted method exists in A with a type parameter
        contentA.ShouldContain("BuildList<", Case.Sensitive);
        // Method body uses T (or the type param name) not MyType directly
        contentA.ShouldContain("List<", Case.Sensitive);

        // B's call site uses the extracted method with OtherType
        contentB.ShouldContain("BuildList<", Case.Sensitive);
        contentB.ShouldContain("OtherType", Case.Sensitive);
        // B's original statements replaced
        contentB.ShouldNotContain("new List<OtherType>()", Case.Sensitive);
        contentB.ShouldNotContain("list.Add(new OtherType())", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC3: Multiple varying type arguments -> extracted method gets <T1, T2>
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task emits_generic_method_for_multiple_varying_type_arguments()
    {
        // A uses Dictionary<KeyTypeA, ValTypeA>; B uses Dictionary<KeyTypeB, ValTypeB>.
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  using System.Collections.Generic;
                                  namespace TestApp;

                                  public class KeyTypeA { }
                                  public class ValTypeA { }

                                  public class A
                                  {
                                      public void Run()
                                      {
                                          var dict = new Dictionary<KeyTypeA, ValTypeA>();
                                          dict[new KeyTypeA()] = new ValTypeA();
                                      }
                                  }
                                  """);
        project.AddSource("B.cs", """
                                  using System.Collections.Generic;
                                  namespace TestApp;

                                  public class KeyTypeB { }
                                  public class ValTypeB { }

                                  public class B
                                  {
                                      public void Run()
                                      {
                                          var dict = new Dictionary<KeyTypeB, ValTypeB>();
                                          dict[new KeyTypeB()] = new ValTypeB();
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "BuildDict",
                11,
                12,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Extracted method in A has multiple type parameters
        contentA.ShouldContain("BuildDict<", Case.Sensitive);

        // B's call site passes explicit type arguments for both
        contentB.ShouldContain("BuildDict<", Case.Sensitive);
        contentB.ShouldContain("KeyTypeB", Case.Sensitive);
        contentB.ShouldContain("ValTypeB", Case.Sensitive);
        // Original statements removed from B
        contentB.ShouldNotContain("new Dictionary<KeyTypeB, ValTypeB>()", Case.Sensitive);
    }

    // ──────────────────────────────────────────────────────────────────────────
    //  AC5: Mixed literal + type-arg variations -> both parameterized correctly
    //  Literal becomes a value parameter; type arg becomes a type parameter
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task parameterizes_mixed_literal_and_type_arg_variations()
    {
        // A: Console.WriteLine("hello"); var list = new List<MyType>();
        // B: Console.WriteLine("bye");   var list = new List<OtherType>();
        // Literal "hello"/"bye" -> string value param
        // Type arg MyType/OtherType    -> type param <T>
        using var project = new AdhocProjectBuilder();
        project.AddSource("A.cs", """
                                  using System;
                                  using System.Collections.Generic;
                                  namespace TestApp;

                                  public class MyType { }

                                  public class A
                                  {
                                      public void Run()
                                      {
                                          Console.WriteLine("hello");
                                          var list = new List<MyType>();
                                      }
                                  }
                                  """);
        project.AddSource("B.cs", """
                                  using System;
                                  using System.Collections.Generic;
                                  namespace TestApp;

                                  public class OtherType { }

                                  public class B
                                  {
                                      public void Run()
                                      {
                                          Console.WriteLine("bye");
                                          var list = new List<OtherType>();
                                      }
                                  }
                                  """);

        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var newSln = await MethodExtractor.ExtractAsync(
                sln,
                FilePath(project, "A.cs"),
                "DoWork",
                11,
                12,
                replaceDuplicates: true);
            await SolutionWriter.ApplyChangesAsync(sln, newSln, false);
        }

        var contentA = await project.ReadSourceAsync("A.cs");
        var contentB = await project.ReadSourceAsync("B.cs");

        // Method exists in A with type parameter
        contentA.ShouldContain("DoWork<", Case.Sensitive);

        // B call site has the differing literal and type argument
        contentB.ShouldContain("DoWork<", Case.Sensitive);
        contentB.ShouldContain("\"bye\"", Case.Sensitive);
        contentB.ShouldContain("OtherType", Case.Sensitive);
        // Original statements removed
        contentB.ShouldNotContain("Console.WriteLine(\"bye\")", Case.Sensitive);
        contentB.ShouldNotContain("new List<OtherType>()", Case.Sensitive);
    }
}
