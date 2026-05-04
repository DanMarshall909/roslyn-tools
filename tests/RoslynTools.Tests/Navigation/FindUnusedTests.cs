using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Core;
using RoslynTools.Services;

namespace RoslynTools.Tests.Navigation;

public class FindUnusedTests
{

    [Fact]
    public async Task Reports_unreferenced_method()
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
            var results = await FindUnusedService.AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            Assert.Contains("UnusedMethod", names);
        }
    }

    [Fact]
    public async Task Does_not_report_referenced_method()
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
            var results = await FindUnusedService.AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("UsedMethod", names);
        }
    }

    [Fact]
    public async Task Excludes_serializable_types()
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
            var results = await FindUnusedService.AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("SerializableData", names);
        }
    }

    [Fact]
    public async Task Reports_only_methods()
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
            var results = await FindUnusedService.AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            // UnusedClass is a type, not a method — should not appear with scope=methods
            Assert.DoesNotContain("UnusedClass", names);
            // UnusedMethod is a method and unreferenced — should appear
            Assert.Contains("UnusedMethod", names);
        }
    }

    [Fact]
    public async Task Reports_only_types_with_scope_types()
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
            var results = await FindUnusedService.AnalyzeAsync(sln, "types");
            var names = results.Select(r => r.Name).ToList();
            // UnusedMethod is a method — must not appear when scope is "types"
            Assert.DoesNotContain("UnusedMethod", names);
            // OrphanType has no references — should appear
            Assert.Contains("OrphanType", names);
        }
    }

    [Fact]
    public async Task Excludes_Main_entry_point()
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
            var results = await FindUnusedService.AnalyzeAsync(sln);
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("Main", names);
        }
    }

    [Fact]
    public async Task Excludes_xunit_Fact_test_methods()
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
            var results = await FindUnusedService.AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("Some_test", names);
        }
    }

    [Fact]
    public async Task Excludes_interface_members()
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
            var results = await FindUnusedService.AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("Execute", names);
        }
    }

    [Fact]
    public async Task Excludes_xunit_Theory_test_methods()
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
            var results = await FindUnusedService.AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("Parameterised_test", names);
        }
    }

    [Fact]
    public async Task Excludes_DataContract_attributed_types()
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
            var results = await FindUnusedService.AnalyzeAsync(sln, "types");
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("OrderDto", names);
        }
    }
}
