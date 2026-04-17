using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using RoslynTools.Commands;
using RoslynTools.Core;

namespace RoslynTools.Tests;

[Collection("MSBuild")]
public class FindUnusedTests
{
    private static async Task<(Microsoft.CodeAnalysis.Workspace workspace, Solution sln)> BuildFixture()
    {
        using var project = new TestProjectBuilder();
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
        var slnPath = await project.BuildAsync();
        return await WorkspaceLoader.LoadAsync(slnPath);
    }

    [Fact]
    public async Task FindUnused_ReportsUnreferencedMethod()
    {
        using var project = new TestProjectBuilder();
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
        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var results = await FindUnusedCore.AnalyzeAsync(sln, "all");
            var names = results.Select(r => r.Name).ToList();
            Assert.Contains("UnusedMethod", names);
        }
    }

    [Fact]
    public async Task FindUnused_DoesNotReportReferencedMethod()
    {
        using var project = new TestProjectBuilder();
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
        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var results = await FindUnusedCore.AnalyzeAsync(sln, "all");
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("UsedMethod", names);
        }
    }

    [Fact]
    public async Task FindUnused_ExcludesSerializationAttributedTypes()
    {
        using var project = new TestProjectBuilder();
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
        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var results = await FindUnusedCore.AnalyzeAsync(sln, "all");
            var names = results.Select(r => r.Name).ToList();
            Assert.DoesNotContain("SerializableData", names);
        }
    }

    [Fact]
    public async Task FindUnused_ScopeMethods_OnlyReportsMethods()
    {
        using var project = new TestProjectBuilder();
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
        var slnPath = await project.BuildAsync();
        var (workspace, sln) = await WorkspaceLoader.LoadAsync(slnPath);
        using (workspace)
        {
            var results = await FindUnusedCore.AnalyzeAsync(sln, "methods");
            var names = results.Select(r => r.Name).ToList();
            // UnusedClass is a type, not a method — should not appear with scope=methods
            Assert.DoesNotContain("UnusedClass", names);
            // UnusedMethod is a method and unreferenced — should appear
            Assert.Contains("UnusedMethod", names);
        }
    }
}
