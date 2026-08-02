using RoslynTools.Services;
using Shouldly;

namespace RoslynTools.Tests.Navigation;

public class FindUnused
{
    [Fact]
    public async Task reports_unreferenced_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         using System;

                                         public class UsedService
                                         {
                                             public void UsedMethod() { }
                                             public void UnusedMethod() { }  // never called
                                         }

                                         public class CallerClass
                                         {
                                             public void Run()
                                             {
                                                 var s = new UsedService();
                                                 s.UsedMethod();
                                             }
                                         }

                                         [Serializable]
                                         public class SerializableData { }  // should be excluded

                                         public class UnusedClass { }  // never referenced
                                         """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            names.ShouldContain("UnusedMethod");
        }
    }

    [Fact]
    public async Task does_not_report_referenced_method()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         using System;

                                         public class UsedService
                                         {
                                             public void UsedMethod() { }
                                             public void UnusedMethod() { }  // never called
                                         }

                                         public class CallerClass
                                         {
                                             public void Run()
                                             {
                                                 var s = new UsedService();
                                                 s.UsedMethod();
                                             }
                                         }

                                         [Serializable]
                                         public class SerializableData { }  // should be excluded

                                         public class UnusedClass { }  // never referenced
                                         """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("UsedMethod");
        }
    }

    [Fact]
    public async Task excludes_serializable_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         using System;

                                         public class UsedService
                                         {
                                             public void UsedMethod() { }
                                             public void UnusedMethod() { }  // never called
                                         }

                                         public class CallerClass
                                         {
                                             public void Run()
                                             {
                                                 var s = new UsedService();
                                                 s.UsedMethod();
                                             }
                                         }

                                         [Serializable]
                                         public class SerializableData { }  // should be excluded

                                         public class UnusedClass { }  // never referenced
                                         """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("SerializableData");
        }
    }

    [Fact]
    public async Task reports_only_methods()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         using System;

                                         public class UsedService
                                         {
                                             public void UsedMethod() { }
                                             public void UnusedMethod() { }  // never called
                                         }

                                         public class CallerClass
                                         {
                                             public void Run()
                                             {
                                                 var s = new UsedService();
                                                 s.UsedMethod();
                                             }
                                         }

                                         [Serializable]
                                         public class SerializableData { }  // should be excluded

                                         public class UnusedClass { }  // never referenced
                                         """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            // UnusedClass is a type, not a method — should not appear with scope=methods
            names.ShouldNotContain("UnusedClass");
            // UnusedMethod is a method and unreferenced — should appear
            names.ShouldContain("UnusedMethod");
        }
    }

    [Fact]
    public async Task reports_only_types_with_scope_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Services.cs", """
                                         public class UsedService
                                         {
                                             public void UsedMethod() { }
                                             public void UnusedMethod() { }
                                         }

                                         public class CallerClass
                                         {
                                             public void Run()
                                             {
                                                 var s = new UsedService();
                                                 s.UsedMethod();
                                             }
                                         }

                                         public class OrphanType { }
                                         """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "types");
            var names = results.Select(r => r.Name).ToList();
            // UnusedMethod is a method — must not appear when scope is "types"
            names.ShouldNotContain("UnusedMethod");
            // OrphanType has no references — should appear
            names.ShouldContain("OrphanType");
        }
    }

    [Fact]
    public async Task excludes_main_entry_point()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Program.cs", """
                                        public class Program
                                        {
                                            public static void Main(string[] args) { }
                                        }
                                        """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("Main");
        }
    }

    [Fact]
    public async Task excludes_xunit_fact_test_methods()
    {
        using var project = new AdhocProjectBuilder();
        // Simulate a [Fact] attribute — AdhocProjectBuilder compiles against a minimal set
        // so we define a stub attribute to keep the project self-contained.
        project.AddSource("Tests.cs", """
                                      using System;

                                      [AttributeUsage(AttributeTargets.Method)]
                                      public class FactAttribute : Attribute { }

                                      public class MyTests
                                      {
                                          [Fact]
                                          public void Some_test() { }
                                      }
                                      """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("Some_test");
        }
    }

    [Fact]
    public async Task excludes_interface_members()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Contracts.cs", """
                                          public interface IService
                                          {
                                              void Execute();
                                          }
                                          """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("Execute");
        }
    }

    [Fact]
    public async Task excludes_xunit_theory_test_methods()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Tests.cs", """
                                      using System;

                                      [AttributeUsage(AttributeTargets.Method)]
                                      public class TheoryAttribute : Attribute { }

                                      public class MyTests
                                      {
                                          [Theory]
                                          public void Parameterised_test(int x) { }
                                      }
                                      """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("Parameterised_test");
        }
    }

    [Fact]
    public async Task excludes_reflectively_invoked_command_build_methods()
    {
        using var project = new AdhocProjectBuilder();
        // Mirrors rt's own App.Discover(): every IRtCommand implementer's Build() is
        // invoked via MethodInfo.Invoke, never called by name from source.
        project.AddSource("Commands.cs", """
                                          public interface IRtCommand
                                          {
                                          }

                                          public class FooCommand : IRtCommand
                                          {
                                              public object Build() => new object();
                                          }
                                          """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("Build");
        }
    }

    [Fact]
    public async Task still_reports_unused_methods_on_command_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Commands.cs", """
                                          public interface IRtCommand
                                          {
                                          }

                                          public class FooCommand : IRtCommand
                                          {
                                              public object Build() => new object();
                                              public void Orphan() { }  // never called, unrelated to Build
                                          }
                                          """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            names.ShouldContain("Orphan");
        }
    }

    [Fact]
    public async Task excludes_data_contract_attributed_types()
    {
        using var project = new AdhocProjectBuilder();
        project.AddSource("Contracts.cs", """
                                          using System;

                                          [AttributeUsage(AttributeTargets.Class)]
                                          public class DataContractAttribute : Attribute { }

                                          [DataContract]
                                          public class OrderDto { }
                                          """);
        var (workspace, sln) = project.Build();
        using (workspace)
        {
            var results = await new FindUnusedService().AnalyzeAsync(sln, "types");
            var names = results.Select(r => r.Name).ToList();
            names.ShouldNotContain("OrderDto");
        }
    }
}
